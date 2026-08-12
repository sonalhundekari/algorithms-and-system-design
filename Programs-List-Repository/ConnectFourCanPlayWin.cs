/*
Connect Four -- Can Play Win

An m x n board of '.', 'a', 'b'. Given a move (x, y, player): drop the piece at
board[x][y] and say whether that move wins, i.e. whether it completes a run of 4
or more identical pieces horizontally, vertically, on the '\' diagonal, or on
the '/' anti-diagonal. If board[x][y] is already occupied the move is illegal
and the answer is false.

The whole problem is one observation: the move can only create a win THROUGH the
cell it landed on. Any winning line that does not contain (x, y) was already
there before the move, so it is not something this move did. That collapses a
full-board scan into four cheap probes from a single cell.

For each of the four AXES -- not eight directions -- walk outward both ways and
count matching pieces:

    length = 1 (the piece just placed)
           + run(x, y,  dr,  dc)
           + run(x, y, -dr, -dc)

    axes: (0,1) horizontal   (1,0) vertical   (1,1) '\'   (1,-1) '/'

Win iff length >= 4 on any axis. Three things people get wrong here:

  1. Eight directions instead of four axes, which double-counts every line
     (the '\' win gets found twice) -- harmless, but it usually comes with
  2. forgetting the `+ 1`, or adding it twice, so "a a a [new] " reports 3 or 5.
     The placed piece belongs to both half-runs and must be counted exactly once.
  3. Only walking forward from the placed cell. Placing into the middle of
     "a a . a" is a win, and a forward-only scan sees a run of 2.

Also worth saying out loud: >= 4, not == 4. A 5-in-a-row is still a win, and
`== 4` fails on any board where a line longer than four is reachable.

Each half-run stops after 3 steps, because a 4th matching piece in one direction
already means the answer is yes and nothing beyond it can change the verdict. So
the check touches at most 4 axes * 2 directions * 3 cells = 24 cells regardless
of board size.

Complexity
  Time   O(1) per move -- at most 24 cell reads, independent of m and n.
         (A full-board rescan would be O(m * n * 4) and answers a different,
         weaker question: "does a win exist", not "did this move win".)
  Space  O(1), and the board is updated in place.

Contract questions worth asking before writing anything:

  - Does a losing move still leave the piece on the board? Assumed yes: the move
    happened, it just did not win. An ILLEGAL move (occupied cell) leaves the
    board untouched. That asymmetry is deliberate and is the only mutation rule
    in the problem.
  - Can the board already contain a win before this move? In a real game no,
    which is why "through the placed cell" is the right test. If the input is
    arbitrary, say explicitly that a pre-existing line the move does not touch
    is not reported -- see WouldWin/HasAnyWin below for the other question.
  - Is this really Connect Four, i.e. does gravity apply? The stated problem
    places at an arbitrary (x, y), so no. The gravity variant is Drop() below,
    where the caller picks only a COLUMN and the row is derived.
*/

namespace CodingPatterns.ArraysStrings;

public static class ConnectFourCanPlayWin
{
    public const char Empty = '.';
    public const int Connect = 4;

    /// <summary>The four axes. Each is walked in both directions, so this covers all eight.</summary>
    private static readonly (int Row, int Col)[] Axes = { (0, 1), (1, 0), (1, 1), (1, -1) };

    /// <summary>
    /// Places <paramref name="player"/> at board[x][y] and reports whether that
    /// move wins. Returns false without touching the board when the cell is
    /// already occupied.
    /// </summary>
    public static bool CanPlayWin(char[][] board, int x, int y, char player)
    {
        Validate(board, x, y, player);

        if (board[x][y] != Empty)
            return false;                       // illegal move: no mutation, no win

        board[x][y] = player;                   // the move happens either way
        return IsWinningCell(board, x, y);
    }

    /// <summary>
    /// Same verdict without keeping the piece: useful for "which of my legal
    /// moves win?" lookahead. Restores the cell before returning.
    /// </summary>
    public static bool WouldWin(char[][] board, int x, int y, char player)
    {
        Validate(board, x, y, player);

        if (board[x][y] != Empty)
            return false;

        board[x][y] = player;
        bool won = IsWinningCell(board, x, y);
        board[x][y] = Empty;
        return won;
    }

