// Grid Drop and Remove Duplicates (Connect-4-like board utility)
// Difficulty: Easy part 1, Medium parts 2-3, Medium-Hard once they compose
// Pattern: flood fill (connected components) on a grid + a cached column pointer
//
// An m x n grid. '0' is empty, letters ('R', 'Y', 'B', ...) are pieces. Three
// parts, asked in this order for a reason:
//
//   1. Drop(color, col)     -- the piece falls to the LOWEST empty row in that
//                              column. Column full -> error.
//   2. RemoveDuplicates()   -- every 4-directionally connected group of one
//                              colour with >= 2 pieces is cleared to '0'.
//   3. Gravity              -- survivors fall into the holes, and Drop / Remove
//                              must keep working in ANY interleaving.
//
//       R Y 0        R 0 0            0 0 0
//       B Y Y   ->   0 0 0    --+-->  0 0 0
//       B R 0        0 R 0     |      R R 0
//                              |
//              part 2 output --+-- then gravity (part 3)
//
// THE ONE INSIGHT THE WHOLE INTERVIEW IS BUILT AROUND
//
// Part 1 wants an O(1) drop, which means caching "next free row" per column
// instead of scanning. That cache is not free-standing -- it is only meaningful
// under an INVARIANT:
//
//       every column is a solid stack of pieces sitting on the floor,
//       with nothing but empties above it.
//
// Under that invariant "next free row" is a single number and Drop is O(1).
// Part 2 destroys the invariant: clearing a group punches holes in the middle of
// columns, and now "the lowest empty row" and "the row directly under the stack"
// are two different cells. THAT is why part 3 exists. Gravity is not a cosmetic
// follow-up -- it is what restores the precondition the part-1 optimisation was
// standing on.
//
// So there are only two honest designs, and you should say which one you picked:
//
//   (a) Clearing ALWAYS settles.  RemoveDuplicates = clear + gravity + repair the
//       pointers, as one atomic operation. The invariant never breaks, Drop stays
//       O(1) forever. This is the design below (RemoveDuplicates).
//   (b) Clearing may leave the board unsettled, because the interviewer's own
//       part-2 example output has a floating 'R' in it. Then the cached pointer
//       is INVALID and Drop must fall back to an O(m) column scan until someone
//       settles the board. Also implemented below (ClearGroupsOnly + the
//       _settled flag), because refusing to reproduce the stated example is the
//       wrong way to win the argument.
//
//   The bug to name out loud: keep the cached pointer and clear without gravity,
//   and a column whose TOP piece was just cleared still reports "full" -- the
//   pointer says -1, the cell says empty. It is measured in the tests below.
//
// WHAT TO PIN DOWN BEFORE WRITING ANYTHING
//
//   1. Connectivity: 4-directional or 8? Assumed 4. Diagonals change the answer
//      on real boards and cost nothing but a longer direction array.
//   2. Threshold: ">= 2 pieces" as stated -- NOT the >= 4 a real Connect Four
//      would want. Parameterised as minGroup so the same code answers both.
//   3. Is removal one pass or does it CASCADE? After gravity, pieces that were
//      never adjacent land next to each other and form new groups -- in the
//      example above, the two surviving R's. Match-3 games cascade; the stated
//      problem does not say. Both are here; ask, do not assume.
//   4. Simultaneity: all groups found on the CURRENT board are cleared together.
//      Clearing greedily as you find them changes the answer, because removing
//      one group can never split another (components are disjoint) but it does
//      change what "the board" means for the rest of the sweep.
//   5. Does the return value alias the board? The stated signature returns the
//      grid on every call. Returning a defensive copy makes Drop O(m*n) and
//      quietly kills the O(1) you were just asked for. Return a live view and
//      say so; offer Snapshot() for callers who need isolation.
//   6. Bad input: column out of range, unknown colour, dropping '0'. Throw.
//
// WHY BFS/DFS AND NOT UNION-FIND. Connected components scream DSU, and DSU is
// the wrong tool here: it is a structure for MERGING. This problem's dominant
// operation is deletion -- clearing a group and dropping survivors into new
// adjacencies -- and a disjoint-set forest cannot un-merge. You would rebuild it
// from scratch every round, which is the O(m*n) scan you were trying to avoid,
// plus the constant factor. Flood fill from a stamped seen-array is strictly
// better here.
//
// Complexity
//   Drop                  O(1) settled (cached pointer) / O(m) unsettled (scan)
//   RemoveDuplicates      O(m*n) time, O(m*n) space for the seen stamps, which
//                         are allocated ONCE per board, not once per call
//   Gravity               O(m*n) time, O(1) extra
//   Cascade to fixpoint   O((m*n)^2) worst case, because each round clears >= 2
//                         cells so there are at most m*n/2 rounds; in practice
//                         the incremental version below makes it ~O(m*n) total
//   DropAndResolve        O(size of the affected components), see the invariant
//                         argument on that method

