/*
Valid Tic-Tac-Toe State (LeetCode 794), extended to N x N with K in a row

Given a board of 'X', 'O' and empty cells, decide whether it can be reached by a
legal sequence of moves: X first, players alternate, and NOBODY MOVES AFTER
SOMEONE HAS WON.

THE FOUR INVARIANTS EVERYONE WRITES (and they are all correct)

    1.  countX == countO  or  countX == countO + 1      X moves first
    2.  X has a line  =>  countX == countO + 1          X's win was the last move
    3.  O has a line  =>  countX == countO              O's win was the last move
    4.  X and O cannot both have a line                 implied by 2 and 3

Invariant 4 is not an extra rule, it is arithmetic: (2) forces x = o+1 and (3)
forces x = o, so writing both checks makes the double win fall out for free.
Worth saying out loud rather than adding a third scan for it.

THE PART THAT MAKES THE EXTENSION A DIFFERENT PROBLEM

For 3x3 those four invariants are not just necessary, they are SUFFICIENT --
every board that passes them really is reachable, which is why the LeetCode
solution is fifteen lines and no search. That is a fact about the number 3, not a
fact about tic-tac-toe, and it does not survive the generalization:

    N = 5, K = 3        X X X . .      x = 6, o = 5      -> invariant 1 passes
                        O O . . .      X has a line      -> invariant 2 passes
                        . O O . .      O has none        -> invariants 3, 4 pass
                        . . . . O
                        X X X . .

    Counts are legal, exactly one player has a line, and the winner is the last
    mover. Every invariant above says VALID.

    It is not. X has TWO DISJOINT triples. Whichever X mark was played last, the
    other triple already existed one move earlier -- so the game was already over
    and that last move could not have happened.

THE ACTUAL CRITERION. A board is reachable if and only if

    (a) countX == countO  or  countX == countO + 1
    (b) at most one player has a K-line
    (c) if a player has a K-line, that player is the last mover  (2 and 3 above)
    (d) SOME SINGLE CELL LIES ON EVERY ONE OF THE WINNER'S K-LINES

(d) is the generalization of "the winning move was the last move": the last move
is one cell, and removing it has to end the win completely. Equivalently: the
intersection of all the winner's winning windows must be non-empty. For 3x3 (d)
is implied by (a)-(c) -- two disjoint 3-lines need 6 X's and 5 O's on 9 squares
-- which is exactly why nobody ever writes it for LeetCode 794.

WHY THAT IS THE WHOLE ANSWER (both directions, and both are two sentences)

    Necessary:  the game ended on the last move, so the board one move earlier
                had no line at all. Every winning line therefore ran through the
                cell that was just played.
    Sufficient: a board with NO line and legal counts is always reachable -- peel
                off any mark belonging to the player with the larger (or equal)
                count and recurse; removing marks can never create a line, so no
                intermediate board has one either, and the reversed peel order is
                a legal game. Given (d), delete the common cell: what is left has
                no line, so it is reachable, and playing that cell last is legal
                because nobody had won yet.

The peel argument is the load-bearing idea. It says the only thing that can make
a legal-looking board unreachable is a win that could not have been created by
one move -- everything else is free.

FINDING THE COMMON CELL IN O(N^2), WITH NO SET INTERSECTION

Scan each of the 4 axes once and cut the board into MAXIMAL RUNS of one symbol. A
run of length L >= K contains L-K+1 winning windows, and their intersection is a
contiguous slice of the run:

    windows start at 0 .. L-K, window s covers [s, s+K-1]
    intersection = run positions [L-K, K-1]

which is non-empty exactly when L <= 2K-1. Three corollaries worth knowing cold:

    L == K       the core is the whole run     (any of its cells could be last)
    L == 2K-1    the core is one cell          -- the exact middle, and nothing
                 else: deleting it splits the run into two of length K-1
    L >= 2K      the core is EMPTY             -- a run that long is by itself
                 proof of an illegal board, because deleting any one cell still
                 leaves K in a row on one side. Six in a row at K=3 is not a
                 board, it is a bug -- but FIVE in a row at K=3 is perfectly
                 legal, which is the off-by-one this problem is built to punish.

Intersect the cores across all runs of the winner, in all 4 directions, and stop
the moment the running intersection empties. The whole validator is O(N^2) time
and O(K) space.

WHAT TO SETTLE WITH THE INTERVIEWER BEFORE WRITING ANYTHING
  1. What is K? "N" for small boards, a constant (5, Gomoku) for large ones. They
     are different problems: K == N means at most 2N+2 possible lines and the
     naive scan is fine; K < N means O(N^2) windows and the run scan earns its
     keep. Default here is Min(N, 5), and it is a DEFAULT, not a rule.
  2. Does a full board with no win need extra handling? No -- a draw is just
     "nobody has a line", which is case (a) and nothing else.
  3. Must the board be square? Nothing below needs it except the shape check
     itself; the scans already handle rectangles. It is validated only because
     the problem said N x N.
  4. Is a malformed board (wrong shape, stray character) FALSE or an error?
     Here: an error. "Not reachable" and "not a board" are different answers, and
     silently returning false for a typo hides bugs in the caller.

Time:  O(N^2) -- one pass per axis for counting, win detection and the core.
Space: O(K) for the running intersection; O(1) if you only need the boolean.
*/

