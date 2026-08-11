// LeetCode 51 / 52 - N-Queens, N-Queens II
// Difficulty: Hard
// Pattern: Backtracking (DFS over a decision tree) with O(1) conflict checks
//
// Problem: Place n queens on an n x n board so that no two share a row, column,
// or diagonal. #51 returns every distinct board, #52 returns only how many there
// are. Boards are rendered as n strings of '.' and 'Q'.
//
//   n = 4 -> [[".Q..","...Q","Q...","..Q."],
//             ["..Q.","Q...","...Q",".Q.."]]
//   n = 8 -> 92 solutions
//
// Approach: The rows are the decision levels. Every solution has exactly one
// queen per row -- n queens into n rows with none sharing a row leaves no other
// option -- so instead of choosing n squares out of n^2, choose one COLUMN for
// row 0, then row 1, and so on. That alone cuts the search space from C(n^2, n)
// to n^n, and the conflict checks prune it far below that.
//
// A square is legal iff its column, its diagonal, and its anti-diagonal are all
// still free, so the state is three boolean arrays rather than the board:
//   column        col                 identical on a column
//   diagonal      row - col + (n - 1) constant along "\"; shifted to stay >= 0
//   anti-diagonal row + col           constant along "/"
// Both diagonal indices run 0 .. 2n-2, hence the 2n-1 sizes. Checking a square
// is then O(1) instead of an O(n) scan back over the placed queens, and the
// mark/recurse/unmark around the recursive call is the backtracking itself.
// Rows above the current one are never revisited, so no "visited" set is needed.
//
// Time: O(n!) upper bound -- row r has at most n - r free columns -- times O(n^2)
//       to render each board it reaches. The real count is far smaller (n = 8
//       explores ~2k nodes, not 8!), but the bound is not polynomial.
// Space: O(n) for the recursion and the three marker arrays, plus the output.
//
// Follow-up (just the count, as fast as possible): see TotalNQueens.

namespace CodingPatterns.Graphs;

public class NQueens
{
    public IList<IList<string>> SolveNQueens(int n)
    {
        var boards = new List<IList<string>>();
        if (n <= 0)
            return boards;

        var queenCol = new int[n];              // queenCol[row] = chosen column
        var usedCol = new bool[n];
        var usedDiag = new bool[2 * n - 1];     // row - col + (n - 1)
        var usedAnti = new bool[2 * n - 1];     // row + col

        void Place(int row)
        {
            if (row == n)
            {
                boards.Add(Render(queenCol));
                return;
            }

            for (int col = 0; col < n; col++)
            {
                int diag = row - col + n - 1;
                int anti = row + col;
                if (usedCol[col] || usedDiag[diag] || usedAnti[anti])
                    continue;

                usedCol[col] = usedDiag[diag] = usedAnti[anti] = true;
                queenCol[row] = col;

                Place(row + 1);

                usedCol[col] = usedDiag[diag] = usedAnti[anti] = false;
            }
        }

        Place(0);
        return boards;
    }

    private static IList<string> Render(int[] queenCol)
    {
        int n = queenCol.Length;
        var rows = new List<string>(n);
        foreach (int col in queenCol)
            rows.Add(new string('.', col) + "Q" + new string('.', n - col - 1));
        return rows;
    }

    // ---- Follow-up: count only (LeetCode 52) ----
    //
    // When the boards are never inspected, two things get cheaper.
    //
    // 1. BITMASKS. The three boolean arrays become three ints whose bits are the
    //    columns blocked in the CURRENT row, so the free squares are one word:
    //        available = ~(cols | diag | anti) & full
    //    `bit & -bit` peels off the lowest free column. Descending a row shifts a
    //    diagonal block sideways by one -- that is what a diagonal *is* -- so
    //    diag shifts left and anti shifts right; `& full` drops the block that
    //    has slid off the edge. Same O(n!) shape, but no array writes and no
    //    per-column loop over blocked squares.
    //
    // 2. SYMMETRY. Mirroring a board left-to-right maps solutions to solutions
    //    and is an involution with no fixed points (a board equal to its own
    //    mirror would need row 0's queen in a column equal to its reflection,
    //    and for even n no column reflects to itself; for odd n only the middle
    //    column does, and those boards are counted separately below). So the
    //    solutions with row 0's queen in the left half are exactly half of all
    //    of them, and the search only has to explore that half.
    //
    // Time: still O(n!), roughly 2x faster in practice.  Space: O(n) recursion.
    public int TotalNQueens(int n)
    {
        if (n <= 0)
            return 0;

        int full = (1 << n) - 1;
        int total = 0;

        for (int col = 0; col < n / 2; col++)
        {
            int bit = 1 << col;
            total += 2 * Count(bit, (bit << 1) & full, bit >> 1, full);
        }

        if (n % 2 == 1)                          // middle column is its own mirror
        {
            int bit = 1 << (n / 2);
            total += Count(bit, (bit << 1) & full, bit >> 1, full);
        }

        return total;
    }

    private static int Count(int cols, int diag, int anti, int full)
    {
        if (cols == full)                        // every column filled == n rows placed
            return 1;

        int available = ~(cols | diag | anti) & full;
        int count = 0;

        while (available != 0)
        {
            int bit = available & -available;    // lowest free column
            available -= bit;
            count += Count(cols | bit, ((diag | bit) << 1) & full, (anti | bit) >> 1, full);
        }

        return count;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new NQueens();

        // Known counts for n = 1..9. n = 2 and n = 3 have no solution at all.
        int[] expected = { 1, 0, 0, 2, 10, 4, 40, 92, 352 };

        for (int n = 1; n <= 9; n++)
        {
            var boards = sol.SolveNQueens(n);
            int counted = sol.TotalNQueens(n);
            bool valid = boards.All(b => IsValid(b, n));

            Console.WriteLine(
                $"n={n}  boards={boards.Count,3}  count={counted,3}  " +
                $"expected={expected[n - 1],3}  all valid: {valid}");
        }

        // The n = 4 boards, spelled out.
        Console.WriteLine();
        foreach (var board in sol.SolveNQueens(4))
        {
            foreach (var row in board)
                Console.WriteLine("  " + row);
            Console.WriteLine();
        }

        // Counting alone reaches sizes enumeration should not be asked to render.
        Console.WriteLine($"n=12 -> {sol.TotalNQueens(12)} solutions");   // 14200
    }

    // A board is valid iff it has one queen per row and no two queens share a
    // column or a diagonal -- re-derived from the strings, not from the search.
    private static bool IsValid(IList<string> board, int n)
    {
        if (board.Count != n)
            return false;

        var cols = new List<int>();
        foreach (var row in board)
        {
            if (row.Length != n || row.Count(c => c == 'Q') != 1)
                return false;
            cols.Add(row.IndexOf('Q'));
        }

        for (int r1 = 0; r1 < n; r1++)
            for (int r2 = r1 + 1; r2 < n; r2++)
                if (cols[r1] == cols[r2] || Math.Abs(cols[r1] - cols[r2]) == r2 - r1)
                    return false;

        return true;
    }
}
