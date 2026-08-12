# Pattern: Dynamic Programming

## Key Techniques
- **1D DP** — dp[i] depends on dp[i-1] or dp[i-k]
- **2D DP** — dp[i][j] depends on subproblems of both sequences
- **Bottom-up (tabulation)** — fill table iteratively, no recursion overhead
- **Top-down (memoization)** — recursive + cache; easier to reason about
- **Bitmask / subset DP** — n ≤ ~20; iterate submasks for O(3^n) partitioning
- **Binary search the answer** — when feasibility is monotone, optimize → decide
- **DP table + outer sweep** — tabulate once for *all* subproblems, then minimize over a free parameter; prove the parameter's range is bounded
- **Sort + binary-searched predecessor** — order intervals by end time so "what's still available" is a prefix; one binary search per item replaces a scan
- **Two pointers + a signed balance** — when two sequences advance at different rates, carry "how far ahead one side is" as a third state axis; only the side that is behind may move
- **Read the constraint before building the table** — a "pick exactly one kind" rule collapses a knapsack shape into a closed form per candidate plus one linear scan

## Problems

| Problem | LeetCode | Difficulty | Type |
|---------|----------|------------|------|
| Coin Change | #322 | Medium | 1D DP (unbounded knapsack) |
| Longest Increasing Subsequence | #300 | Medium | 1D DP / patience sort |
| Word Break | #139 | Medium | 1D DP + set lookup |
| Edit Distance | #72 | Medium | 2D DP |
| House Robber | #198 | Medium | 1D DP |
| Find Minimum Time to Finish All Jobs | #1723 | Hard | Binary search + pruned DFS; subset DP O(k·3^n) |
| Min Coins to Pay with Change Allowed | — | Medium | Coin-change DP + bounded sweep over the overpayment |
| Max Credits with K Classes | #1751 (#1235 uncapped) | Hard | Weighted interval scheduling + a count axis |
| Original String Exists (Two Encoded Strings) | #2060 | Hard | Top-down DP on (i, j, diff); wildcards absorb the other side's letters |
| Single Query Type, Max Revenue in K Minutes | — | Easy (Hard if you write the knapsack) | Closed form `(k / d) * r` + one scan; unbounded knapsack is only the follow-up |

## Pattern Cheat Sheet

