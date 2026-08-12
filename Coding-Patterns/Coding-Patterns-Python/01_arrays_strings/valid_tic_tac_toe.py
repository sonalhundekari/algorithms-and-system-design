# Valid Tic-Tac-Toe State (LeetCode 794), extended to N x N with K in a row
# Difficulty: Medium base, Hard extension
# Pattern: Reachability by peeling the LAST MOVE -- validate the final state by
#          asking "which single mark could have been played last?"
#
# Given a board of 'X', 'O' and empty cells, decide whether it can be reached by
# a legal sequence of moves: X first, players alternate, and NOBODY MOVES AFTER
# SOMEONE HAS WON.
#
# THE FOUR INVARIANTS EVERYONE WRITES (and they are all correct)
#
#     1.  count_X == count_O  or  count_X == count_O + 1      X moves first
#     2.  X has a line  =>  count_X == count_O + 1            X's win was the last move
#     3.  O has a line  =>  count_X == count_O                O's win was the last move
#     4.  X and O cannot both have a line                     implied by 2 and 3
#
# Invariant 4 is not an extra rule, it is arithmetic: (2) forces x = o+1 and (3)
# forces x = o, so writing both checks makes the double win fall out for free.
# Worth saying out loud rather than adding a third scan for it.
#
# THE PART THAT MAKES THE EXTENSION A DIFFERENT PROBLEM
#
# For 3x3 those four invariants are not just necessary, they are SUFFICIENT --
# every board that passes them really is reachable, which is why the LeetCode
# solution is fifteen lines and no search. That is a fact about the number 3, not
# a fact about tic-tac-toe, and it does not survive the generalization:
#
#     N = 5, K = 3        X X X . .      x = 6, o = 5      -> invariant 1 passes
#                         O O . . .      X has a line      -> invariant 2 passes
#                         . O O . .      O has none        -> invariants 3, 4 pass
#                         . . . . O
#                         X X X . .
#
#     Counts are legal, exactly one player has a line, and the winner is the
#     last mover. Every invariant above says VALID.
#
#     It is not. X has TWO DISJOINT triples. Whichever X mark was played last,
#     the other triple already existed one move earlier -- so the game was
#     already over and that last move could not have happened.
#
# THE ACTUAL CRITERION. A board is reachable if and only if
#
#     (a) count_X == count_O  or  count_X == count_O + 1
#     (b) at most one player has a K-line
#     (c) if a player has a K-line, that player is the last mover  (2 and 3 above)
#     (d) SOME SINGLE CELL LIES ON EVERY ONE OF THE WINNER'S K-LINES
#
# (d) is the generalization of "the winning move was the last move": the last
# move is one cell, and removing it has to end the win completely. Equivalently:
# the intersection of all the winner's winning windows must be non-empty. For
# 3x3 (d) is implied by (a)-(c) -- two disjoint 3-lines need 6 X's and 5 O's on
# 9 squares -- which is exactly why nobody ever writes it for LeetCode 794.
#
# WHY THAT IS THE WHOLE ANSWER (both directions, and both are two sentences)
#
#     Necessary:  the game ended on the last move, so the board one move earlier
#                 had no line at all. Every winning line therefore ran through
#                 the cell that was just played.
#     Sufficient: a board with NO line and legal counts is always reachable --
#                 peel off any mark belonging to the player with the larger (or
#                 equal) count and recurse; removing marks can never create a
#                 line, so no intermediate board has one either, and the reversed
#                 peel order is a legal game. Given (d), delete the common cell:
#                 what is left has no line, so it is reachable, and playing that
#                 cell last is legal because nobody had won yet.
#
# The peel argument is the load-bearing idea. It says the only thing that can
# make a legal-looking board unreachable is a win that could not have been
# created by one move -- everything else is free.
#
# FINDING THE COMMON CELL IN O(N^2), WITH NO SET INTERSECTION
#
# Scan each of the 4 axes once and cut the board into MAXIMAL RUNS of one symbol.
# A run of length L >= K contains L-K+1 winning windows, and their intersection
# is a contiguous slice of the run:
#
#     windows start at 0 .. L-K, window s covers [s, s+K-1]
#     intersection = [L-K, K-1]        i.e.  run[L-K : K]  as a Python slice
#
# which is non-empty exactly when L <= 2K-1. Three corollaries worth knowing cold:
#
#     L == K       the core is the whole run     (any of its cells could be last)
#     L == 2K-1    the core is one cell          -- the exact middle, and nothing
#                  else: deleting it splits the run into two of length K-1
#     L >= 2K      the core is EMPTY             -- a run that long is by itself
#                  proof of an illegal board, because deleting any one cell still
#                  leaves K in a row on one side. Six in a row at K=3 is not a
#                  board, it is a bug -- but FIVE in a row at K=3 is perfectly
#                  legal, which is the off-by-one this problem is built to punish.
#
# Intersect the cores across all runs of the winner, in all 4 directions, and
# stop the moment the running intersection empties. Whole validator is O(N^2)
# time and O(K) space.
#
# WHAT TO SETTLE WITH THE INTERVIEWER BEFORE WRITING ANYTHING
#   1. What is K? "N" for small boards, a constant (5, Gomoku) for large ones.
#      They are different problems: K == N means at most 2N+2 possible lines and
#      the naive scan is fine; K < N means O(N^2) windows and the run scan earns
#      its keep. Default here is min(N, 5), and it is a DEFAULT, not a rule.
#   2. Does a full board with no win need any extra handling? No -- a draw is
#      just "nobody has a line", which is case (a) and nothing else.
#   3. Must the board be square? Nothing below needs it except the diagonal
#      scan, which already handles rectangles; squareness is validated only
#      because the problem said N x N.
#   4. Is a malformed board (wrong shape, stray character) FALSE or an error?
#      Here: an error. "Not reachable" and "not a board" are different answers,
#      and silently returning False for a typo hides bugs in the caller.
#
# Time:  O(N^2) -- one pass per axis for counting, win detection and the core.
# Space: O(K) for the running intersection; O(1) if you only need the boolean.