namespace CodingPatterns.Graphs;

public sealed class GridDropAndRemove
{
    public const char Empty = '0';

    /// <summary>Up / down / left / right. Add the four diagonals for 8-connectivity.</summary>
    private static readonly (int Row, int Col)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private readonly char[,] _grid;

    /// <summary>
    /// Row a piece dropped into column c would land on, or -1 for "full". Valid
    /// ONLY while <see cref="_settled"/>; see the header.
    /// </summary>
    private readonly int[] _nextFreeRow;

    /// <summary>
    /// Flood-fill visited marks. One array for the life of the board, stamped
    /// with a monotonic round number instead of being cleared -- clearing it per
    /// call would make every removal allocate and memset m*n, which is the whole
    /// cost of the operation paid twice.
    /// </summary>
    private readonly int[,] _seen;
    private int _stamp;

    /// <summary>Every column is a gap-free stack on the floor, so _nextFreeRow means something.</summary>
    private bool _settled = true;

    private readonly List<(int Row, int Col)> _frontier = new();
    private readonly List<(int Row, int Col)> _component = new();
    private readonly List<(int Row, int Col)> _doomed = new();
    private readonly List<(int Row, int Col)> _touched = new();

    public int Rows { get; }
    public int Cols { get; }

    /// <summary>Minimum connected-group size that gets cleared. 2 as stated; 4 for real Connect Four.</summary>
    public int MinGroup { get; }

    public GridDropAndRemove(int rows, int cols, int minGroup = 2)
    {
        if (rows <= 0 || cols <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows), "board must be at least 1 x 1");
        if (minGroup < 2)
            throw new ArgumentOutOfRangeException(nameof(minGroup), "a group of one is just a piece");

        Rows = rows;
        Cols = cols;
        MinGroup = minGroup;

        _grid = new char[rows, cols];
        _seen = new int[rows, cols];
        _nextFreeRow = new int[cols];

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                _grid[r, c] = Empty;

