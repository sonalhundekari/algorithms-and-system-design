# Pattern: Dynamic Programming

## Key Techniques
- **1D DP** — dp[i] depends on dp[i-1] or dp[i-k]
- **2D DP** — dp[i][j] depends on subproblems of both sequences
- **Bottom-up (tabulation)** — fill table iteratively, no recursion overhead
- **Top-down (memoization)** — recursive + cache; easier to reason about

## Problems

| Problem | LeetCode | Difficulty | Type |
|---------|----------|------------|------|
| Coin Change | #322 | Medium | 1D DP (unbounded knapsack) |
| Longest Increasing Subsequence | #300 | Medium | 1D DP / patience sort |
| Word Break | #139 | Medium | 1D DP + set lookup |
| Edit Distance | #72 | Medium | 2D DP |
| House Robber | #198 | Medium | 1D DP |

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
```