import random
from itertools import product

X, O, EMPTY = "X", "O", " "

# Right, down, down-right, down-left. Four axes, not eight: a run and its
# reverse are the same run, so the opposite directions would only double count.
DIRECTIONS = ((0, 1), (1, 0), (1, 1), (1, -1))


def default_k(n):
    """The K nobody specified. Small boards play the whole side; big boards play
    Gomoku's 5. Say this out loud instead of hard-coding 3 and hoping."""
    return min(n, 5)


# ---------------------------------------------------------------------------
# Part 0 -- LeetCode 794 exactly: 3x3, the four invariants, no generalization
# ---------------------------------------------------------------------------

def valid_tic_tac_toe(board):
    """The base problem, written the way it should be written in a screen: the
    lines of a 3x3 board are a fixed list of 8 triples, so 'has a line' is a
    loop over a constant."""
    if len(board) != 3 or any(len(row) != 3 for row in board):
        raise ValueError("LeetCode 794 takes exactly a 3x3 board")
    if any(cell not in (X, O, EMPTY) for row in board for cell in row):
        raise ValueError("cells must be 'X', 'O' or ' '")

    x = sum(row.count(X) for row in board)
    o = sum(row.count(O) for row in board)

    # X moves first, so X is either level with O or exactly one ahead.
    if o > x or x > o + 1:
        return False

    def wins(player):
        lines = ([[(r, c) for c in range(3)] for r in range(3)]          # rows
                 + [[(r, c) for r in range(3)] for c in range(3)]        # columns
                 + [[(i, i) for i in range(3)]]                          # main diagonal
                 + [[(i, 2 - i) for i in range(3)]])                     # anti-diagonal
        return any(all(board[r][c] == player for r, c in line) for line in lines)

    # The winning move IS the last move, which pins the counts exactly. Checking
    # both also rules out the double win: x == o+1 and x == o cannot both hold.
    if wins(X) and x != o + 1:
        return False
    if wins(O) and x != o:
        return False
    return True


# ---------------------------------------------------------------------------
# Part 1 -- the N x N machinery: normalize, count, scan runs
# ---------------------------------------------------------------------------

