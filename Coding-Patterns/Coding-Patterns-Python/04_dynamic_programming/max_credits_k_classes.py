# Max Credits with K Classes (weighted interval scheduling, count-capped)
# Difficulty: Hard (LeetCode 1751; Medium once you see it is 1235 + one axis)
# Pattern: sort by end time, binary-search the predecessor, 2D DP over (class, count)
#
# Classes are (start_time, end_time, credit). Pick at most K of them, no two
# overlapping, and maximize total credit.
#
# FIRST, THE MISDIRECTION. "Classes" and "K" pull people straight to Course
# Schedule (LC 207 / 210) and they start building an adjacency list. There are
# no prerequisites here and no graph -- the only relation between two classes is
# whether their time ranges collide. This is interval scheduling. Say that out
# loud early; the rest of the round is easy once the wrong frame is dropped.
#
# WHY NOT GREEDY. Both obvious greedies lose:
#
#     by credit/hour, K=1:  (0,1,10) -> density 10, (0,10,50) -> density 5.
#                           Greedy takes 10, the answer is 50.
#     by raw credit, K=2:   (0,10,10) beats each of (0,4,6) and (5,9,6) alone,
#                           but the pair is 12.
#
# Taking a class costs you the *time window*, and the window's worth depends on
# what else could have filled it. That is a DP, not a sort.
#
# THE DP. Sort by end time, so "what is still available after class i" is a
# prefix of the same array. With classes 1..n in that order:
#
#     p(i) = number of classes whose end time is early enough to precede i
#     dp[i][j] = max credit using the first i classes, at most j of them
#              = max( dp[i-1][j],                    skip i
#                     credit[i] + dp[p(i)][j-1] )    take i
#
# The whole trick of the sorted-by-end order is that p(i) is a *prefix length*,
# found with one binary search, so "take i" jumps straight to a solved
# subproblem instead of re-scanning for compatible classes.
#
# THE BOUNDARY RULE -- ASK. Does a class ending at t conflict with one starting
# at t? Two different answers, two different binary searches:
#
#     half-open times (default, LC 1235):  end <= start is fine
#                                          p(i) = bisect_right(ends, start_i)
#     inclusive end DAYS (LC 1751):        need end < start, a shared day is a
#                                          conflict, p(i) = bisect_left(ends, start_i)
#
# bisect_right counts ends <= start; bisect_left counts ends < start. That one
# call is the entire difference between the two problems -- get it wrong and you
# fail exactly the tests where intervals touch.
#
# Time:  O(N log N + N*K)      sort + one binary search per class + the table
# Space: O(N) rolling over the K axis, O(N*K) if you want the schedule back
#
# Edge cases: K = 0 -> 0. K >= N -> the cap is inert, clamp it to N so the DP
# does not do K wasted passes. Everything overlapping -> falls out as the single
# best credit. Negative credits never get taken because "skip" is always an
# option. A zero-length class (start == end) satisfies `end <= start` against
# itself under the half-open rule, so p(i) is clamped to i -- otherwise the DP
# takes such a class twice.
#
# If the interviewer DROPS the K cap, delete the j axis and it is LC 1235
# (job_scheduling below, 1D). If they drop the credits and ask for the maximum
# *number* of events, it stops being a DP entirely -- that is the greedy
# min-heap-by-end-day problem (LC 1353), not this.

from bisect import bisect_left, bisect_right
from itertools import combinations
from typing import List, Sequence, Tuple

Class = Tuple[int, int, int]  # (start, end, credit)


def _prepare(classes: Sequence[Class], inclusive_end: bool) -> Tuple[List[Class], List[int]]:
    """Sort by end time and precompute p(i) for every class.

    Returns (ordered, prev) where prev[i] is the number of classes in `ordered`
    that finish early enough to be followed by ordered[i] -- i.e. the length of
    the still-usable prefix, which is exactly the row index the DP jumps to.
    """
    ordered = sorted(classes, key=lambda c: (c[1], c[0]))
    ends = [end for _, end, _ in ordered]
    # ends <= start when a shared boundary is legal, ends < start when it is not.
    search = bisect_left if inclusive_end else bisect_right
    # min(..., i) keeps a class from being its own predecessor: a zero-length
    # class (start == end) satisfies `end <= start` against itself, and without
    # the clamp the DP would happily take it twice.
    prev = [min(search(ends, start), i) for i, (start, _, _) in enumerate(ordered)]
    return ordered, prev