    /// <summary>
    /// Does any line of <see cref="Connect"/> pass through (x, y)? The cell must
    /// already hold a piece; an empty cell is never part of a line.
    /// </summary>
    public static bool IsWinningCell(char[][] board, int x, int y)
    {
        char player = board[x][y];
        if (player == Empty)
            return false;

        foreach (var (dr, dc) in Axes)
        {
            // The placed piece is shared by both half-runs -- count it once.
            int length = 1
                       + Run(board, x, y, dr, dc)
                       + Run(board, x, y, -dr, -dc);

            if (length >= Connect)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matching pieces strictly beyond (r, c) along (dr, dc). Capped at
    /// Connect - 1: a 4th consecutive piece on one side already decides the
    /// answer, so counting further is wasted work.
    /// </summary>
    private static int Run(char[][] board, int r, int c, int dr, int dc)
    {
        char player = board[r][c];
        int matched = 0;

        for (int step = 1; step < Connect; step++)
        {
            int nr = r + dr * step, nc = c + dc * step;

            if (nr < 0 || nr >= board.Length || nc < 0 || nc >= board[nr].Length)
                break;
            if (board[nr][nc] != player)
                break;

            matched++;
        }

        return matched;
    }

    // ------------------------------------------------------------ whole board

    /// <summary>
    /// The other question: does a line exist ANYWHERE for <paramref name="player"/>,
    /// regardless of who moved last. O(m * n). Not what CanPlayWin answers, and
    /// conflating the two is the usual source of "it says I won and I didn't".
    /// </summary>
    public static bool HasAnyWin(char[][] board, char player)
    {
        for (int r = 0; r < (board?.Length ?? 0); r++)
            for (int c = 0; c < board[r].Length; c++)
                if (board[r][c] == player && IsWinningCell(board, r, c))
                    return true;

        return false;
    }

    // -------------------------------------------------- gravity (real game)

    /// <summary>Column is full.</summary>
    public const int ColumnFull = -1;

    /// <summary>
    /// The actual Connect Four move: the caller names a COLUMN and the piece
    /// falls to the lowest empty row (largest index). Returns the landing row,
    /// or <see cref="ColumnFull"/>. Note the row is derived, never supplied --
    /// which is exactly why the stated problem, taking both x and y, is a
    /// board-scan exercise wearing a Connect Four costume.
    /// </summary>
    public static int Drop(char[][] board, int column, char player, out bool won)
    {
        won = false;
        int rows = board?.Length ?? 0;
        if (rows == 0)
            return ColumnFull;
        if (column < 0 || column >= board[0].Length)
            throw new ArgumentOutOfRangeException(nameof(column), $"column {column} outside 0..{board[0].Length - 1}");

        for (int r = rows - 1; r >= 0; r--)
        {
            if (board[r][column] != Empty)
                continue;

            board[r][column] = player;
            won = IsWinningCell(board, r, column);
            return r;
        }

        return ColumnFull;                      // stack reached the top
    }

    // ---------------------------------------------------------------- guards

    private static void Validate(char[][] board, int x, int y, char player)
    {
        if (board is null || board.Length == 0)
            throw new ArgumentException("board must have at least one row", nameof(board));
        if (player == Empty)
            throw new ArgumentException($"'{Empty}' is the empty marker, not a player", nameof(player));
        if (x < 0 || x >= board.Length)
            throw new ArgumentOutOfRangeException(nameof(x), $"row {x} outside 0..{board.Length - 1}");
        if (board[x] is null || y < 0 || y >= board[x].Length)
            throw new ArgumentOutOfRangeException(nameof(y), $"column {y} outside row {x}");
    }

    // ----------------------------------------------------------------- tests

    public static void Main()
    {
        Console.WriteLine("Connect Four -- Can Play Win");
        Console.WriteLine("============================");

        var cases = new (string Name, string[] Board, int X, int Y, char Player, bool Expected)[]
        {
            // The two given cases.
            ("case 1: horizontal completion",
                new[] { "aaa.", "...." }, 0, 3, 'a', true),
            ("case 2: nothing lines up",
                new[] { "ab..", "ba..", "....", "...." }, 2, 2, 'a', false),

            // One per axis, each completed from the far end.
            ("vertical",
                new[] { "a...", "a...", "a...", "...." }, 3, 0, 'a', true),
            ("diagonal '\\'",
                new[] { "a...", ".a..", "..a.", "...." }, 3, 3, 'a', true),
            ("anti-diagonal '/'",
                new[] { "...b", "..b.", ".b..", "...." }, 3, 0, 'b', true),

            // The bugs the two-sided walk exists to catch.
            ("fills a GAP in the middle (a a . a)",
                new[] { "aa.a" }, 0, 2, 'a', true),
            ("gap on the '\\' diagonal",
                new[] { "a...", "....", "..a.", "...a" }, 1, 1, 'a', true),
            ("forward-only scan would say no",
                new[] { "aaa." }, 0, 3, 'a', true),

            // >= 4, not == 4.
            ("five in a row still wins",
                new[] { "aaaa.a" }, 0, 4, 'a', true),
            ("extends an existing four",
                new[] { ".aaaa" }, 0, 0, 'a', true),

            // Near misses.
            ("blocked by b on the far side",
                new[] { "baa.b" }, 0, 3, 'a', false),
            ("three is not four",
                new[] { "aa..", "...." }, 0, 2, 'a', false),
            ("opponent's line does not count",
                new[] { "bbb." }, 0, 3, 'a', false),
            ("mixed line a b a a a",
                new[] { "ab.aa" }, 0, 2, 'a', false),

            // Illegal move: occupied cell, either colour.
            ("occupied by self",
                new[] { "aaaa", "...." }, 0, 3, 'a', false),
            ("occupied by opponent",
                new[] { "aaab", "...." }, 0, 3, 'a', false),

            // Boards too small to ever hold a line.
            ("1x1 board",
                new[] { "." }, 0, 0, 'a', false),
            ("3-wide board can never win horizontally",
                new[] { "aa." }, 0, 2, 'a', false),
            ("1x4 board is exactly enough",
                new[] { "aa.a" }, 0, 2, 'a', true),

            // Corners and edges: the walk must not run off the board.
            ("top-left corner, diagonal down-right",
                new[] { "....", ".a..", "..a.", "...a" }, 0, 0, 'a', true),
            ("bottom-right corner",
                new[] { "a...", ".a..", "..a.", "...." }, 3, 3, 'a', true),
        };

        int failures = 0;

        foreach (var (name, rows, x, y, player, expected) in cases)
        {
            var board = Grid(rows);
            bool actual = CanPlayWin(board, x, y, player);
            bool ok = actual == expected;
            failures += ok ? 0 : 1;

            Console.WriteLine();
            Console.WriteLine($"{name}: play '{player}' at ({x},{y}) -> {actual} (expect {expected}) [{(ok ? "OK" : "MISMATCH!")}]");
            PrintSideBySide(rows, board);
        }

        // Mutation contract: a legal losing move still leaves the piece behind,
        // an illegal move leaves the board exactly as it was.
        Console.WriteLine();
        Console.WriteLine("-- mutation contract --");

        var losing = Grid("....", "....");
        CanPlayWin(losing, 1, 2, 'b');
        Console.WriteLine($"  legal move that does not win keeps the piece: '{losing[1][2]}' (expect 'b')");

        var occupied = Grid("ab..", "....");
        CanPlayWin(occupied, 0, 1, 'a');
        Console.WriteLine($"  illegal move leaves the cell untouched: '{occupied[0][1]}' (expect 'b')");

        var probe = Grid("aaa.");
        bool lookahead = WouldWin(probe, 0, 3, 'a');
        Console.WriteLine($"  WouldWin reports {lookahead} and restores the cell: '{probe[0][3]}' (expect '.')");

        // Guards.
        Console.WriteLine();
        Console.WriteLine("-- rejected inputs --");
        foreach (var (label, action) in new (string, Action)[]
        {
            ("row out of range",    () => CanPlayWin(Grid("..."), 5, 0, 'a')),
            ("column out of range", () => CanPlayWin(Grid("..."), 0, 9, 'a')),
            ("'.' as a player",     () => CanPlayWin(Grid("..."), 0, 0, '.')),
            ("empty board",         () => CanPlayWin(Array.Empty<char[]>(), 0, 0, 'a')),
        })
        {
            try
            {
                action();
                Console.WriteLine($"  {label}: NOT REJECTED!");
                failures++;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  {label}: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        // A win somewhere on the board is not the same as a win from THIS move.
        Console.WriteLine();
        Console.WriteLine("-- 'did this move win' vs 'does a win exist' --");
        var preexisting = Grid("aaaa", "....");
        bool moveWins = CanPlayWin(preexisting, 1, 0, 'a');
        Console.WriteLine($"  board already holds a row of 4; playing (1,0) -> CanPlayWin {moveWins} (expect False)");
        Console.WriteLine($"  HasAnyWin(board, 'a') -> {HasAnyWin(preexisting, 'a')} (expect True) -- different question");

        // Gravity variant.
        Console.WriteLine();
        Console.WriteLine("-- follow-up: real gravity (caller picks a column) --");
        var live = Grid("....", "....", "....", "....");
        foreach (int col in new[] { 0, 1, 0, 1, 0, 1, 0 })
        {
            char who = col == 0 ? 'a' : 'b';
            int landed = Drop(live, col, who, out bool won);
            Console.WriteLine($"  '{who}' drops in column {col} -> row {landed}, win {won}");
            if (won)
                break;
        }
        foreach (var row in live)
            Console.WriteLine($"    {new string(row)}");

        var full = Grid("a", "a", "a", "a");
        Console.WriteLine($"  dropping into a full column -> {Drop(full, 0, 'b', out _)} (expect {ColumnFull})");

        // Cross-check against an independent implementation: enumerate every
        // 4-window that contains the placed cell instead of walking outward.
        var rng = new Random(47);
        bool agreed = true;
        int wins = 0, trials = 0;

        for (int trial = 0; trial < 20_000; trial++)
        {
            int rows = rng.Next(1, 7), cols = rng.Next(1, 7);
            var board = new char[rows][];
            for (int r = 0; r < rows; r++)
            {
                board[r] = new char[cols];
                for (int c = 0; c < cols; c++)
                    board[r][c] = rng.Next(3) switch { 0 => 'a', 1 => 'b', _ => Empty };
            }

            int x = rng.Next(rows), y = rng.Next(cols);
            char player = rng.Next(2) == 0 ? 'a' : 'b';

            bool occupiedCell = board[x][y] != Empty;
            bool got = CanPlayWin(board, x, y, player);

            // Reference runs on the board AFTER the (possibly skipped) move.
            bool want = !occupiedCell && LineThroughCell(board, x, y, player);

            if (got != want)
                agreed = false;
            if (!occupiedCell)
            {
                trials++;
                wins += got ? 1 : 0;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"20,000 random moves agree with the window-enumeration reference: {agreed}");
        Console.WriteLine($"  ({wins} of {trials} legal moves were wins -- enough of both to be meaningful)");

        if (!agreed)
            failures++;

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "All cases passed." : $"{failures} case(s) FAILED.");
    }

    /// <summary>
    /// Reference implementation, deliberately structured differently: for each
    /// axis, try every window of 4 consecutive cells that contains (x, y) and
    /// check whether all four hold <paramref name="player"/>.
    /// </summary>
    private static bool LineThroughCell(char[][] board, int x, int y, char player)
    {
        foreach (var (dr, dc) in Axes)
        {
            for (int offset = -(Connect - 1); offset <= 0; offset++)
            {
                bool all = true;

                for (int k = 0; k < Connect && all; k++)
                {
                    int r = x + (offset + k) * dr, c = y + (offset + k) * dc;
                    all = r >= 0 && r < board.Length
                       && c >= 0 && c < board[r].Length
                       && board[r][c] == player;
                }

                if (all)
                    return true;
            }
        }

        return false;
    }

    private static char[][] Grid(params string[] rows) =>
        rows.Select(r => r.ToCharArray()).ToArray();

    private static void PrintSideBySide(string[] before, char[][] after)
    {
        int width = Math.Max(before.Max(r => r.Length), "before".Length);

        Console.WriteLine($"  {"before".PadRight(width)}     after");
        for (int r = 0; r < before.Length; r++)
        {
            string arrow = r == before.Length / 2 ? " --> " : "     ";
            Console.WriteLine($"  {before[r].PadRight(width)}{arrow}{new string(after[r])}");
        }
    }
}

// ---- Notes for the follow-up questions ----
//
// "Now make it a real game."
//     The caller stops supplying a row -- see Drop(). State to add: whose turn
//     it is, a per-column height array so a drop is O(1) instead of a scan, and
//     a filled-cell counter so a draw (every cell full, no win) is detectable
//     without rescanning. The win check itself does not change at all.
//
// "Undo the last move."
//     Keep a stack of landing coordinates. Undo pops one and writes '.' back.
//     The win check is stateless -- it reads the board, never a cached verdict --
//     so nothing else needs unwinding. This is why WouldWin() can exist as a
//     three-line wrapper.
//
// "Connect K instead of 4, on a huge board."
//     Only the Connect constant and the Run() cap move; the check stays
//     O(K) per move. It is O(K) and not O(board) precisely because the search is
//     anchored at the placed cell.
//
// "Which cells are the winning line?"
//     Same walk, but return the endpoint instead of the count: step back
//     run(-dr,-dc) cells from (x, y) to get the head, then emit K cells along
//     (dr, dc). Worth doing for a UI, and it makes off-by-one bugs visible --
//     a length that says 4 while the emitted line is 3 cells is an immediate tell.
//
// "Find every winning move for the current player."
//     WouldWin over each legal cell: O(legal moves) probes, each O(1). With
//     gravity there are at most n legal moves (one per column), so a full
//     "can I win this turn / must I block" check is O(n * K) -- cheap enough to
//     be the base case of a minimax search rather than a special case beside it.
//
// "The board arrives as string[] instead of char[][]."
//     Reads are identical; only the mutation breaks, since strings are immutable.
//     Either convert once up front, or keep the placed piece in a separate
//     variable and special-case (x, y) inside Run(). The first is clearer and
//     the copy is O(m * n) once, not per probe.