def normalize(grid, k=None):
    """Validate the board and return (rows as a tuple of strings, n, k).
    '.' and '_' are accepted as empty so that wide boards stay readable in
    source; everything else is a malformed board and raises."""
    if not isinstance(grid, (list, tuple)) or not grid:
        raise ValueError("board must be a non-empty sequence of rows")

    n = len(grid)
    rows = []
    for row in grid:
        row = "".join(EMPTY if ch in "._" else ch for ch in row)
        if len(row) != n:
            raise ValueError(f"board must be square: got a row of {len(row)} on an {n}-row board")
        if any(ch not in (X, O, EMPTY) for ch in row):
            raise ValueError("cells must be 'X', 'O' or empty ('.', '_', ' ')")
        rows.append(row)

    k = default_k(n) if k is None else k
    if k < 1:
        raise ValueError("K must be at least 1")
    return tuple(rows), n, k


def counts(rows):
    """(count_X, count_O). O(N^2)."""
    return (sum(row.count(X) for row in rows),
            sum(row.count(O) for row in rows))


def runs(rows, player, dr, dc):
    """Yield every MAXIMAL run of `player` along direction (dr, dc) as its list
    of cells. A cell starts a run when the cell behind it along the same axis is
    off the board or not the player's -- that O(1) test is what keeps the whole
    scan O(N^2) instead of O(N^2 * K)."""
    n = len(rows)
    for r in range(n):
        for c in range(n):
            if rows[r][c] != player:
                continue
            pr, pc = r - dr, c - dc
            if 0 <= pr < n and 0 <= pc < n and rows[pr][pc] == player:
                continue                                  # mid-run, not the head
            run, rr, cc = [], r, c
            while 0 <= rr < n and 0 <= cc < n and rows[rr][cc] == player:
                run.append((rr, cc))
                rr, cc = rr + dr, cc + dc
            yield run


def has_win(rows, player, k):
    """Does `player` have K in a row anywhere? O(N^2), early exit."""
    return any(len(run) >= k
               for dr, dc in DIRECTIONS
               for run in runs(rows, player, dr, dc))


def winning_windows(rows, player, k):
    """Every winning K-window as a frozenset of cells -- the definition, written
    out. Only used to check the fast version; O(N^2 * K) and worth avoiding."""
    n = len(rows)
    found = []
    for r, c in product(range(n), repeat=2):
        for dr, dc in DIRECTIONS:
            er, ec = r + (k - 1) * dr, c + (k - 1) * dc
            if not (0 <= er < n and 0 <= ec < n):
                continue
            cells = [(r + i * dr, c + i * dc) for i in range(k)]
            if all(rows[y][x] == player for y, x in cells):
                found.append(frozenset(cells))
    return found


def last_move_cells(rows, player, k):
    """The cells that lie on EVERY one of `player`'s winning lines -- i.e. the
    only cells that could have been the winning (and therefore final) move.

        None        the player has no K-line at all
        set()       the player has won, but no single move could have done it
        {cells}     any of these could have been played last

    One pass per axis, intersecting run cores. O(N^2) time, O(K) space."""
    core = None
    for dr, dc in DIRECTIONS:
        for run in runs(rows, player, dr, dc):
            length = len(run)
            if length < k:
                continue
            # Windows of this run start at 0 .. L-K, so they all contain
            # exactly run[L-K : K]. Empty as soon as L >= 2K-1.
            cells = set(run[length - k:k])
            core = cells if core is None else (core & cells)
            if not core:
                return set()                  # can only shrink -- stop now
    return core


# ---------------------------------------------------------------------------
# Part 2 -- the tempting generalization, kept because it is WRONG
# ---------------------------------------------------------------------------

def valid_board_counting_only(grid, k=None):
    """The four LeetCode invariants with the win scanner generalized to N x N.
    This is what "just make the line detection N x N" produces, it passes every
    3x3 test, and it says True for boards that cannot exist -- see the disjoint
    double win in the header. Here to be disagreed with, not to be called."""
    rows, _, k = normalize(grid, k)
    x, o = counts(rows)
    if o > x or x > o + 1:
        return False
    if has_win(rows, X, k) and x != o + 1:
        return False
    if has_win(rows, O, k) and x != o:
        return False
    return True


