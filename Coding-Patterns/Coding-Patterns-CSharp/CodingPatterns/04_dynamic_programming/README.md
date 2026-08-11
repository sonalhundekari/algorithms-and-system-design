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
```