namespace CodingPatterns.ArraysStrings;

public class ValidTicTacToe
{
    private const char X = 'X';
    private const char O = 'O';
    private const char Empty = ' ';

    // Right, down, down-right, down-left. Four axes, not eight: a run and its
    // reverse are the same run, so opposite directions would only double count.
    private static readonly (int Dr, int Dc)[] Directions = { (0, 1), (1, 0), (1, 1), (1, -1) };

    /// <summary>The K nobody specified. Small boards play the whole side; big boards play
    /// Gomoku's 5. Say this out loud instead of hard-coding 3 and hoping.</summary>
    public static int DefaultK(int n) => Math.Min(n, 5);

    // =====================================================================
    // Part 0 -- LeetCode 794 exactly: 3x3, the four invariants, no generalization
    // =====================================================================

    /// <summary>The base problem, written the way it should be written in a screen: the
    /// lines of a 3x3 board are a fixed list of 8 triples, so "has a line" is a loop
    /// over a constant.</summary>
    public static bool ValidTicTacToe3x3(IReadOnlyList<string> board)
    {
        if (board is null || board.Count != 3 || board.Any(row => row.Length != 3))
            throw new ArgumentException("LeetCode 794 takes exactly a 3x3 board");
        if (board.Any(row => row.Any(ch => ch != X && ch != O && ch != Empty)))
            throw new ArgumentException("cells must be 'X', 'O' or ' '");

        int x = board.Sum(row => row.Count(ch => ch == X));
        int o = board.Sum(row => row.Count(ch => ch == O));

        // X moves first, so X is either level with O or exactly one ahead.
        if (o > x || x > o + 1) return false;

        bool Wins(char player)
        {
            for (int i = 0; i < 3; i++)
            {
                if (board[i][0] == player && board[i][1] == player && board[i][2] == player) return true;
                if (board[0][i] == player && board[1][i] == player && board[2][i] == player) return true;
            }
            if (board[0][0] == player && board[1][1] == player && board[2][2] == player) return true;
            if (board[0][2] == player && board[1][1] == player && board[2][0] == player) return true;
            return false;
        }

        // The winning move IS the last move, which pins the counts exactly. Checking
        // both also rules out the double win: x == o+1 and x == o cannot both hold.
        if (Wins(X) && x != o + 1) return false;
        if (Wins(O) && x != o) return false;
        return true;
    }

    // =====================================================================
    // Part 1 -- the N x N machinery: normalize, count, scan runs
    // =====================================================================

    /// <summary>Validate the board and return its rows. '.' and '_' are accepted as empty
    /// so wide boards stay readable in source; anything else is a malformed board.</summary>
    public static string[] Normalize(IReadOnlyList<string> grid)
    {
        if (grid is null || grid.Count == 0)
            throw new ArgumentException("board must be a non-empty sequence of rows");

        int n = grid.Count;
        var rows = new string[n];
        for (int r = 0; r < n; r++)
        {
            var row = new string(grid[r].Select(ch => ch == '.' || ch == '_' ? Empty : ch).ToArray());
            if (row.Length != n)
                throw new ArgumentException($"board must be square: got a row of {row.Length} on an {n}-row board");
            if (row.Any(ch => ch != X && ch != O && ch != Empty))
                throw new ArgumentException("cells must be 'X', 'O' or empty ('.', '_', ' ')");
            rows[r] = row;
        }
        return rows;
    }

    private static int ResolveK(int n, int? k)
    {
        int value = k ?? DefaultK(n);
        if (value < 1) throw new ArgumentException("K must be at least 1");
        return value;
    }

    /// <summary>(countX, countO). O(N^2).</summary>
    public static (int X, int O) Counts(IReadOnlyList<string> rows)
    {
        int x = 0, o = 0;
        foreach (var row in rows)
            foreach (var ch in row)
            {
                if (ch == X) x++;
                else if (ch == O) o++;
            }
        return (x, o);
    }