```python
# 1D DP template
dp = [initial] * (n + 1)
dp[base] = base_value
for i in range(1, n + 1):
    for choice in choices:
        dp[i] = optimal(dp[i], dp[i - choice] + cost)

# 2D DP template (e.g. two sequences)
dp = [[0] * (m + 1) for _ in range(n + 1)]
for i in range(1, n + 1):
    for j in range(1, m + 1):
        if match(i, j):
            dp[i][j] = dp[i-1][j-1] + value
        else:
            dp[i][j] = optimal(dp[i-1][j], dp[i][j-1]) + cost

# Balanced k-way partitioning (makespan). Greedy is only a 4/3 approximation --
# [3,3,2,2,2] with k=2 gives 7, the optimum is 6 -- so it has to be searched.
#
# 1. Binary search the answer; feasibility is monotone in the limit.
lo, hi = max(max(jobs), ceil(total / k)), total
while lo < hi:
    mid = (lo + hi) // 2
    if can_place(sorted(jobs, reverse=True), [0] * k, mid):
        hi = mid
    else:
        lo = mid + 1

# The two prunings that make the DFS finish: biggest job first (fail near the
# root) and SKIP A WORKER WHOSE LOAD YOU ALREADY TRIED (workers are
# interchangeable, so equal loads are the same subtree -- this collapses every
# idle worker into one branch).
for i, load in enumerate(loads):
    if load + job > limit or load in loads[:i]:
        continue

# 2. Or skip the search: subset DP, O(k * 3^n), no dependence on the values.
#    dp[mask] = min over submask sub: max(sum[sub], prev_dp[mask ^ sub])
sub = mask
while sub:
    dp[mask] = min(dp[mask], max(sum_[sub], prev[mask ^ sub]))
    sub = (sub - 1) & mask

# Coin change WHEN CHANGE COMES BACK. Overpaying is allowed and the recipient
# returns exact change, so a transaction is just "what you hand over":
#     answer = min over paid >= n of  coins(paid) + coins(paid - n)
# n=41 with {1,5,10,50,100,200}: plain LC 322 says 5 (10+10+10+10+1); paying
# 51 and taking a 10 back is 3. Missing this branch is the whole trick.
dp = coins_table(n + max(denoms))          # one 322 table, reused for every amount
best = min(dp[n + c] + dp[c] for c in range(max(denoms) + 1))

# The cutoff is what gets pushed on. c is the change; if c >= D (largest coin)
# then coins(n+c) + coins(c) = coins(n+c-D) + coins(c-D) + 2 -- an extra 200
# handed over and handed straight back is two wasted coins. So c in [0, D).
#
# Greedy for coins(x) is valid ONLY on a canonical set. {1,5,10,50,100,200} is a
# divisibility chain (1|5|10|50|100|200): never hold d[i+1]/d[i] of a coin, swap
# up instead, so each count is forced to floor(rest / d[j]). {1,3,4} is not --
# greedy(6) = 4+1+1 = 3, optimal 3+3 = 2 -- there the DP is mandatory.

# WEIGHTED INTERVAL SCHEDULING WITH A COUNT CAP (max credit, at most K classes).
# "Classes" + "K" is bait for Course Schedule -- there is no graph here, only
# overlap. Greedy by credit/hour loses: (0,1,10) vs (0,10,50), K=1 -> 50, not 10.
ordered = sorted(classes, key=lambda c: (c[1], c[0]))      # by END time
ends = [e for _, e, _ in ordered]
prev = [min(bisect_right(ends, s), i) for i, (s, _, _) in enumerate(ordered)]

row = [0] * (n + 1)                                        # j = 0 row
for _ in range(k):                                         # roll the count axis
    cur = [0] * (n + 1)
    for i in range(1, n + 1):
        cur[i] = max(cur[i - 1],                           # skip class i
                     ordered[i-1][2] + row[prev[i-1]])     # take it, one slot down
    row = cur

# ASK ABOUT THE BOUNDARY -- it is the whole difference between the two versions:
#   half-open times (LC 1235):    end <= start ok   -> UpperBound(ends, start)
#   inclusive end days (LC 1751): need end < start  -> LowerBound(ends, start)
# min(..., i) stops a zero-length class from being its own predecessor.
# K = 0 -> 0; K >= N -> clamp to N; all overlapping -> the single best credit.
# Drop the K axis and it is LC 1235 (1D). Drop the *credits* and it stops being
# a DP at all -- max COUNT of events is the greedy min-heap-by-end-day, LC 1353.

# TWO ENCODED STRINGS, ONE ORIGINAL (#2060). Digits replace hidden substrings by
# their LENGTH, so a run of digits is ambiguous ("123" = 123, 12+3, 1+23, 1+2+3)
# and every hidden segment is a WILDCARD that swallows the other side's letters.
# State: (i, j, diff), diff = characters s1 has committed minus s2's. Only the
# side that is BEHIND may advance:
def solve(i, j, diff):                       # memo on all THREE coordinates
    if i == len(s1) and j == len(s2): return diff == 0
    if i < len(s1) and s1[i].isdigit():      # extend the number one digit at a
        v = 0                                # time -- that enumerates the splits
        for k in range(i, min(i + 3, len(s1))):
            if not s1[k].isdigit(): break
            v = v * 10 + int(s1[k])
            if solve(k + 1, j, diff + v): return True
    elif j < len(s2) and s2[j].isdigit():    # mirror image, diff goes down
        ...
    elif diff == 0:                          # same index of the original ->
        return s1[i] == s2[j] and solve(i + 1, j + 1, 0)      # must be EQUAL
    elif diff > 0:                           # s1's wildcard eats s2's letter,
        return solve(i, j + 1, diff - 1)     # whatever it is -- NO comparison
    else:
        return solve(i + 1, j, diff + 1)
    return False

# "l123e" vs "44" -> true: 4+4 hidden chars absorb 'l', 'e' and 1+2+3 in between.
# Traps: comparing letters while diff != 0 (they are being eaten, not matched);
# pairing numbers against numbers instead of adding them to a balance; memoising
# on (i, j) alone. "At most 3 digits in a row" is the bound that matters: no
# number exceeds 999, and a run is only entered with diff already toward 0, so
# |diff| <= 999 and the table is 41 x 41 x 2001, not unbounded.

# THE KNAPSACK THAT ISN'T -- pick ONE query type, run it back-to-back for k
# minutes. A time budget with a per-run cost and a per-run revenue screams
# unbounded knapsack, but "only a single type may be selected" kills both
# decisions a knapsack makes (which item next, how many of it), so every
# candidate has a closed form and the answer is one scan:
best = max(((k // d) * r, -i) for i, (d, r) in enumerate(types))   # -i: ties -> lowest index
# O(n) / O(1) against the DP's O(n*k) / O(k) -- and k is minutes, a VALUE, not a
# size, so the DP is pseudo-polynomial on an input three numbers wide.
#
# RANK BY NO PROXY: floor() is not monotone in any single column.
#   k = 10, types = [(6,10), (5,6), (1,1)]
#   best per run -> (6,10) = 10   best density -> (6,10) = 10   shortest -> (1,1) = 10
#   the answer is (5,6) twice = 12. The wasted tail k % d is what breaks every
#   greedy, and the size of that tail depends on the candidate.
#
# Edges: d > k -> 0 runs, 0 revenue (a losing candidate, not an error); d == 0 ->
# unbounded, reject and ask; r < 0 -> the best count is 0 if idling is allowed;
# (k // 1) * r overflows 32 bits. Follow-ups: allow a MIX and it finally IS
# unbounded knapsack, dp[t] = max(dp[t-1], max(dp[t-d] + r)) -- strictly better
# than the single-type answer (14 > 12 above: one (6,10) plus four (1,1)), which
# is therefore a lower bound. Allow PARTIAL runs and the floor vanishes, so the
# density greedy is at last correct: k * max(r / d).
```