# ---------------------------------------------------------------------------
# Part 3 -- the correct validator
# ---------------------------------------------------------------------------

def valid_board(grid, k=None):
    """Is this N x N board reachable by legal play with K in a row to win?"""
    rows, _, k = normalize(grid, k)
    x, o = counts(rows)

    # (a) X moves first and players alternate.
    if o > x or x > o + 1:
        return False

    x_core = last_move_cells(rows, X, k)
    o_core = last_move_cells(rows, O, k)
    x_won, o_won = x_core is not None, o_core is not None

    # (b) Both players holding a line is unreachable. Stated explicitly, though
    # the two count checks below already make it impossible.
    if x_won and o_won:
        return False

    # (c) + (d) The winner is the last mover, AND one cell carries every line.
    if x_won:
        return x == o + 1 and bool(x_core)
    if o_won:
        return x == o and bool(o_core)

    # Nobody has won: counts are the only constraint. Peel and it unwinds.
    return True


# ---------------------------------------------------------------------------
# Part 4 -- reference implementations, for checking Part 3 rather than shipping
# ---------------------------------------------------------------------------

def _with(rows, r, c, ch):
    return rows[:r] + (rows[r][:c] + ch + rows[r][c + 1:],) + rows[r + 1:]


def reachable_reference(grid, k=None):
    """The DEFINITION, searched: is there a legal move order ending here? Peels
    the last mover's marks one at a time, refusing any peel that leaves a board
    which had already been won -- because no move may follow a win.

    Shares no reasoning with valid_board: it never asks which cells lines run
    through, only whether the position one move back was still in play.
    Exponential, memoized on the board; small boards only."""
    rows, n, k = normalize(grid, k)
    memo = {}

    def go(board):
        x, o = counts(board)
        if x + o == 0:
            return True                        # the empty board is where games start
        if o > x or x > o + 1:
            return False
        if board in memo:
            return memo[board]

        memo[board] = False                    # counts strictly shrink; no cycles
        last = X if x == o + 1 else O
        for r, c in product(range(n), repeat=2):
            if board[r][c] != last:
                continue
            before = _with(board, r, c, EMPTY)
            if has_win(before, X, k) or has_win(before, O, k):
                continue                       # game was already over; illegal move
            if go(before):
                memo[board] = True
                break
        return memo[board]

    return go(rows)


def all_reachable(n, k=None):
    """Every board reachable by legal play, by forward search from the empty
    board. Ground truth -- and only tractable because 3x3 has 5,478 of them."""
    k = default_k(n) if k is None else k
    start = tuple(EMPTY * n for _ in range(n))
    seen, frontier = {start}, [start]

    while frontier:
        board = frontier.pop()
        x, o = counts(board)
        if x + o == n * n or has_win(board, X, k) or has_win(board, O, k):
            continue                           # terminal: nobody moves from here
        player = X if x == o else O
        for r, c in product(range(n), repeat=2):
            if board[r][c] != EMPTY:
                continue
            nxt = _with(board, r, c, player)
            if nxt not in seen:
                seen.add(nxt)
                frontier.append(nxt)
    return seen


def count_sequences(grid, k=None):
    """The flip side of the question: how many distinct legal move orders end at
    exactly this board? Same peel as reachable_reference, summing instead of
    short-circuiting, memoized over which marks are still placed.

    count_sequences(b) > 0 is the same predicate as valid_board(b) -- which
    makes it a second, independent implementation of the answer.

    States: 2^x * 2^o at worst, so this is a small-board tool. It is the honest
    answer to 'now count the games', and the honest follow-up is that no
    polynomial formula exists: the win constraint couples the orders."""
    rows, n, k = normalize(grid, k)
    xs = [(r, c) for r, c in product(range(n), repeat=2) if rows[r][c] == X]
    os = [(r, c) for r, c in product(range(n), repeat=2) if rows[r][c] == O]
    memo = {}

    def go(board, xm, om):
        x, o = bin(xm).count("1"), bin(om).count("1")
        if x + o == 0:
            return 1
        if o > x or x > o + 1:
            return 0
        key = (xm, om)
        if key in memo:
            return memo[key]

        total = 0
        cells = ((xs, xm, X, 0) if x == o + 1 else (os, om, O, 1))
        placed, mask, sym, which = cells
        for i, (r, c) in enumerate(placed):
            if not mask >> i & 1:
                continue
            before = _with(board, r, c, EMPTY)
            if has_win(before, X, k) or has_win(before, O, k):
                continue
            nxm = xm & ~(1 << i) if which == 0 else xm
            nom = om & ~(1 << i) if which == 1 else om
            total += go(before, nxm, nom)

        memo[key] = total
        return total

    return go(rows, (1 << len(xs)) - 1, (1 << len(os)) - 1)


