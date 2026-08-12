# Single Query Type, Max Revenue in K Minutes (Snowflake-flavoured)
# Difficulty: Easy once the constraint is read; Hard if you start writing knapsack
# Pattern: closed form per candidate + one linear scan -- the DP that is NOT needed
#
# A virtual warehouse can execute one of n query types. Query type i takes
# duration_i minutes per run and earns revenue_i per run. Pick ONE query type,
# run it back-to-back for k minutes, and report the type and the total revenue.
#
#     k = 10, types = [(6, 10), (5, 6), (1, 1)]      (duration, revenue)
#         type 0:  10 // 6 = 1 run    ->  10
#         type 1:  10 // 5 = 2 runs   ->  12   <- answer
#         type 2:  10 // 1 = 10 runs  ->  10
#
# THE WHOLE PROBLEM IS THE WORD "ONE". Runs cost time and pay revenue, the budget
# is k, so every instinct says unbounded knapsack (LC 322's cousin) and people
# start allocating dp = [0] * (k + 1). Read the constraint again: only a single
# type may be selected. That removes the only real decision a knapsack makes --
# *which* item to spend the next minute on. With the mix forbidden the count is
# not a choice either: revenue is non-negative, so you run the chosen type as many
# times as physically fit. Both axes collapse and each candidate has a closed form:
#
#     revenue(i) = (k // duration_i) * revenue_i
#
# n candidates, O(1) each. One scan, O(n) time, O(1) space, and it is exactly
# optimal -- there is nothing left for a DP to search. Say that sentence out loud
# before writing code; the round is graded on noticing, not on typing.
#
# It also matters at the scale this problem is dressed in. Knapsack over minutes
# is O(n * k) time and O(k) memory -- pseudo-polynomial, driven by the VALUE of k,
# not its size. A day is 1440 minutes and that is fine; a quarter in seconds is
# not. The scan does not care what k is.
#
# EVERY GREEDY SHORTCUT IS ALSO WRONG -- floor division is not monotone in any
# single column, which the example above is built to show:
#
#     highest revenue per run  -> type 0 (10 per run)    gives 10, answer is 12
#     highest revenue density  -> type 0 (10/6 = 1.67)   gives 10, answer is 12
#     shortest duration        -> type 2 (1 minute)      gives 10, answer is 12
#
# Density is the seductive one, and it is only right when the leftover time can be
# used -- i.e. after the floor disappears (see best_fractional below). The wasted
# tail k % duration_i is what breaks it, and the tail depends on the candidate.
# So: evaluate all n, do not rank by a proxy.
#
# CLARIFY BEFORE CODING (these are the questions the round is actually probing):
#
#   duration_i > k        Zero runs, zero revenue. Not an error -- just a losing
#                         candidate. If EVERY type is too long the answer is 0 and
#                         "which type" is arbitrary; agree on a convention.
#   duration_i == 0       Degenerate: infinitely many runs, unbounded revenue.
#                         There is no correct answer, so reject the input rather
#                         than silently dividing. Ask; do not assume.
#   revenue_i < 0         Only possible if "revenue" is really net of cost. Then
#                         running the max number of times is no longer optimal --
#                         the best count is 0. Handled by idle_allowed below.
#   overflow              Free in Python; in C#/Java this is k * max-revenue and
#                         needs 64-bit. Mention it even here -- interviewers ask.
#   ties                  Stated as arbitrary; return the lowest index anyway so
#                         the function is deterministic and testable.
#
# FOLLOW-UPS, in the order interviewers ask them:
#
#   "Now allow any mix of types."   -> best_mix: the unbounded knapsack you were
#                                      about to write, O(n * k) / O(k), with the
#                                      counts recovered. On the example above it
#                                      returns 14 (one type-0 run + four type-2),
#                                      strictly better than the single-type 12 --
#                                      the single-type answer is a lower bound.
#   "Runs can be interrupted."      -> best_fractional: the floor vanishes, so the
#                                      density greedy that loses above becomes
#                                      correct: k * max(revenue_i / duration_i).
#   "Each type has a run limit."    -> bounded knapsack: binary-split each type
#                                      into 1, 2, 4, ... copies, then 0/1 knapsack,
#                                      O(n * k * log limit).
#   "Many k values, same types."    -> the scan is already O(n) per query; if k is
#                                      huge and n small, note the answer as a
#                                      function of k is piecewise constant and only
#                                      steps at multiples of the durations.
#
# Time: O(n)   Space: O(1)

from typing import List, Sequence, Tuple

QueryType = Tuple[int, int]  # (duration_minutes, revenue_per_run)