        for (int c = 0; c < cols; c++)
            _nextFreeRow[c] = rows - 1;
    }

    // ------------------------------------------------------------- part 1: drop

    /// <summary>
    /// Drop <paramref name="color"/> into <paramref name="col"/>; it lands on the
    /// lowest empty row. Returns the board as a LIVE view (see contract note 5).
    ///
    /// O(1) on a settled board. On an unsettled one -- which only happens if the
    /// caller used <see cref="ClearGroupsOnly"/> -- the cached pointer is a lie
    /// and this scans the column instead. That is the honest fallback: the answer
    /// stays correct and only the complexity degrades, which is the right way
    /// round for a cache whose precondition someone else broke.
    /// </summary>
    public char[,] Drop(char color, int col)
    {
        if (col < 0 || col >= Cols)
            throw new ArgumentOutOfRangeException(nameof(col), $"column {col} is outside 0..{Cols - 1}");
        if (color == Empty)
            throw new ArgumentException("'0' is the empty marker, not a colour", nameof(color));

        int row = _settled ? _nextFreeRow[col] : LowestEmptyRow(col);
        if (row < 0)
            throw new InvalidOperationException($"column {col} is full");

        _grid[row, col] = color;

        if (_settled)
            _nextFreeRow[col] = row - 1;                // the O(1) that part 1 is asking for

        return _grid;
    }

    /// <summary>The O(m) fallback: bottom-up, first empty cell wins.</summary>
    private int LowestEmptyRow(int col)
    {
        for (int r = Rows - 1; r >= 0; r--)
            if (_grid[r, col] == Empty)
                return r;

        return -1;
    }

    // --------------------------------------------------- part 2: remove groups

    /// <summary>
    /// Clear every connected same-colour group of at least <see cref="MinGroup"/>
    /// pieces, then settle the board (part 3) so the column pointers stay honest.
    /// One pass -- groups formed BY the gravity are left alone; see
    /// <see cref="RemoveDuplicatesCascade"/> for the fixpoint.
    /// </summary>
    public char[,] RemoveDuplicates()
    {
        ClearGroups(AllCells(), _touched);
        ApplyGravity(null);
        return _grid;
    }

    /// <summary>
    /// Part 2 exactly as stated: clear the groups and stop, leaving pieces
    /// floating -- which is what the interviewer's own example output shows.
    ///
    /// The board is now UNSETTLED, and this is where the interesting bug lives.
    /// Keeping the cached _nextFreeRow after this call is wrong in both
    /// directions: a column whose top piece was cleared still reports "full",
    /// and a column with a hole punched under a survivor would hand out a row
    /// that is already occupied. So the flag comes down and Drop starts scanning
    /// until someone calls <see cref="Settle"/>. Correctness is preserved by
    /// admitting the cache is invalid, not by patching the number.
    /// </summary>
    public char[,] ClearGroupsOnly()
    {
        int cleared = ClearGroups(AllCells(), _touched);
        if (cleared > 0)
            _settled = false;

        return _grid;
    }

    /// <summary>
    /// Flood fill from <paramref name="seeds"/>, collecting every component of
    /// size >= MinGroup, then clear them all AT ONCE. Two-phase on purpose:
    /// components are disjoint so clearing during the sweep could not corrupt a
    /// later one, but it would make the operation's meaning depend on scan order,
    /// and "all groups on the board as it stands" is the rule that is actually
    /// stated. Returns the number of cells cleared; appends them to
    /// <paramref name="cleared"/> when the caller wants the seed set for the next
    /// round.
    /// </summary>
    private int ClearGroups(IEnumerable<(int Row, int Col)> seeds, List<(int Row, int Col)> cleared)
    {
        _stamp++;
        _doomed.Clear();
        cleared?.Clear();

        foreach (var (seedRow, seedCol) in seeds)
        {
            char color = _grid[seedRow, seedCol];
            if (color == Empty || _seen[seedRow, seedCol] == _stamp)
                continue;

            // BFS, not recursive DFS: a one-colour board is a single component of
            // m*n cells, and that is a stack overflow on any grid worth the name.
            _frontier.Clear();
            _component.Clear();
            _seen[seedRow, seedCol] = _stamp;
            _frontier.Add((seedRow, seedCol));
            _component.Add((seedRow, seedCol));

            for (int head = 0; head < _frontier.Count; head++)
            {
                var (row, col) = _frontier[head];
                foreach (var (dr, dc) in Neighbours)
                {
                    int nr = row + dr, nc = col + dc;
                    if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols)
                        continue;
                    if (_seen[nr, nc] == _stamp || _grid[nr, nc] != color)
                        continue;

                    _seen[nr, nc] = _stamp;             // mark on ENQUEUE, or the same
                    _frontier.Add((nr, nc));            // cell rides the queue once per
                    _component.Add((nr, nc));           // neighbour that reaches it
                }
            }

            if (_component.Count >= MinGroup)
                _doomed.AddRange(_component);
        }

        foreach (var (row, col) in _doomed)
            _grid[row, col] = Empty;

        cleared?.AddRange(_doomed);
        return _doomed.Count;
    }

    private IEnumerable<(int Row, int Col)> AllCells()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                yield return (r, c);
    }

    // ------------------------------------------------------------ part 3: fall

    /// <summary>
    /// Compact every column onto the floor and rebuild the pointers. Idempotent,
    /// so calling it on an already-settled board costs a scan and changes nothing.
    /// </summary>
    public char[,] Settle()
    {
        ApplyGravity(null);
        return _grid;
    }

    /// <summary>
    /// Two-pointer compaction per column, bottom-up. <paramref name="moved"/>, if
    /// given, collects every cell whose CONTENTS changed -- both the vacated
    /// source and the filled destination -- which is exactly the seed set the
    /// incremental cascade needs.
    /// </summary>
    private void ApplyGravity(List<(int Row, int Col)> moved)
    {
        for (int col = 0; col < Cols; col++)
        {
            int write = Rows - 1;

            for (int read = Rows - 1; read >= 0; read--)
            {
                if (_grid[read, col] == Empty)
                    continue;

                if (write != read)
                {
                    _grid[write, col] = _grid[read, col];
                    _grid[read, col] = Empty;
                    moved?.Add((write, col));
                    moved?.Add((read, col));
                }

                write--;
            }

            // No zero-fill loop above `write` is needed, and it is worth being
            // able to say why rather than adding one defensively: write only
            // decrements when a piece is placed, so when a piece is read at row
            // r the write head was at >= r and ends at <= r - 1. Every occupied
            // row therefore ends up strictly below the final write head, and
            // every source row was either already empty or explicitly vacated.
            _nextFreeRow[col] = write;
        }

        _settled = true;
    }

    // ----------------------------------------------- composing them: the cascade

    /// <summary>
    /// Clear, settle, and repeat until a round finds nothing -- the match-3
    /// reading of the problem. <paramref name="rounds"/> counts the rounds that
    /// actually cleared something.
    ///
    /// It terminates because every counted round removes at least MinGroup >= 2
    /// pieces and nothing ever adds one, so there are fewer than m*n/2 of them.
    /// That bound is also the worst-case complexity, O((m*n)^2), and it is not
    /// reachable in practice -- see <see cref="DropAndResolve"/> for why the real
    /// cost is proportional to what actually moved.
    /// </summary>
    public char[,] RemoveDuplicatesCascade(out int rounds)
    {
        rounds = 0;

        while (ClearGroups(AllCells(), null) > 0)
        {
            rounds++;
            ApplyGravity(null);
        }

        // The last round cleared nothing but the board may still be unsettled if
        // the caller handed us one; settle unconditionally so the postcondition
        // ("reduced AND settled") holds on every exit path.
        ApplyGravity(null);
        return _grid;
    }

    /// <summary>
    /// Drop a piece and cascade, touching only the cells that could possibly have
    /// changed. This is the efficiency follow-up, and it rests on an invariant the
    /// board maintains for itself:
    ///
    ///     between calls, the board is REDUCED (no group of >= MinGroup exists)
    ///     and SETTLED.
    ///
    /// Given that, a full m*n rescan is wasted work:
    ///
    ///   * After the drop, the only cell that changed is the one piece. Any new
    ///     group must contain it, because a group not containing it existed
    ///     before the drop and the board was reduced. So round 1 seeds from the
    ///     dropped cell alone.
    ///   * Clearing removes whole components, so the survivors are exactly the
    ///     components that were already under-sized: still reduced.
    ///   * After gravity, any new group must contain a cell whose contents
    ///     changed -- if none of its cells moved, the identical group existed in
    ///     the reduced pre-gravity board, contradiction. So each later round
    ///     seeds from the cells gravity touched.
    ///
    /// Cost is therefore proportional to the components actually involved plus
    /// O(m*n) for the gravity scans, instead of a fresh O(m*n) flood fill per
    /// round. The randomised tests below check it against the full-scan cascade
    /// on every board they generate, because an invariant argument that is not
    /// tested is a hypothesis.
    /// </summary>
    public char[,] DropAndResolve(char color, int col, out int rounds)
    {
        if (!_settled)
            ApplyGravity(null);                          // restore the precondition first

        Drop(color, col);
        rounds = 0;

        int row = _settled ? _nextFreeRow[col] + 1 : LowestEmptyRow(col) + 1;
        var seeds = new List<(int Row, int Col)> { (row, col) };

        while (seeds.Count > 0 && ClearGroups(seeds, null) > 0)
        {
            rounds++;
            _touched.Clear();
            ApplyGravity(_touched);
            seeds = new List<(int Row, int Col)>(_touched);
        }

        return _grid;
    }

    // ------------------------------------------------------------ inspection

    public char this[int row, int col] => _grid[row, col];

    /// <summary>Row a drop into this column would land on, or -1 if full. O(1) or O(m), as above.</summary>
    public int NextFreeRow(int col) => _settled ? _nextFreeRow[col] : LowestEmptyRow(col);

    public bool IsColumnFull(int col) => NextFreeRow(col) < 0;

    public bool IsSettled => _settled;

    /// <summary>Detached copy, for callers who must not see later mutations.</summary>
    public char[,] Snapshot() => (char[,])_grid.Clone();

    public string Render()
    {
        var lines = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            var cells = new char[Cols];
            for (int c = 0; c < Cols; c++)
                cells[c] = _grid[r, c];

            lines[r] = string.Join(' ', cells);
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Build a board from rows of characters, e.g. "RY0". Derives the pointers
    /// from the contents and reports honestly whether the result is settled --
    /// the interviewer's own part-2 example is NOT, which is the point.
    /// </summary>
    public static GridDropAndRemove FromRows(int minGroup, params string[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Length == 0)
            throw new ArgumentException("need at least one row", nameof(rows));

        int cols = rows[0].Length;
        var board = new GridDropAndRemove(rows.Length, cols, minGroup);

        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r].Length != cols)
                throw new ArgumentException("rows must be the same length", nameof(rows));

            for (int c = 0; c < cols; c++)
                board._grid[r, c] = rows[r][c];
        }

        board._settled = board.LooksSettled();
        if (board._settled)
            for (int c = 0; c < cols; c++)
                board._nextFreeRow[c] = board.LowestEmptyRow(c);

        return board;
    }

    public static GridDropAndRemove FromRows(params string[] rows) => FromRows(2, rows);

    /// <summary>No column has a piece with an empty cell beneath it.</summary>
    public bool LooksSettled()
    {
        for (int col = 0; col < Cols; col++)
        {
            bool sawEmpty = false;
            for (int row = Rows - 1; row >= 0; row--)
            {
                if (_grid[row, col] == Empty)
                    sawEmpty = true;
                else if (sawEmpty)
                    return false;                        // a piece floating over a hole
            }
        }

        return true;
    }

    /// <summary>No connected same-colour group reaches MinGroup. The other postcondition.</summary>
    public bool IsReduced()
    {
        int savedStamp = _stamp;
        var seen = new bool[Rows, Cols];

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                char color = _grid[r, c];
                if (color == Empty || seen[r, c])
                    continue;

                var queue = new Queue<(int, int)>();
                seen[r, c] = true;
                queue.Enqueue((r, c));
                int size = 0;

                while (queue.Count > 0)
                {
                    var (row, col) = queue.Dequeue();
                    size++;
                    foreach (var (dr, dc) in Neighbours)
                    {
                        int nr = row + dr, nc = col + dc;
                        if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols)
                            continue;
                        if (seen[nr, nc] || _grid[nr, nc] != color)
                            continue;

                        seen[nr, nc] = true;
                        queue.Enqueue((nr, nc));
                    }
                }

                if (size >= MinGroup)
                    return false;
            }
        }

        _stamp = savedStamp;
        return true;
    }

    /// <summary>The cached pointers agree with the board. Used only by the tests.</summary>
    public bool PointersAgree()
    {
        if (!_settled)
            return true;                                 // nothing is claimed while unsettled

        for (int col = 0; col < Cols; col++)
            if (_nextFreeRow[col] != LowestEmptyRow(col))
                return false;

        return true;
    }

    // ---------------------------------------------------------------- tests

    private static int _checks;
    private static int _failures;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (condition)
            return;

        _failures++;
        Console.WriteLine($"  FAIL: {what}");
    }

    public static void Main()
    {
        _checks = _failures = 0;

        Console.WriteLine("== part 1: drop, and the fourth one that must fail ==");

        var board = new GridDropAndRemove(3, 3);
        board.Drop('Y', 1);
        Check(board[2, 1] == 'Y', "first Y lands on the floor");
        board.Drop('Y', 1);
        board.Drop('Y', 1);
        Console.WriteLine(Indent(board.Render()));
        Check(board[0, 1] == 'Y' && board[1, 1] == 'Y' && board[2, 1] == 'Y', "column 1 fills bottom-up");
        Check(board.IsColumnFull(1), "column 1 reports full");

        bool threw = false;
        try { board.Drop('Y', 1); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, "the fourth drop into a full column throws");

        threw = false;
        try { board.Drop('Y', 3); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "an out-of-range column throws");

        threw = false;
        try { board.Drop(Empty, 0); }
        catch (ArgumentException) { threw = true; }
        Check(threw, "dropping the empty marker throws");

        Console.WriteLine();
        Console.WriteLine("== part 2: the interviewer's example, cleared but not settled ==");

        var example = FromRows("RY0",
                               "BYY",
                               "BR0");
        Console.WriteLine("  before:");
        Console.WriteLine(Indent(example.Render(), 4));
        Check(!example.IsSettled, "the given example is NOT gravity-consistent to begin with");

        example.ClearGroupsOnly();
        Console.WriteLine("  after ClearGroupsOnly:");
        Console.WriteLine(Indent(example.Render(), 4));
        Check(example.Render() == "R 0 0\n0 0 0\n0 R 0", "matches the stated part-2 output exactly");
        Check(example.IsReduced(), "no group of 2+ survives");
        Check(!example.IsSettled, "clearing without gravity leaves the board unsettled");

        Console.WriteLine();
        Console.WriteLine("== the stale-pointer bug that part 3 exists to prevent ==");

        // A full column whose top piece is then cleared. A cached "next free row"
        // that survives the clear says -1; the board says row 0 is empty.
        var stale = new GridDropAndRemove(3, 1);
        stale.Drop('B', 0);
        stale.Drop('R', 0);
        stale.Drop('R', 0);                              // R R on top of B: rows 0 and 1
        Check(stale.IsColumnFull(0), "column starts full");

        stale.ClearGroupsOnly();                         // the two R's go, B stays on the floor
        Console.WriteLine(Indent(stale.Render(), 4));
        Check(stale[2, 0] == 'B' && stale[1, 0] == Empty && stale[0, 0] == Empty, "only the R pair cleared");
        Check(!stale.IsColumnFull(0), "the column is no longer full -- a kept pointer would still say -1");
        Check(stale.NextFreeRow(0) == 1, "the scan fallback finds row 1, the lowest empty cell");

        stale.Drop('Y', 0);
        Check(stale[1, 0] == 'Y', "the drop after an unsettled clear lands correctly anyway");

        stale.Settle();
        Check(stale.IsSettled && stale.PointersAgree(), "Settle restores the invariant and the pointers");

        Console.WriteLine();
        Console.WriteLine("== part 3: gravity, and what it creates ==");

        var settling = FromRows("RY0",
                                "BYY",
                                "BR0");
        settling.RemoveDuplicates();                     // clear + gravity, one pass
        Console.WriteLine(Indent(settling.Render(), 4));
        Check(settling.Render() == "0 0 0\n0 0 0\nR R 0", "the survivors fall onto the floor");
        Check(settling.IsSettled && settling.PointersAgree(), "pointers rebuilt by gravity");
        Check(!settling.IsReduced(), "and gravity has just CREATED a new R pair -- hence the cascade question");

        Console.WriteLine();
        Console.WriteLine("== cascade to a fixpoint ==");

        var cascading = FromRows("RY0",
                                 "BYY",
                                 "BR0");
        cascading.RemoveDuplicatesCascade(out int rounds);
        Console.WriteLine(Indent(cascading.Render(), 4));
        Console.WriteLine($"    rounds that cleared something: {rounds}");
        Check(rounds == 2, "two clearing rounds: the Y/B groups, then the R pair gravity made");
        Check(cascading.Render() == "0 0 0\n0 0 0\n0 0 0", "the board empties completely");
        Check(cascading.IsReduced() && cascading.IsSettled, "fixpoint: reduced and settled");

        Console.WriteLine();
        Console.WriteLine("== threshold is a parameter, not a constant ==");

        var connectFour = FromRows(4, "0000",
                                      "0000",
                                      "R00R",
                                      "RRRR");
        connectFour.RemoveDuplicates();
        Console.WriteLine(Indent(connectFour.Render(), 4));
        Check(connectFour.Render() == "0 0 0 0\n0 0 0 0\n0 0 0 0\n0 0 0 0",
            "minGroup 4: the whole L-shaped component of 6 goes, it is ONE group");

        var pairs = FromRows(4, "0000",
                                "0000",
                                "0000",
                                "RR0R");
        pairs.RemoveDuplicates();
        Check(pairs.Render() == "0 0 0 0\n0 0 0 0\n0 0 0 0\nR R 0 R", "minGroup 4: a pair is not enough");

        Console.WriteLine();
        Console.WriteLine("== interleaving drop / remove / drop, which is the real part 3 ==");

        var mixed = new GridDropAndRemove(4, 4);
        mixed.Drop('R', 0);
        mixed.Drop('R', 0);
        mixed.RemoveDuplicates();                        // both R's go, column empties
        Check(mixed.NextFreeRow(0) == 3, "the emptied column takes a drop on the floor again");
        mixed.Drop('B', 0);
        Check(mixed[3, 0] == 'B', "and it lands there");
        Check(mixed.PointersAgree(), "pointers still agree after clear -> drop");

        Console.WriteLine();
        Console.WriteLine("== single colour: one component of the entire board ==");

        var mono = new GridDropAndRemove(50, 50);
        for (int c = 0; c < 50; c++)
            for (int r = 0; r < 50; r++)
                mono.Drop('R', c);

        mono.RemoveDuplicates();
        bool allEmpty = true;
        for (int r = 0; r < 50; r++)
            for (int c = 0; c < 50; c++)
                allEmpty &= mono[r, c] == Empty;

        Check(allEmpty, "2500 cells, one flood fill, no recursion to overflow");
        Check(mono.NextFreeRow(0) == 49, "pointers reset to the floor");

        Console.WriteLine();
        Console.WriteLine("== 1 x 1, and other degenerate boards ==");

        var tiny = new GridDropAndRemove(1, 1);
        tiny.Drop('R', 0);
        tiny.RemoveDuplicates();
        Check(tiny[0, 0] == 'R', "a lone piece is not a group");
        Check(tiny.IsColumnFull(0), "and the 1 x 1 board is still full");

        var column = new GridDropAndRemove(5, 1);
        column.Drop('R', 0);
        column.Drop('B', 0);
        column.Drop('B', 0);
        column.Drop('R', 0);
        column.RemoveDuplicates();
        Check(column.Render() == "0\n0\n0\nR\nR", "the B pair goes and the two R's meet -- one pass leaves them");
        Check(!column.IsReduced(), "which is exactly the state the cascade would keep working on");

        Console.WriteLine();
        Console.WriteLine("== randomised: incremental resolve == full-scan cascade ==");

        var rng = new Random(20260811);
        bool agreed = true;
        int totalDrops = 0;

        for (int trial = 0; trial < 400; trial++)
        {
            int rows = 1 + rng.Next(6), cols = 1 + rng.Next(6);
            int minGroup = 2 + rng.Next(3);
            var colors = "RYB".Substring(0, 1 + rng.Next(3));

            var incremental = new GridDropAndRemove(rows, cols, minGroup);
            var fullScan = new GridDropAndRemove(rows, cols, minGroup);

            for (int move = 0; move < 40; move++)
            {
                int col = rng.Next(cols);
                if (incremental.IsColumnFull(col))
                    continue;

                char color = colors[rng.Next(colors.Length)];
                totalDrops++;

                incremental.DropAndResolve(color, col, out _);

                fullScan.Drop(color, col);
                fullScan.RemoveDuplicatesCascade(out _);

                agreed &= SameBoard(incremental, fullScan);
                agreed &= incremental.IsReduced() && incremental.IsSettled && incremental.PointersAgree();
                agreed &= incremental.LooksSettled();

                if (!agreed)
                    break;
            }

            if (!agreed)
                break;
        }

        Console.WriteLine($"    400 random boards, {totalDrops} drops: agree = {agreed}");
        Check(agreed, "seeding only from the dropped piece and the cells gravity moved is complete");

        Console.WriteLine();
        Console.WriteLine("== the returned grid is a live view, not a copy ==");

        var aliased = new GridDropAndRemove(2, 2);
        char[,] view = aliased.Drop('R', 0);
        char[,] snapshot = aliased.Snapshot();
        aliased.Drop('B', 1);
        Check(view[1, 1] == 'B', "the returned array tracks later mutations");
        Check(snapshot[1, 1] == Empty, "Snapshot does not -- copy only when you need to, or Drop is O(m*n)");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"All {_checks} checks passed."
            : $"{_failures} of {_checks} checks FAILED.");
    }

    private static bool SameBoard(GridDropAndRemove a, GridDropAndRemove b)
    {
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            return false;

        for (int r = 0; r < a.Rows; r++)
            for (int c = 0; c < a.Cols; c++)
                if (a[r, c] != b[r, c])
                    return false;

        return true;
    }

    private static string Indent(string text, int spaces = 2)
    {
        var pad = new string(' ', spaces);
        return pad + text.Replace("\n", "\n" + pad);
    }
}

