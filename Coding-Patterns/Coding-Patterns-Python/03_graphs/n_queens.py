# LeetCode 51 / 52 - N-Queens, N-Queens II
# Difficulty: Hard
# Pattern: Backtracking (DFS over a decision tree) with O(1) conflict checks
#
# Problem: Place n queens on an n x n board so that no two share a row, column,
# or diagonal. #51 returns every distinct board, #52 returns only how many there
# are. Boards are rendered as n strings of '.' and 'Q'.
#
#   n = 4 -> [[".Q..", "...Q", "Q...", "..Q."],
#             ["..Q.", "Q...", "...Q", ".Q.."]]
#   n = 8 -> 92 solutions
#
# Approach: The rows are the decision levels. Every solution has exactly one
# queen per row -- n queens into n rows with none sharing a row leaves no other
# option -- so instead of choosing n squares out of n^2, choose one COLUMN for
# row 0, then row 1, and so on. That alone cuts the search space from C(n^2, n)
# to n^n, and the conflict checks prune it far below that.
#
# A square is legal iff its column, its diagonal, and its anti-diagonal are all
# still free, so the state is three sets rather than the board:
#   column        col        identical on a column
#   diagonal      row - col  constant along "\"
#   anti-diagonal row + col  constant along "/"
# Checking a square is then O(1) instead of an O(n) scan back over the placed
# queens, and the add/recurse/discard around the recursive call is the
# backtracking itself. Rows above the current one are never revisited, so no
# "visited" set is needed.
#
# Time: O(n!) upper bound -- row r has at most n - r free columns -- times O(n^2)
#       to render each board it reaches. The real count is far smaller (n = 8
#       explores ~2k nodes, not 8!), but the bound is not polynomial.
# Space: O(n) for the recursion and the three sets, plus the output.
#
# Follow-up (just the count, as fast as possible): see total_n_queens.

from typing import List


def solve_n_queens(n: int) -> List[List[str]]:
    boards: List[List[str]] = []
    if n <= 0:
        return boards

    queen_col = [0] * n          # queen_col[row] = chosen column
    used_col = set()
    used_diag = set()            # row - col
    used_anti = set()            # row + col

    def place(row: int) -> None:
        if row == n:
            boards.append(render(queen_col))
            return

        for col in range(n):
            if col in used_col or row - col in used_diag or row + col in used_anti:
                continue

            used_col.add(col)
            used_diag.add(row - col)
            used_anti.add(row + col)
            queen_col[row] = col

            place(row + 1)

            used_col.discard(col)
            used_diag.discard(row - col)
            used_anti.discard(row + col)

    place(0)
    return boards


def render(queen_col: List[int]) -> List[str]:
    n = len(queen_col)
    return ["." * col + "Q" + "." * (n - col - 1) for col in queen_col]


# ---- Follow-up: count only (LeetCode 52) ----
#
# When the boards are never inspected, two things get cheaper.
#
# 1. BITMASKS. The three sets become three ints whose bits are the columns
#    blocked in the CURRENT row, so the free squares are one word:
#        available = ~(cols | diag | anti) & full
#    `bit & -bit` peels off the lowest free column. Descending a row shifts a
#    diagonal block sideways by one -- that is what a diagonal *is* -- so diag
#    shifts left and anti shifts right; `& full` drops the block that has slid
#    off the edge. Same O(n!) shape, but no set churn and no per-column loop
#    over blocked squares.
#
# 2. SYMMETRY. Mirroring a board left-to-right maps solutions to solutions and
#    is an involution with no fixed points (a board equal to its own mirror
#    would need row 0's queen in a column equal to its reflection, and for even
#    n no column reflects to itself; for odd n only the middle column does, and
#    those boards are counted separately below). So the solutions with row 0's
#    queen in the left half are exactly half of all of them, and the search only
#    has to explore that half.
#
# Time: still O(n!), roughly 2x faster in practice.  Space: O(n) recursion.
def total_n_queens(n: int) -> int:
    if n <= 0:
        return 0

    full = (1 << n) - 1

    def count(cols: int, diag: int, anti: int) -> int:
        if cols == full:                     # every column filled == n rows placed
            return 1

        available = ~(cols | diag | anti) & full
        total = 0
        while available:
            bit = available & -available     # lowest free column
            available -= bit
            total += count(cols | bit, ((diag | bit) << 1) & full, (anti | bit) >> 1)
        return total

    total = 0
    for col in range(n // 2):
        bit = 1 << col
        total += 2 * count(bit, (bit << 1) & full, bit >> 1)

    if n % 2 == 1:                           # middle column is its own mirror
        bit = 1 << (n // 2)
        total += count(bit, (bit << 1) & full, bit >> 1)

    return total


# A board is valid iff it has one queen per row and no two queens share a column
# or a diagonal -- re-derived from the strings, not from the search.
def is_valid(board: List[str], n: int) -> bool:
    if len(board) != n:
        return False

    cols = []
    for row in board:
        if len(row) != n or row.count("Q") != 1:
            return False
        cols.append(row.index("Q"))

    for r1 in range(n):
        for r2 in range(r1 + 1, n):
            if cols[r1] == cols[r2] or abs(cols[r1] - cols[r2]) == r2 - r1:
                return False

    return True


# ---- Tests ----
if __name__ == "__main__":
    # Known counts for n = 1..9. n = 2 and n = 3 have no solution at all.
    expected = [1, 0, 0, 2, 10, 4, 40, 92, 352]

    for n, want in enumerate(expected, start=1):
        boards = solve_n_queens(n)
        assert len(boards) == want, (n, len(boards), want)
        assert total_n_queens(n) == want, (n, total_n_queens(n), want)
        assert all(is_valid(b, n) for b in boards), n
        assert len({tuple(b) for b in boards}) == want, n   # no duplicate boards

    assert solve_n_queens(0) == [] and total_n_queens(0) == 0

    # Columns are tried left to right, so row 0's queen advances rightwards.
    assert solve_n_queens(4) == [
        [".Q..", "...Q", "Q...", "..Q."],
        ["..Q.", "Q...", "...Q", ".Q.."],
    ]

    # The n = 4 boards, spelled out.
    for board in solve_n_queens(4):
        print("\n".join("  " + row for row in board))
        print()

    # Counting alone reaches sizes enumeration should not be asked to render.
    assert total_n_queens(12) == 14200
    print("n=12 -> 14200 solutions")

    print("All tests passed.")