def best_single_type(types: Sequence[QueryType], k: int, idle_allowed: bool = False) -> Tuple[int, int, int]:
    """THE ANSWER: (type_index, runs, revenue). One division per type, no DP, no sort.

    idle_allowed only matters when revenues can be negative: it lets the warehouse
    run nothing at all (index -1, revenue 0) instead of being forced to lose money.
    With non-negative revenues it changes nothing.
    """
    if k < 0:
        raise ValueError("Time budget cannot be negative.")

    # Idle is the floor when losses are possible; otherwise there is no baseline
    # and even an all-too-slow candidate (0 runs, 0 revenue) beats it.
    best = (-1, 0, 0) if idle_allowed else (-1, 0, float("-inf"))

    for i, (duration, revenue_per_run) in enumerate(types):
        if duration <= 0:
            raise ValueError(
                f"Query type {i} has duration {duration}: a run must take time, "
                "otherwise revenue is unbounded."
            )

        runs = k // duration                    # duration > k -> 0 runs, 0 revenue
        revenue = runs * revenue_per_run

        if idle_allowed and revenue < 0:        # a losing type is best run zero times
            runs, revenue = 0, 0

        if revenue > best[2]:                   # strict > keeps the lowest index on a tie
            best = (i, runs, revenue)

    return best if best[0] >= 0 else (-1, 0, 0)  # (-1, 0, 0) only for an empty catalogue


def best_mix(types: Sequence[QueryType], k: int) -> Tuple[int, List[int]]:
    """FOLLOW-UP: drop the single-type rule and any mix is legal.

    NOW it is unbounded knapsack -- dp[t] = best revenue within t minutes -- walked
    forward so a type can be reused. O(n * k) time, O(k) space: pseudo-polynomial,
    viable only while k stays small. That cost gap is the point of the comparison.
    """
    if k < 0:
        raise ValueError("Time budget cannot be negative.")

    dp = [0] * (k + 1)
    last_used = [-1] * (k + 1)          # which type's run ended at minute t, if any

    for t in range(1, k + 1):
        dp[t] = dp[t - 1]               # leave the minute idle...
        last_used[t] = -1               # ...so no run ended here
        for i, (duration, revenue_per_run) in enumerate(types):
            if duration <= 0 or duration > t:
                continue
            candidate = dp[t - duration] + revenue_per_run
            if candidate > dp[t]:
                dp[t], last_used[t] = candidate, i

    counts = [0] * len(types)           # walk back through the finished runs
    t = k
    while t > 0:
        pick = last_used[t]
        if pick < 0:
            t -= 1
            continue
        counts[pick] += 1
        t -= types[pick][0]
    return dp[k], counts


def best_fractional(types: Sequence[QueryType], k: int) -> Tuple[int, float]:
    """FOLLOW-UP: runs may be cut off mid-flight and still pay pro rata.

    The floor disappears, so no time is ever wasted and the density greedy that
    loses above becomes exactly right: pour the whole budget into the best ratio.
    O(n), and an upper bound on both answers above.
    """
    best_index, best_density = -1, 0.0   # idling beats a negative rate
    for i, (duration, revenue_per_run) in enumerate(types):
        if duration <= 0:
            raise ValueError("Duration must be positive.")
        density = revenue_per_run / duration
        if density > best_density:
            best_index, best_density = i, density
    return best_index, best_density * k


# ---- Tests ----
if __name__ == "__main__":
    import random

    # The worked example: every one-column greedy picks a different loser.
    types = [(6, 10), (5, 6), (1, 1)]
    assert best_single_type(types, 10) == (1, 2, 12)     # not type 0 (per run / density)
    assert best_mix(types, 10) == (14, [1, 0, 4])        # mixing strictly beats 12
    index, revenue = best_fractional(types, 10)
    assert index == 0 and abs(revenue - 100 / 6) < 1e-9

    # Every duration exceeds the budget: zero runs, zero revenue, index arbitrary.
    assert best_single_type([(50, 900), (99, 5)], 10) == (0, 0, 0)
    assert best_single_type(types, 0) == (0, 0, 0)       # k = 0, same story
    assert best_single_type([], 10) == (-1, 0, 0)        # nothing to pick

    # Ties resolve to the lowest index (both earn 12).
    assert best_single_type([(2, 3), (4, 6)], 8)[0] == 0

    # Losses: forced to pick you take the least-bad, allowed to idle you idle.
    lossy = [(3, -1), (2, -5)]
    assert best_single_type(lossy, 10) == (0, 3, -3)
    assert best_single_type(lossy, 10, idle_allowed=True) == (-1, 0, 0)

    # Degenerate duration is rejected, not answered.
    for bad in ([(0, 5)], [(-2, 5)]):
        try:
            best_single_type(bad, 10)
            raise AssertionError("expected a rejection")
        except ValueError:
            pass

    # The single-type answer is always a lower bound on the mixed answer.
    rng = random.Random(7)
    for _ in range(500):
        sample = [(rng.randint(1, 12), rng.randint(0, 40)) for _ in range(rng.randint(1, 5))]
        budget = rng.randint(0, 60)
        assert best_single_type(sample, budget)[2] <= best_mix(sample, budget)[0]

        # And brute force agrees with the closed form on every candidate.
        brute = max(
            (sum(revenue for _ in range(budget // duration)), -i)
            for i, (duration, revenue) in enumerate(sample)
        )
        assert best_single_type(sample, budget)[2] == brute[0]

    print("All tests passed.")