    /// <summary>Every MAXIMAL run of <paramref name="player"/> along (dr, dc), as its list of
    /// cells. A cell starts a run when the cell behind it along the same axis is off the
    /// board or not the player's -- that O(1) test is what keeps the scan O(N^2) rather
    /// than O(N^2 * K).</summary>
    public static IEnumerable<List<(int R, int C)>> Runs(IReadOnlyList<string> rows, char player, int dr, int dc)
    {
        int n = rows.Count;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (rows[r][c] != player) continue;

                int pr = r - dr, pc = c - dc;
                if (pr >= 0 && pr < n && pc >= 0 && pc < n && rows[pr][pc] == player)
                    continue;                                   // mid-run, not the head

                var run = new List<(int, int)>();
                for (int rr = r, cc = c; rr >= 0 && rr < n && cc >= 0 && cc < n && rows[rr][cc] == player;
                     rr += dr, cc += dc)
                    run.Add((rr, cc));
                yield return run;
            }
        }
    }

    /// <summary>Does the player have K in a row anywhere? O(N^2), early exit.</summary>
    public static bool HasWin(IReadOnlyList<string> rows, char player, int k)
    {
        foreach (var (dr, dc) in Directions)
            foreach (var run in Runs(rows, player, dr, dc))
                if (run.Count >= k) return true;
        return false;
    }

    /// <summary>Every winning K-window, as its set of cells -- the definition, written out.
    /// Only used to check the fast version; O(N^2 * K) and worth avoiding.</summary>
    public static List<HashSet<(int R, int C)>> WinningWindows(IReadOnlyList<string> rows, char player, int k)
    {
        int n = rows.Count;
        var found = new List<HashSet<(int, int)>>();

        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                foreach (var (dr, dc) in Directions)
                {
                    int er = r + (k - 1) * dr, ec = c + (k - 1) * dc;
                    if (er < 0 || er >= n || ec < 0 || ec >= n) continue;

                    var cells = new HashSet<(int, int)>();
                    for (int i = 0; i < k; i++) cells.Add((r + i * dr, c + i * dc));
                    if (cells.All(cell => rows[cell.Item1][cell.Item2] == player))
                        found.Add(cells);
                }
        return found;
    }

    /// <summary>The cells lying on EVERY one of the player's winning lines -- i.e. the only
    /// cells that could have been the winning, and therefore final, move.
    /// <list type="bullet">
    /// <item><c>null</c> -- the player has no K-line at all</item>
    /// <item><c>empty</c> -- the player has won, but no single move could have done it</item>
    /// <item><c>cells</c> -- any of these could have been played last</item>
    /// </list>
    /// One pass per axis, intersecting run cores. O(N^2) time, O(K) space.</summary>
    public static HashSet<(int R, int C)> LastMoveCells(IReadOnlyList<string> rows, char player, int k)
    {
        HashSet<(int, int)> core = null;

        foreach (var (dr, dc) in Directions)
            foreach (var run in Runs(rows, player, dr, dc))
            {
                int length = run.Count;
                if (length < k) continue;

                // Windows of this run start at 0 .. L-K, so they all contain exactly
                // run positions [L-K, K-1]. Empty as soon as L >= 2K-1.
                var cells = new HashSet<(int, int)>();
                for (int i = length - k; i <= k - 1; i++) cells.Add(run[i]);

                if (core is null) core = cells;
                else core.IntersectWith(cells);

                if (core.Count == 0) return core;              // can only shrink -- stop now
            }

        return core;
    }

    // =====================================================================
    // Part 2 -- the tempting generalization, kept because it is WRONG
    // =====================================================================

    /// <summary>The four LeetCode invariants with the win scanner generalized to N x N. This
    /// is what "just make the line detection N x N" produces, it passes every 3x3 test, and
    /// it says true for boards that cannot exist -- see the disjoint double win in the
    /// header. Here to be disagreed with, not to be called.</summary>
    public static bool IsValidCountingOnly(IReadOnlyList<string> grid, int? k = null)
    {
        var rows = Normalize(grid);
        int kk = ResolveK(rows.Length, k);
        var (x, o) = Counts(rows);

        if (o > x || x > o + 1) return false;
        if (HasWin(rows, X, kk) && x != o + 1) return false;
        if (HasWin(rows, O, kk) && x != o) return false;
        return true;
    }

    // =====================================================================
    // Part 3 -- the correct validator
    // =====================================================================

    /// <summary>Is this N x N board reachable by legal play with K in a row to win?</summary>
    public static bool IsValid(IReadOnlyList<string> grid, int? k = null)
    {
        var rows = Normalize(grid);
        int kk = ResolveK(rows.Length, k);
        var (x, o) = Counts(rows);

        // (a) X moves first and players alternate.
        if (o > x || x > o + 1) return false;

        var xCore = LastMoveCells(rows, X, kk);
        var oCore = LastMoveCells(rows, O, kk);
        bool xWon = xCore is not null, oWon = oCore is not null;

        // (b) Both players holding a line is unreachable. Stated explicitly, though
        // the two count checks below already make it impossible.
        if (xWon && oWon) return false;

        // (c) + (d) The winner is the last mover, AND one cell carries every line.
        if (xWon) return x == o + 1 && xCore.Count > 0;
        if (oWon) return x == o && oCore.Count > 0;

        // Nobody has won: counts are the only constraint. Peel and it unwinds.
        return true;
    }

    // =====================================================================
    // Part 4 -- reference implementations, for checking Part 3 rather than shipping
    // =====================================================================

    private static string[] With(IReadOnlyList<string> rows, int r, int c, char ch)
    {
        var next = rows.ToArray();
        var row = next[r].ToCharArray();
        row[c] = ch;
        next[r] = new string(row);
        return next;
    }

    private static string Key(IReadOnlyList<string> rows) => string.Concat(rows);

    /// <summary>The DEFINITION, searched: is there a legal move order ending here? Peels the
    /// last mover's marks one at a time, refusing any peel that leaves a board which had
    /// already been won -- because no move may follow a win.
    ///
    /// Shares no reasoning with <see cref="IsValid"/>: it never asks which cells lines run
    /// through, only whether the position one move back was still in play. Exponential,
    /// memoized on the board; small boards only.</summary>
    public static bool ReachableReference(IReadOnlyList<string> grid, int? k = null)
    {
        var rows = Normalize(grid);
        int n = rows.Length, kk = ResolveK(n, k);
        var memo = new Dictionary<string, bool>();

        bool Go(string[] board)
        {
            var (x, o) = Counts(board);
            if (x + o == 0) return true;                    // the empty board starts games
            if (o > x || x > o + 1) return false;

            string key = Key(board);
            if (memo.TryGetValue(key, out bool cached)) return cached;

            memo[key] = false;                              // counts shrink; no cycles
            char last = x == o + 1 ? X : O;

            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (board[r][c] != last) continue;
                    var before = With(board, r, c, Empty);
                    if (HasWin(before, X, kk) || HasWin(before, O, kk))
                        continue;                           // game was over; illegal move
                    if (Go(before))
                    {
                        memo[key] = true;
                        return true;
                    }
                }

            return false;
        }

        return Go(rows);
    }

    /// <summary>Every board reachable by legal play, by forward search from the empty board.
    /// Ground truth -- and only tractable because 3x3 has 5,478 of them.</summary>
    public static HashSet<string> AllReachable(int n, int? k = null)
    {
        int kk = ResolveK(n, k);
        var start = Enumerable.Repeat(new string(Empty, n), n).ToArray();
        var seen = new HashSet<string> { Key(start) };
        var frontier = new Stack<string[]>();
        frontier.Push(start);

        while (frontier.Count > 0)
        {
            var board = frontier.Pop();
            var (x, o) = Counts(board);
            if (x + o == n * n || HasWin(board, X, kk) || HasWin(board, O, kk))
                continue;                                   // terminal: nobody moves on

            char player = x == o ? X : O;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (board[r][c] != Empty) continue;
                    var next = With(board, r, c, player);
                    if (seen.Add(Key(next))) frontier.Push(next);
                }
        }
        return seen;
    }

    /// <summary>The flip side of the question: how many distinct legal move orders end at
    /// exactly this board? Same peel as <see cref="ReachableReference"/>, summing instead of
    /// short-circuiting, memoized over which marks are still placed.
    ///
    /// <c>CountSequences(b) &gt; 0</c> is the same predicate as <see cref="IsValid"/>, which
    /// makes it a second, independent implementation of the answer. States: 2^x * 2^o at
    /// worst, so this is a small-board tool. It is the honest answer to "now count the
    /// games", and the honest follow-up is that no polynomial formula exists: the win
    /// constraint couples the orders.</summary>
    public static long CountSequences(IReadOnlyList<string> grid, int? k = null)
    {
        var rows = Normalize(grid);
        int n = rows.Length, kk = ResolveK(n, k);

        var xs = new List<(int R, int C)>();
        var os = new List<(int R, int C)>();
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (rows[r][c] == X) xs.Add((r, c));
                else if (rows[r][c] == O) os.Add((r, c));
            }

        var memo = new Dictionary<(int, int), long>();

        long Go(string[] board, int xMask, int oMask)
        {
            int x = System.Numerics.BitOperations.PopCount((uint)xMask);
            int o = System.Numerics.BitOperations.PopCount((uint)oMask);
            if (x + o == 0) return 1;
            if (o > x || x > o + 1) return 0;

            var key = (xMask, oMask);
            if (memo.TryGetValue(key, out long cached)) return cached;

            bool lastIsX = x == o + 1;
            var placed = lastIsX ? xs : os;
            int mask = lastIsX ? xMask : oMask;
            long total = 0;

            for (int i = 0; i < placed.Count; i++)
            {
                if ((mask >> i & 1) == 0) continue;
                var before = With(board, placed[i].R, placed[i].C, Empty);
                if (HasWin(before, X, kk) || HasWin(before, O, kk)) continue;
                total += lastIsX
                    ? Go(before, xMask & ~(1 << i), oMask)
                    : Go(before, xMask, oMask & ~(1 << i));
            }

            memo[key] = total;
            return total;
        }

        return Go(rows, (1 << xs.Count) - 1, (1 << os.Count) - 1);
    }

    /// <summary>Number of complete legal games on an n x n board -- forward this time, so it
    /// checks CountSequences from the other end. 255,168 for standard 3x3.</summary>
    public static long CountGames(int n, int? k = null)
    {
        int kk = ResolveK(n, k);
        var memo = new Dictionary<string, long>();

        long Go(string[] board)
        {
            var (x, o) = Counts(board);
            if (x + o == n * n || HasWin(board, X, kk) || HasWin(board, O, kk))
                return 1;                                   // the game ends here: one game

            string key = Key(board);
            if (memo.TryGetValue(key, out long cached)) return cached;

            char player = x == o ? X : O;
            long total = 0;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (board[r][c] == Empty)
                        total += Go(With(board, r, c, player));

            memo[key] = total;
            return total;
        }

        return Go(Enumerable.Repeat(new string(Empty, n), n).ToArray());
    }

    // =====================================================================
    // Tests
    // =====================================================================

    private static string Show(IReadOnlyList<string> rows) =>
        string.Join(" / ", rows.Select(row => row.Replace(Empty, '.')));

    private static string ShowCells(IEnumerable<(int R, int C)> cells) =>
        cells is null ? "none (no line)"
                      : "{" + string.Join(", ", cells.OrderBy(p => p.R).ThenBy(p => p.C)
                                                     .Select(p => $"({p.R},{p.C})")) + "}";

    /// <summary>A board with legal COUNTS and otherwise arbitrary placement -- most of the
    /// interesting invalid boards live here, since count bugs are the easy ones.</summary>
    private static string[] RandomBoard(Random rng, int n, int marks)
    {
        var cells = Enumerable.Range(0, n * n).OrderBy(_ => rng.Next()).ToArray();
        int x = rng.NextDouble() < 0.5 ? (marks + 1) / 2 : marks / 2;

        var grid = Enumerable.Range(0, n).Select(_ => new string(Empty, n).ToCharArray()).ToArray();
        for (int i = 0; i < marks; i++)
            grid[cells[i] / n][cells[i] % n] = i < x ? X : O;
        return grid.Select(row => new string(row)).ToArray();
    }

    /// <summary>Play a legal game for up to <paramref name="stop"/> moves. Whatever comes out
    /// is valid by construction -- the other half of the property, and the half that catches
    /// a validator which is merely strict.</summary>
    private static string[] RandomGame(Random rng, int n, int k, int stop)
    {
        var board = Enumerable.Repeat(new string(Empty, n), n).ToArray();
        for (int move = 0; move < stop; move++)
        {
            var (x, o) = Counts(board);
            if (x + o == n * n || HasWin(board, X, k) || HasWin(board, O, k)) break;

            var empties = new List<(int R, int C)>();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (board[r][c] == Empty) empties.Add((r, c));

            var pick = empties[rng.Next(empties.Count)];
            board = With(board, pick.R, pick.C, x == o ? X : O);
        }
        return board;
    }

    public static void Run()
    {
        // --- the canonical LeetCode 794 set ---------------------------------
        var canonical = new (string[] Board, bool Expected, string Why)[]
        {
            (new[] { "O  ", "   ", "   " }, false, "O moved first"),
            (new[] { "XOX", " X ", "   " }, false, "3 X to 1 O -- X moved twice in a row"),
            (new[] { "XXX", "   ", "OOO" }, false, "both players have a line"),
            (new[] { "XOX", "O O", "XOX" }, true,  "5 X, 4 O, nobody won: a legal draw"),
            (new[] { "XXX", "OOX", "OOX" }, true,  "X won on (0,2), which both lines share"),
            (new[] { "XOX", "OXO", "XOX" }, true,  "X won on the centre -- both diagonals at once"),
            (new[] { "XXX", "OO ", "   " }, true,  "X's row completed as the 5th move"),
            (new[] { "XXX", "OOO", "   " }, false, "O completed a line, then X kept playing"),
            (new[] { "   ", "   ", "   " }, true,  "empty board"),
            (new[] { "X  ", "   ", "   " }, true,  "one move"),
            (new[] { "O  ", "   ", "  X" }, true,  "1 and 1: X opened at (2,2), O replied at (0,0)"),
            (new[] { "O  ", "   ", "  O" }, false, "two O and no X at all"),
        };

        Console.WriteLine("== LeetCode 794, the canonical set ==");
        foreach (var (board, expected, why) in canonical)
        {
            bool got = ValidTicTacToe3x3(board);
            Assert(got == expected, $"794 {Show(board)}");
            Assert(IsValid(board, 3) == expected, $"general {Show(board)}");
            Assert(ReachableReference(board, 3) == expected, $"search {Show(board)}");
            Console.WriteLine($"  {Show(board),-18} {got,-6} {why}");
        }

        // --- 3x3 exhaustive, against a forward search ------------------------
        var ground = AllReachable(3, 3);
        var alphabet = new[] { X, O, Empty };
        var boards = new List<string[]>();
        for (int code = 0; code < 19683; code++)                 // 3^9
        {
            var cells = new char[9];
            for (int i = 0, rest = code; i < 9; i++, rest /= 3) cells[i] = alphabet[rest % 3];
            boards.Add(new[]
            {
                new string(cells, 0, 3), new string(cells, 3, 3), new string(cells, 6, 3),
            });
        }

        foreach (var board in boards)
        {
            bool truth = ground.Contains(Key(board));
            Assert(ValidTicTacToe3x3(board) == truth, $"794 exhaustive {Show(board)}");
            Assert(IsValid(board, 3) == truth, $"general exhaustive {Show(board)}");
            Assert(ReachableReference(board, 3) == truth, $"search exhaustive {Show(board)}");
            Assert(IsValidCountingOnly(board, 3) == truth, $"counting-only 3x3 {Show(board)}");
        }

        Console.WriteLine();
        Console.WriteLine("== 3x3, exhaustively ==");
        Console.WriteLine($"  all {boards.Count:N0} boards over {{X, O, empty}} classified, and all four");
        Console.WriteLine("  implementations agree with a forward search from the empty board:");
        Console.WriteLine($"    reachable positions       {ground.Count,7:N0}   (the known figure is 5,478)");

        var terminal = boards
            .Where(b => ground.Contains(Key(b)))
            .Where(b => HasWin(b, X, 3) || HasWin(b, O, 3) || Counts(b).X + Counts(b).O == 9)
            .ToList();
        int xWins = terminal.Count(b => HasWin(b, X, 3));
        int oWins = terminal.Count(b => HasWin(b, O, 3));
        Console.WriteLine($"    terminal positions        {terminal.Count,7:N0}   "
            + $"({xWins} X wins, {oWins} O wins, {terminal.Count - xWins - oWins} draws)");

        long games = CountGames(3, 3);
        long byBoard = terminal.Sum(b => CountSequences(b, 3));
        Assert(games == 255_168 && byBoard == 255_168, $"game count {games} / {byBoard}");
        Console.WriteLine($"    complete games            {games,7:N0}   forward count == sum over");
        Console.WriteLine("                                        terminal boards of CountSequences");

        for (int i = 0; i < boards.Count; i += 37)
            Assert((CountSequences(boards[i], 3) > 0) == IsValid(boards[i], 3), $"count>0 {Show(boards[i])}");
        Console.WriteLine("    CountSequences(b) > 0 == IsValid(b), on every 37th board");

        // --- where the four invariants stop being enough ---------------------
        var disjoint = new[] { "XXX..", "OO...", ".OO..", "....O", "XXX.." };
        Assert(Counts(Normalize(disjoint)) == (6, 5), "disjoint counts");
        Assert(IsValidCountingOnly(disjoint, 3), "counting-only accepts the disjoint board");
        Assert(!IsValid(disjoint, 3), "IsValid rejects the disjoint board");
        Assert(!ReachableReference(disjoint, 3), "search rejects the disjoint board");

        Console.WriteLine();
        Console.WriteLine("== N=5, K=3: where the LeetCode invariants break ==");
        foreach (var row in disjoint) Console.WriteLine($"    {string.Join(' ', row.ToCharArray())}");
        Console.WriteLine("  counts 6/5, exactly one winner, winner is the last mover -- all four");
        Console.WriteLine("  invariants pass, and IsValidCountingOnly says true.");
        Console.WriteLine("  X has two DISJOINT triples. Whichever X went last, the other triple");
        Console.WriteLine("  had already ended the game. No cell lies on both lines:");
        Console.WriteLine($"    LastMoveCells(X) = {ShowCells(LastMoveCells(Normalize(disjoint), X, 3))}");

        var joined = new[] { "XXX..", "X.O..", "XO...", "..O.O", "....." };
        var core = LastMoveCells(Normalize(joined), X, 3);
        Assert(core.Count == 1 && core.Contains((0, 0)), "joined core");
        Assert(IsValid(joined, 3) && ReachableReference(joined, 3), "joined is reachable");
        Console.WriteLine("  Move one triple so the two lines cross and the same counts are fine:");
        Console.WriteLine($"    {Show(Normalize(joined))}   LastMoveCells(X) = {ShowCells(core)}");

        // --- runs that are too long ------------------------------------------
        Console.WriteLine();
        Console.WriteLine("== a run of 2K is proof on its own ==");
        for (int length = 3; length <= 7; length++)
        {
            var board = new List<string> { new string(X, length) + new string('.', 7 - length) };
            while (board.Count < 7) board.Add(new string('.', 7));

            var runCore = LastMoveCells(Normalize(board), X, 3);
            string note = runCore.Count == 0
                ? "unreachable: no single move made it"
                : $"could end on {ShowCells(runCore)}";
            Console.WriteLine($"  K=3, run of {length}: {note}");
            Assert((runCore.Count == 0) == (length >= 6), $"run core at length {length}");
        }

        // --- edge cases -------------------------------------------------------
        var edges = new (string[] Board, int K, bool Expected, string Why)[]
        {
            (new[] { "   ", "   ", "   " }, 3, true,  "empty board is reachable"),
            (new[] { "X" },                 1, true,  "N=1, K=1: one cell, X takes it and wins"),
            (new[] { "O" },                 1, false, "N=1: O cannot move first"),
            (new[] { " " },                 1, true,  "N=1, unplayed"),
            (new[] { "XO", ".." },          2, true,  "K=2: two lone marks, no line, counts 1/1"),
            (new[] { "XX", "O." },          2, true,  "K=2: X's row was the 3rd move"),
            (new[] { "XX", "OO" },          2, false, "K=2: both players have a line"),
            (new[] { "....", "....", "....", "...." }, 4, true,  "empty 4x4"),
            (new[] { "XXXX", "OOO.", "....", "...." }, 4, true,  "K=4: X's row completed last"),
            (new[] { "XXXX", "OOOO", "....", "...." }, 4, false, "O won, then X kept playing"),
            (new[] { "XXX.", "OOO.", "....", "...." }, 3, false, "K=3: both have a triple"),
            (new[] { "XXX.", "OO..", "....", "...." }, 3, true,  "K=3 on 4x4: an ordinary X win"),
            (new[] { "XXXX", "OO.O", "....", "...." }, 3, true,
                "K=3, run of 4: still legal -- either middle cell could be last"),
            (new[] { "XXXXX", "OO.OO", ".....", ".....", "....." }, 3, true,
                "K=3, run of 5 = 2K-1: legal, and ONLY the middle cell could be last"),
            (new[] { "XXXXXX", "OO.OO.", "...O..", "......", "......", "......" }, 3, false,
                "K=3, run of 6 = 2K: every cell you delete leaves a triple"),
        };

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");
        foreach (var (board, k, expected, why) in edges)
        {
            bool got = IsValid(board, k);
            Assert(got == expected, $"edge {Show(board)} K={k}");
            Assert(ReachableReference(board, k) == expected, $"edge search {Show(board)} K={k}");
            Console.WriteLine($"  K={k}  {Show(Normalize(board)),-30} {got,-6} {why}");
        }

        foreach (var (bad, why) in new (string[], string)[]
                 {
                     (new[] { "XX", "X" }, "ragged rows"),
                     (new[] { "XXX", "XXX" }, "not square"),
                     (new[] { "XY ", "   ", "   " }, "stray character"),
                     (Array.Empty<string>(), "no rows"),
                 })
        {
            try
            {
                IsValid(bad);
                throw new Exception($"expected an ArgumentException for {why}");
            }
            catch (ArgumentException) { }
        }
        Console.WriteLine("  malformed boards throw ArgumentException rather than answering false --");
        Console.WriteLine("  'not a board' is a different answer from 'not reachable'.");

        var tall = new[] { "XXXX", "XXXX", "OOOO", "OOOO" };
        Assert(IsValid(tall, 5) && !IsValid(tall, 4), "K larger than the board");
        Console.WriteLine("  K=5 on a 4x4: no line can exist, so a full 8/8 board is reachable;");
        Console.WriteLine("  the same board at K=4 is not, because those rows are wins.");

        // --- the fast core against the definition, and both against the search -
        var rng = new Random(20260811);
        int checkedBoards = 0, counterexamples = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            int n = new[] { 3, 4, 4, 5 }[rng.Next(4)];
            int k = new[] { 2, 3, 3, n }[rng.Next(4)];
            int marks = rng.Next(0, Math.Min(9, n * n) + 1);
            var board = RandomBoard(rng, n, marks);

            // LastMoveCells (run cores, O(N^2)) == intersecting every window
            foreach (var player in new[] { X, O })
            {
                var windows = WinningWindows(board, player, k);
                HashSet<(int, int)> expected = null;
                if (windows.Count > 0)
                {
                    expected = new HashSet<(int, int)>(windows[0]);
                    foreach (var w in windows.Skip(1)) expected.IntersectWith(w);
                }

                var actual = LastMoveCells(board, player, k);
                Assert(expected is null ? actual is null : actual is not null && actual.SetEquals(expected),
                    $"core {Show(board)} {player} K={k}");
                Assert(HasWin(board, player, k) == (windows.Count > 0), $"haswin {Show(board)}");
            }

            bool truth = ReachableReference(board, k);
            Assert(IsValid(board, k) == truth, $"random {Show(board)} K={k}");
            Assert((CountSequences(board, k) > 0) == truth, $"count>0 {Show(board)} K={k}");
            if (IsValidCountingOnly(board, k) != truth) counterexamples++;
            checkedBoards++;
        }

        // And the other direction: anything actually played is accepted.
        int played = 0;
        for (int trial = 0; trial < 3000; trial++)
        {
            int n = new[] { 3, 4, 5, 6 }[rng.Next(4)];
            int k = new[] { 3, 4, Math.Min(n, 5) }[rng.Next(3)];
            var board = RandomGame(rng, n, k, rng.Next(0, n * n + 1));
            Assert(IsValid(board, k), $"played {Show(board)} K={k}");
            played++;
        }

        Console.WriteLine();
        Console.WriteLine("== randomized ==");
        Console.WriteLine($"  {checkedBoards:N0} arbitrary boards (N in 3..5, K in 2..N), each checked three ways:");
        Console.WriteLine("    run-core intersection == intersection of every winning window");
        Console.WriteLine("    IsValid               == backward search over legal move orders");
        Console.WriteLine("    IsValid               == (CountSequences > 0)");
        Console.WriteLine($"  {counterexamples:N0} of them are boards the four LeetCode invariants get WRONG.");
        Console.WriteLine($"  {played:N0} boards produced by actually playing legal games: all accepted.");

        Console.WriteLine();
        Console.WriteLine("All tests passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("assertion failed: " + message);
    }
}