// ---- Notes for the follow-up questions ----
//
// "Make removal incremental -- do not rescan the whole board after one drop."
//     DropAndResolve above. The invariant "the board is reduced and settled
//     between calls" is what licenses it: a new group must contain a cell that
//     changed, so the seeds are the dropped piece, then whatever gravity moved.
//     Say the invariant out loud before writing the optimisation; without it the
//     seeded scan is just a guess that happens to pass small tests.
//
// "The board is enormous and mostly empty."
//     char[,] is m*n bytes regardless of occupancy. Switch to per-column
//     stacks -- List<char> per column, index 0 at the floor -- and the memory
//     becomes O(pieces), Drop is a push, gravity is free because a stack cannot
//     have holes, and clearing a group is the only awkward operation (a RemoveAt
//     in the middle, O(height)). That representation makes part 3 vanish and
//     part 2 harder, which is a genuinely interesting trade to offer.
//
// "Undo the last move."
//     Do not snapshot the board per move -- store the diff. Each operation is a
//     list of (row, col, oldChar), which is O(cells touched) rather than O(m*n),
//     and a drop's diff is a single entry. Replaying backwards restores both the
//     grid and, since gravity is deterministic, the pointers via one Settle.
//
// "Diagonal connectivity."
//     Add the four diagonal offsets to Neighbours. Nothing else changes -- which
//     is the payoff for having the direction set be data instead of eight
//     hand-written if statements.
//
// "Two players drop concurrently."
//     Column-level locks are tempting and wrong: a clear spans columns, so a
//     removal that touches columns 2 and 3 while someone drops into 3 can
//     interleave into a board no sequence of legal moves could produce. Take one
//     lock over the whole board for the duration of drop+cascade, or serialise
//     moves through a single-threaded queue and let clients read Snapshot()s. The
//     board is small and the operations are microseconds; there is nothing to win
//     by being clever here, and the failure mode is silent corruption.
//
// "How do you test this?"
//     Exactly as above: the properties are the tests. After every operation the
//     board must be REDUCED and SETTLED and its pointers must agree with its
//     contents -- three predicates, checkable in O(m*n), that between them pin
//     down almost every bug this problem admits. Randomised move sequences
//     checking those three invariants find the stale-pointer and incremental-seed
//     bugs immediately; hand-written examples find neither.