def max_credits(classes: Sequence[Class], k: int, inclusive_end: bool = False) -> int:
    """Max total credit from at most k non-overlapping classes.

    Rolls the DP over the count axis: row j only ever reads row j-1, so two
    length-(n+1) arrays are enough. O(N log N + N*K) time, O(N) space.
    """
    n = len(classes)
    k = min(k, n)                     # K >= N makes the cap inert
    if n == 0 or k <= 0:
        return 0

    ordered, prev = _prepare(classes, inclusive_end)

    row = [0] * (n + 1)               # j = 0: nothing may be taken, so all zero
    for _ in range(k):
        cur = [0] * (n + 1)
        for i in range(1, n + 1):
            take = ordered[i - 1][2] + row[prev[i - 1]]
            cur[i] = max(cur[i - 1], take)
        row = cur
    return row[n]


def max_credits_with_schedule(
    classes: Sequence[Class], k: int, inclusive_end: bool = False
) -> Tuple[int, List[Class]]:
    """(best credit, the classes that achieve it) -- full table, then walk back.

    Same recurrence, but keeps all N*K cells so the choices can be recovered.
    Reach for this when asked "which classes?" and not just "how much?".
    """
    n = len(classes)
    k = min(k, n)
    if n == 0 or k <= 0:
        return 0, []

    ordered, prev = _prepare(classes, inclusive_end)

    dp = [[0] * (k + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        credit, p = ordered[i - 1][2], prev[i - 1]
        for j in range(1, k + 1):
            dp[i][j] = max(dp[i - 1][j], credit + dp[p][j - 1])

    # Backtrack: if the cell matches "skip", it was a skip; otherwise it was a
    # take, so drop to the predecessor row and spend one of the j slots.
    chosen: List[Class] = []
    i, j = n, k
    while i > 0 and j > 0:
        if dp[i][j] == dp[i - 1][j]:
            i -= 1
        else:
            chosen.append(ordered[i - 1])
            i, j = prev[i - 1], j - 1
    chosen.reverse()
    return dp[n][k], chosen


def max_value(events: Sequence[Sequence[int]], k: int) -> int:
    """LeetCode 1751 -- events[i] = [startDay, endDay, value], at most k attended.

    End days are INCLUSIVE, so [1,2] and [2,3] collide over day 2.
    """
    return max_credits([(e[0], e[1], e[2]) for e in events], k, inclusive_end=True)


def job_scheduling(start_time: List[int], end_time: List[int], profit: List[int]) -> int:
    """LeetCode 1235 -- the same problem with no cap, so the j axis disappears.

    Have this ready: interviewers like to remove K mid-round and watch whether
    you rebuild from scratch or just delete a loop.
    """
    jobs = sorted(zip(end_time, start_time, profit))
    ends = [end for end, _, _ in jobs]

    dp = [0] * (len(jobs) + 1)
    for i, (_, start, p) in enumerate(jobs, start=1):
        dp[i] = max(dp[i - 1], p + dp[bisect_right(ends, start)])
    return dp[-1]


def _greedy_by_density(classes: Sequence[Class], k: int) -> int:
    """The tempting wrong answer, kept only so the tests can show it losing."""
    total, last_end, taken = 0, float("-inf"), 0
    for start, end, credit in sorted(classes, key=lambda c: -credit_density(c)):
        if taken == k:
            break
        if start >= last_end:
            total, last_end, taken = total + credit, end, taken + 1
    return total


def credit_density(c: Class) -> float:
    start, end, credit = c
    return credit / (end - start) if end > start else float("inf")


def _brute(classes: Sequence[Class], k: int, inclusive_end: bool = False) -> int:
    """Every subset of size <= k, for cross-checking the DP on small inputs."""
    best = 0
    for size in range(min(k, len(classes)) + 1):
        for combo in combinations(classes, size):
            # (start, end): with equal starts only a zero-length class can fit
            # first, and sorting by start alone would hide that arrangement.
            combo = sorted(combo, key=lambda c: (c[0], c[1]))
            ok = all(
                combo[t][1] < combo[t + 1][0] if inclusive_end else combo[t][1] <= combo[t + 1][0]
                for t in range(len(combo) - 1)
            )
            if ok:
                best = max(best, sum(c[2] for c in combo))
    return best


# ---- Tests ----
if __name__ == "__main__":
    import random

    # The two greedies from the header, both wrong, both fixed by the DP.
    density_trap = [(0, 1, 10), (0, 10, 50)]
    assert _greedy_by_density(density_trap, 1) == 10
    assert max_credits(density_trap, 1) == 50

    credit_trap = [(0, 10, 10), (0, 4, 6), (5, 9, 6)]
    assert max_credits(credit_trap, 1) == 10          # one class: the big one
    assert max_credits(credit_trap, 2) == 12          # two: the pair beats it

    # K edge cases.
    classes = [(1, 3, 50), (2, 5, 20), (4, 6, 70), (6, 9, 30)]
    assert max_credits(classes, 0) == 0               # cap of zero
    assert max_credits([], 5) == 0                    # nothing to take
    assert max_credits(classes, 1) == 70              # best single
    assert max_credits(classes, 2) == 120             # (1,3,50) + (4,6,70)
    assert max_credits(classes, 3) == 150             # ...+ (6,9,30)
    assert max_credits(classes, 99) == max_credits(classes, 4) == 150   # cap inert

    # Everything overlaps -> the single best credit, whatever K says.
    stacked = [(0, 10, 5), (1, 9, 40), (2, 8, 12)]
    assert all(max_credits(stacked, k) == 40 for k in (1, 2, 3, 10))

    # Touching endpoints: the one clarifying question, and it changes the answer.
    touching = [(0, 5, 10), (5, 10, 10)]
    assert max_credits(touching, 2) == 20                        # half-open: both
    assert max_credits(touching, 2, inclusive_end=True) == 10    # inclusive: conflict

    # Negative credits are simply never taken.
    assert max_credits([(0, 1, -5), (2, 3, 4)], 2) == 4

    # The schedule really is legal, and really sums to the reported total.
    best, picked = max_credits_with_schedule(classes, 2)
    assert best == 120 and picked == [(1, 3, 50), (4, 6, 70)]
    for k in range(5):
        total, sched = max_credits_with_schedule(classes, k)
        assert len(sched) <= k
        assert all(sched[t][1] <= sched[t + 1][0] for t in range(len(sched) - 1))
        assert sum(c[2] for c in sched) == total == max_credits(classes, k)

    # LeetCode 1751 -- inclusive end days, so [1,2] and [2,3] cannot both go.
    assert max_value([[1, 2, 4], [3, 4, 3], [2, 3, 1]], 2) == 7
    assert max_value([[1, 2, 4], [3, 4, 3], [2, 3, 10]], 2) == 10
    assert max_value([[1, 1, 1], [2, 2, 2], [3, 3, 3], [4, 4, 4]], 3) == 9

    # LeetCode 1235 -- the uncapped 1D fallback.
    assert job_scheduling([1, 2, 3, 3], [3, 4, 5, 6], [50, 10, 40, 70]) == 120
    assert job_scheduling([1, 2, 3, 4, 6], [3, 5, 10, 6, 9], [20, 20, 100, 70, 60]) == 150
    assert job_scheduling([1, 1, 1], [2, 3, 4], [5, 6, 4]) == 6

    # Uncapped 1235 == capped DP with K = N, on random data.
    random.seed(7)
    for _ in range(300):
        n = random.randint(0, 7)
        pool = []
        for _ in range(n):
            s = random.randint(0, 12)
            pool.append((s, s + random.randint(0, 6), random.randint(-3, 30)))
        for k in range(n + 2):
            assert max_credits(pool, k) == _brute(pool, k), (pool, k)
            assert max_credits(pool, k, True) == _brute(pool, k, True), (pool, k)
            total, sched = max_credits_with_schedule(pool, k)
            assert total == max_credits(pool, k) and sum(c[2] for c in sched) == total
        # LC 1235 guarantees start < end, so compare on the non-degenerate pool.
        proper = [c for c in pool if c[0] < c[1]]
        if proper:
            starts, ends, credits = zip(*proper)
            assert job_scheduling(list(starts), list(ends), list(credits)) == max_credits(
                proper, len(proper)
            ), proper

    # Demo.
    for k in range(1, 4):
        total, sched = max_credits_with_schedule(classes, k)
        print(f"K={k}  credit={total:>3}  take {sched}")

    print("All tests passed.")