def count_games(n, k=None):
    """Number of complete legal games on an n x n board -- forward this time, so
    it checks count_sequences from the other end. 255,168 for standard 3x3."""
    k = default_k(n) if k is None else k
    start = tuple(EMPTY * n for _ in range(n))
    memo = {}

    def go(board):
        x, o = counts(board)
        if x + o == n * n or has_win(board, X, k) or has_win(board, O, k):
            return 1                           # the game ends here: one game
        if board in memo:
            return memo[board]
        player = X if x == o else O
        total = sum(go(_with(board, r, c, player))
                    for r, c in product(range(n), repeat=2)
                    if board[r][c] == EMPTY)
        memo[board] = total
        return total

    return go(start)


# ---- Tests ----
def _show(grid):
    return " / ".join(row.replace(EMPTY, ".") for row in grid)


def _random_board(rng, n, marks):
    """A board with legal COUNTS and otherwise arbitrary placement -- most of the
    interesting invalid boards live here, since count bugs are the easy ones."""
    cells = [(r, c) for r, c in product(range(n), repeat=2)]
    rng.shuffle(cells)
    x = (marks + 1) // 2 if rng.random() < 0.5 else marks // 2
    o = marks - x
    rows = [[EMPTY] * n for _ in range(n)]
    for (r, c), sym in zip(cells, [X] * x + [O] * o):
        rows[r][c] = sym
    return tuple("".join(row) for row in rows)


def _random_game(rng, n, k, stop):
    """Play a legal game for up to `stop` moves. Whatever comes out is valid by
    construction -- the other half of the property, and the half that catches a
    validator which is merely strict."""
    board = tuple(EMPTY * n for _ in range(n))
    for move in range(stop):
        x, o = counts(board)
        if has_win(board, X, k) or has_win(board, O, k) or x + o == n * n:
            break
        empties = [(r, c) for r, c in product(range(n), repeat=2) if board[r][c] == EMPTY]
        r, c = rng.choice(empties)
        board = _with(board, r, c, X if x == o else O)
    return board


