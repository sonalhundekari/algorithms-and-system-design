# Pattern: Dynamic Programming

## Key Techniques
- **1D DP** — dp[i] depends on dp[i-1] or dp[i-k]
- **2D DP** — dp[i][j] depends on subproblems of both sequences
- **Bottom-up (tabulation)** — fill table iteratively, no recursion overhead
- **Top-down (memoization)** — recursive + cache; easier to reason about
- **DP table + outer sweep** — tabulate once for *all* subproblems, then minimize over a free parameter; prove the parameter's range is bounded
- **Sort + binary-searched predecessor** — order intervals by end time so "what's still available" is a prefix; one `bisect` per item replaces a scan
- **Read the constraint before building the table** — a "pick exactly one kind" rule collapses a knapsack shape into a closed form per candidate plus one linear scan

## Problems

| Problem | LeetCode | Difficulty | Type |
|---------|----------|------------|------|
| Coin Change | #322 | Medium | 1D DP (unbounded knapsack) |
| Longest Increasing Subsequence | #300 | Medium | 1D DP / patience sort |
| Word Break | #139 | Medium | 1D DP + set lookup |
| Edit Distance | #72 | Medium | 2D DP |
| House Robber | #198 | Medium | 1D DP |
| Min Coins to Pay with Change Allowed | — | Medium | Coin-change DP + bounded sweep over the overpayment |
| Max Credits with K Classes | #1751 (#1235 uncapped) | Hard | Weighted interval scheduling + a count axis |
| Single Query Type, Max Revenue in K Minutes | — | Easy (Hard if you write the knapsack) | Closed form `(k // d) * r` + one scan; unbounded knapsack is only the follow-up |

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
#   half-open times (LC 1235):   end <= start ok    -> bisect_right(ends, start)
#   inclusive end days (LC 1751): need end < start  -> bisect_left(ends, start)
# min(..., i) stops a zero-length class from being its own predecessor.
# K = 0 -> 0; K >= N -> clamp to N; all overlapping -> the single best credit.
# Drop the K axis and it is LC 1235 (1D). Drop the *credits* and it stops being
# a DP at all -- max COUNT of events is the greedy min-heap-by-end-day, LC 1353.

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
# (k // 1) * r overflows 32 bits outside Python. Follow-ups: allow a MIX and it
# finally IS unbounded knapsack, dp[t] = max(dp[t-1], max(dp[t-d] + r)) --
# strictly better than the single-type answer (14 > 12 above: one (6,10) plus
# four (1,1)), which is therefore a lower bound. Allow PARTIAL runs and the floor
# vanishes, so the density greedy is at last correct: k * max(r / d).
```