// INTERVIEW FOLLOW-UPS
//
// "Why is the 3x3 version allowed to skip the common-cell check?"
//     Because on 9 squares the check can never fire. Two disjoint 3-lines need 6
//     X marks, hence 5 O marks, hence 11 squares. Two 3-lines that do intersect
//     share a cell by definition, and three lines with no common cell need 7
//     marks (7 + 6 = 13). So (d) is implied by (a)-(c) at N = 3 and only at
//     N = 3 -- the LeetCode solution is correct for a reason that evaporates the
//     moment the board grows. This is the single best thing to say in the
//     extension.
//
// "Is 'no line + legal counts' really always reachable? It feels too strong."
//     It is the peel argument, and the reason it is airtight is that removing a
//     mark can never CREATE a line. So peel the last mover's marks in any order
//     at all: every intermediate board is a subset of a line-free board and is
//     therefore line-free, and reversing the peel is a legal game. No search, no
//     case analysis, and it is why the validator is O(N^2) rather than a hunt.
//
// "K = N (the board's own side) -- does anything simplify?"
//     Yes, substantially. There are only 2N+2 possible lines and no line has a
//     proper sub-window, so "the winner's lines share a cell" is just "the
//     winning lines pairwise intersect", and any two full-length lines on a
//     square board intersect unless they are parallel. So the only unreachable
//     win is two PARALLEL full lines -- two full rows or two full columns, never
//     the diagonals, which always cross at the centre for odd N. A nice sanity
//     check to state before writing the general code.
//
// "Gomoku is 15x15 or 19x19 with K = 5. Does the O(N^2) claim hold?"
//     Yes -- the run scan is four passes over the board regardless of K, and the
//     running intersection is at most K cells. What does NOT hold is the rest of
//     real Gomoku: tournament rules add forbidden moves for black (double-three,
//     double-four, overline) and swap2 openings, and every one of them is an
//     extra reachability constraint on the FINAL position. Worth naming, because
//     it is the difference between the puzzle and the game.
//
// "Now allow a player to pass, or allow more than two players."
//     Passing breaks invariant (a) entirely -- counts become |x - o| unbounded --
//     and the win logic is untouched. Three players cycling X, O, Z gives
//     countX >= countO >= countZ >= countX - 1, and the last mover is whichever
//     symbol's count is one ahead of the next in cycle order; (d) is unchanged,
//     because it only ever talked about one cell.
//
// "Count the number of legal games that end at this board." (CountSequences)
//     Exponential, and no closed form: without the win rule the answer is the
//     plain interleaving x! * o!, and the win rule prunes exactly those orders
//     where a line completes early. The DP over (which X marks placed, which O
//     marks placed) is the standard answer at 2^x * 2^o states, and the thing to
//     say is WHY it cannot be factored -- whether an order is legal depends on
//     the whole prefix, not on any per-cell quantity. It is also the best
//     available cross-check on the validator: count > 0 must equal reachable.
//
// "Make it streaming -- cells arrive one at a time."
//     Then you are answering a different and easier question, because you get to
//     watch the game happen: keep four run-length counters per cell (one per
//     axis) and a move counter, and reject the first move that follows a win.
//     The offline problem is harder precisely because the ORDER has been erased,
//     and (d) is the only trace of it left in the final position.
//
// "How would you test it?"
//     Run() is the answer, and its shape matters more than its size. 3x3 is
//     small enough to settle COMPLETELY -- all 19,683 boards against a forward
//     search from the empty board -- so there is no reason to sample it, and
//     getting 5,478 reachable positions and 255,168 games out the far end
//     confirms the model against numbers computed by other people a century ago.
//     Above 3x3 the exhaustive set explodes, so the ground truth becomes a
//     backward search that shares no logic with the validator, and the random
//     boards are drawn with legal counts on purpose -- a uniformly random board
//     is almost always rejected on counting alone and tests nothing.