if __name__ == "__main__":
    # --- the canonical LeetCode 794 set ---------------------------------------
    canonical = [
        (["O  ", "   ", "   "], False, "O moved first"),
        (["XOX", " X ", "   "], False, "3 X to 1 O -- X moved twice in a row"),
        (["XXX", "   ", "OOO"], False, "both players have a line"),
        (["XOX", "O O", "XOX"], True, "5 X, 4 O, nobody won: a legal draw"),
        (["XXX", "OOX", "OOX"], True, "X won on (0,2), which both lines share"),
        (["XOX", "OXO", "XOX"], True, "X won on the centre -- both diagonals at once"),
        (["XXX", "OO ", "   "], True, "X's row completed as the 5th move"),
        (["XXX", "OOO", "   "], False, "O completed a line, then X kept playing"),
        (["   ", "   ", "   "], True, "empty board"),
        (["X  ", "   ", "   "], True, "one move"),
        (["O  ", "   ", "  X"], True, "1 and 1: X opened at (2,2), O replied at (0,0)"),
        (["O  ", "   ", "  O"], False, "two O and no X at all"),
    ]

    print("== LeetCode 794, the canonical set ==")
    for board, expected, why in canonical:
        got = valid_tic_tac_toe(board)
        assert got == expected, (board, got, expected)
        assert valid_board(board, 3) == expected, board          # general agrees
        assert reachable_reference(board, 3) == expected, board  # search agrees
        print(f"  {_show(board):<18} {str(got):<6} {why}")

    # --- 3x3 exhaustive, against a forward search -----------------------------
    ground = all_reachable(3, 3)
    boards = [tuple("".join(cells) for cells in zip(*[iter(assign)] * 3))
              for assign in product((X, O, EMPTY), repeat=9)]
    assert len(boards) == 3 ** 9

    for board in boards:
        truth = board in ground
        assert valid_tic_tac_toe(list(board)) == truth, board
        assert valid_board(board, 3) == truth, board
        assert reachable_reference(board, 3) == truth, board
        assert valid_board_counting_only(board, 3) == truth, board   # 3x3 only!

    print()
    print("== 3x3, exhaustively ==")
    print(f"  all {len(boards):,} boards over {{X, O, empty}} classified, and all four")
    print("  implementations agree with a forward search from the empty board:")
    print(f"    reachable positions       {len(ground):,}   (the known figure is 5,478)")

    terminal = [b for b in ground
                if has_win(b, X, 3) or has_win(b, O, 3) or sum(counts(b)) == 9]
    x_wins = sum(1 for b in terminal if has_win(b, X, 3))
    o_wins = sum(1 for b in terminal if has_win(b, O, 3))
    print(f"    terminal positions        {len(terminal):,}   ({x_wins} X wins, "
          f"{o_wins} O wins, {len(terminal) - x_wins - o_wins} draws)")

    games = count_games(3, 3)
    by_board = sum(count_sequences(b, 3) for b in terminal)
    assert games == by_board == 255_168, (games, by_board)
    print(f"    complete games            {games:,}   forward count == sum over "
          "terminal boards of count_sequences")

    # count_sequences is a second answer to the original question.
    for board in boards[::37]:
        assert (count_sequences(board, 3) > 0) == valid_board(board, 3), board
    print("    count_sequences(b) > 0 == valid_board(b), on every 37th board")

    # --- where the four invariants stop being enough --------------------------
    disjoint = ["XXX..",
                "OO...",
                ".OO..",
                "....O",
                "XXX.."]
    assert counts(normalize(disjoint)[0]) == (6, 5)
    assert valid_board_counting_only(disjoint, 3) is True     # every invariant passes
    assert valid_board(disjoint, 3) is False                  # and it is still unreachable
    assert reachable_reference(disjoint, 3) is False

    print()
    print("== N=5, K=3: where the LeetCode invariants break ==")
    for row in disjoint:
        print(f"    {' '.join(row)}")
    print("  counts 6/5, exactly one winner, winner is the last mover -- all four")
    print("  invariants pass, and valid_board_counting_only says True.")
    print("  X has two DISJOINT triples. Whichever X went last, the other triple")
    print("  had already ended the game. No cell lies on both lines:")
    print(f"    last_move_cells(X) = {last_move_cells(normalize(disjoint)[0], X, 3)}")

    joined = ["XXX..",
              "X.O..",
              "XO...",
              "..O.O",
              "....."]
    core = last_move_cells(normalize(joined)[0], X, 3)
    assert core == {(0, 0)} and valid_board(joined, 3) and reachable_reference(joined, 3)
    print("  Move one triple so the two lines cross and the same counts are fine:")
    print(f"    {_show(normalize(joined)[0])}   last_move_cells(X) = {core}")

    # --- runs that are too long -----------------------------------------------
    print()
    print("== a run of 2K is proof on its own ==")
    for length in (3, 4, 5, 6, 7):
        board = [X * length + "." * (7 - length)] + ["." * 7] * 6
        core = last_move_cells(normalize(board, 3)[0], X, 3)
        note = "unreachable: no single move made it" if core == set() else f"could end on {sorted(core)}"
        print(f"  K=3, run of {length}: {note}")
        assert (core == set()) == (length >= 6), (length, core)

    # --- edge cases -----------------------------------------------------------
    edges = [
        (["   ", "   ", "   "], 3, True, "empty board is reachable"),
        (["X"], 1, True, "N=1, K=1: one cell, X takes it and wins"),
        (["O"], 1, False, "N=1: O cannot move first"),
        ([" "], 1, True, "N=1, unplayed"),
        (["XO", ".."], 2, True, "K=2: two lone marks, no line, counts 1/1"),
        (["XX", "O."], 2, True, "K=2: X's row was the 3rd move"),
        (["XX", "OO"], 2, False, "K=2: both players have a line"),
        (["....", "....", "....", "...."], 4, True, "empty 4x4"),
        (["XXXX", "OOO.", "....", "...."], 4, True, "K=4: X's row completed last"),
        (["XXXX", "OOOO", "....", "...."], 4, False, "O won, then X kept playing"),
        (["XXX.", "OOO.", "....", "...."], 3, False, "K=3: both have a triple"),
        (["XXX.", "OO..", "....", "...."], 3, True, "K=3 on 4x4: an ordinary X win"),
        (["XXXX", "OO.O", "....", "...."], 3, True,
         "K=3, run of 4: still legal -- either middle cell could be last"),
        (["XXXXX", "OO.OO", ".....", ".....", "....."], 3, True,
         "K=3, run of 5 = 2K-1: legal, and ONLY the middle cell could be last"),
        (["XXXXXX", "OO.OO.", "...O..", "......", "......", "......"], 3, False,
         "K=3, run of 6 = 2K: every cell you delete leaves a triple"),
    ]

    print()
    print("== edge cases ==")
    for board, k, expected, why in edges:
        got = valid_board(board, k)
        assert got == expected, (board, k, got, expected)
        assert reachable_reference(board, k) == expected, (board, k)
        print(f"  K={k}  {_show(normalize(board, k)[0]):<28} {str(got):<6} {why}")

    for bad, why in [(["XX", "X"], "ragged rows"),
                     (["XXX", "XXX"], "not square"),
                     (["XY ", "   ", "   "], "stray character"),
                     ([], "no rows")]:
        try:
            valid_board(bad)
        except ValueError:
            pass
        else:
            raise AssertionError(f"expected a ValueError for {why}: {bad}")
    print("  malformed boards raise ValueError rather than answering False --")
    print("  'not a board' is a different answer from 'not reachable'.")

    # K larger than the board: nobody can ever win, so only counts matter.
    tall = ["XXXX", "XXXX", "OOOO", "OOOO"]
    assert valid_board(tall, 5) is True and valid_board(tall, 4) is False
    print("  K=5 on a 4x4: no line can exist, so a full 8/8 board is reachable;")
    print("  the same board at K=4 is not, because those rows are wins.")

    # --- the fast core against the definition, and both against the search ----
    rng = random.Random(20260811)
    checked = counterexamples = 0

    for _ in range(4000):
        n = rng.choice((3, 4, 4, 5))
        k = rng.choice((2, 3, 3, n))
        marks = rng.randrange(0, min(9, n * n) + 1)
        board = _random_board(rng, n, marks)

        # last_move_cells (run cores, O(N^2)) == intersecting every window
        for player in (X, O):
            windows = winning_windows(board, player, k)
            expected = None if not windows else set.intersection(*(set(w) for w in windows))
            assert last_move_cells(board, player, k) == expected, (board, player, k)
            assert has_win(board, player, k) == bool(windows), (board, player, k)

        truth = reachable_reference(board, k)
        assert valid_board(board, k) == truth, (board, k)
        assert (count_sequences(board, k) > 0) == truth, (board, k)
        if valid_board_counting_only(board, k) != truth:
            counterexamples += 1
        checked += 1

    # And the other direction: anything actually played is accepted.
    played = 0
    for _ in range(3000):
        n = rng.choice((3, 4, 5, 6))
        k = rng.choice((3, 4, min(n, 5)))
        board = _random_game(rng, n, k, rng.randrange(0, n * n + 1))
        assert valid_board(board, k), (board, k)
        played += 1

    print()
    print("== randomized ==")
    print(f"  {checked:,} arbitrary boards (N in 3..5, K in 2..N), each checked three ways:")
    print("    run-core intersection == intersection of every winning window")
    print("    valid_board           == backward search over legal move orders")
    print("    valid_board           == (count_sequences > 0)")
    print(f"  {counterexamples:,} of them are boards the four LeetCode invariants get WRONG.")
    print(f"  {played:,} boards produced by actually playing legal games: all accepted.")

    print()
    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "Why is the 3x3 version allowed to skip the common-cell check?"
#     Because on 9 squares the check can never fire. Two disjoint 3-lines need 6
#     X marks, hence 5 O marks, hence 11 squares. Two 3-lines that do intersect
#     share a cell by definition, and three lines with no common cell need 7
#     marks (7 + 6 = 13). So (d) is implied by (a)-(c) at N = 3 and only at N = 3
#     -- the LeetCode solution is correct for a reason that evaporates the moment
#     the board grows. This is the single best thing to say in the extension.
#
# "Is 'no line + legal counts' really always reachable? It feels too strong."
#     It is the peel argument, and the reason it is airtight is that removing a
#     mark can never CREATE a line. So peel the last mover's marks in any order
#     at all: every intermediate board is a subset of a line-free board and is
#     therefore line-free, and reversing the peel is a legal game. No search, no
#     case analysis, and it is why the validator is O(N^2) rather than a hunt.
#
# "K = N (the board's own side) -- does anything simplify?"
#     Yes, substantially. There are only 2N+2 possible lines and no line has a
#     proper sub-window, so 'the winner's lines share a cell' is just 'the
#     winning lines pairwise intersect', and any two full-length lines on a
#     square board intersect unless they are parallel. So the only unreachable
#     win is two PARALLEL full lines (two full rows, two full columns, or... not
#     the diagonals, which always cross at the centre for odd N). That is a nice
#     sanity check to state before writing the general code.
#
# "Gomoku is 15x15 or 19x19 with K = 5. Does the O(N^2) claim hold?"
#     Yes -- the run scan is four passes over the board regardless of K, and the
#     running intersection is at most K cells. What does NOT hold is the rest of
#     real Gomoku: tournament rules add forbidden moves for black (double-three,
#     double-four, overline) and swap2 openings, and every one of them is an
#     extra reachability constraint on the FINAL position. Worth naming, because
#     it shows the difference between the puzzle and the game.
#
# "Now allow a player to pass, or allow more than two players."
#     Passing breaks invariant (a) entirely -- counts become |x - o| unbounded --
#     and the win logic is untouched. Three players cycling X, O, Z gives
#     count_X >= count_O >= count_Z >= count_X - 1, and the last mover is
#     whichever symbol's count is one ahead of the next in cycle order; (d) is
#     unchanged, because it only ever talked about one cell.
#
# "Count the number of legal games that end at this board." (count_sequences)
#     Exponential, and no closed form: without the win rule the answer is the
#     plain interleaving x! * o!, and the win rule prunes exactly those orders
#     where a line completes early. The DP over (which X marks placed, which O
#     marks placed) is the standard answer at 2^x * 2^o states, and the thing to
#     say is WHY it cannot be factored -- whether an order is legal depends on
#     the whole prefix, not on any per-cell quantity. It is also the best
#     available cross-check on the validator: count > 0 must equal reachable.
#
# "Make it streaming -- cells arrive one at a time."
#     Then you are answering a different and easier question, because you get to
#     watch the game happen: keep four run-length counters per cell (one per
#     axis) and a moves counter, and reject the first move that follows a win.
#     The reason the offline problem is harder is precisely that the ORDER has
#     been erased, and (d) is the only trace of it left in the final position.
#
# "How would you test it?"
#     The block above is the answer, and its shape matters more than its size.
#     3x3 is small enough to settle COMPLETELY -- all 19,683 boards against a
#     forward search from the empty board -- so there is no reason to sample it,
#     and getting 5,478 reachable positions and 255,168 games out the far end
#     confirms the model against numbers computed by other people a century ago.
#     Above 3x3 the exhaustive set explodes, so the ground truth becomes a
#     backward search that shares no logic with the validator, and the random
#     boards are drawn with legal counts on purpose -- a uniformly random board
#     is almost always rejected on counting alone and tests nothing.
