# LeetCode 322 - Coin Change
# Difficulty: Medium
# Pattern: 1D DP (unbounded knapsack)
#
# Problem: Given coin denominations and an amount, return the fewest
# coins needed to make up that amount. Return -1 if not possible.
#
# Approach: dp[i] = min coins to make amount i.
# For each amount, try each coin: dp[i] = min(dp[i], dp[i - coin] + 1)
#
# Time: O(amount * len(coins))  Space: O(amount)

from typing import List

def coin_change(coins: List[int], amount: int) -> int:
    dp = [float('inf')] * (amount + 1)
    dp[0] = 0  # base case: 0 coins to make amount 0

    for i in range(1, amount + 1):
        for coin in coins:
            if coin <= i:
                dp[i] = min(dp[i], dp[i - coin] + 1)

    return dp[amount] if dp[amount] != float('inf') else -1


# ---- Tests ----
if __name__ == "__main__":
    assert coin_change([1, 5, 11], 15) == 3   # 11+1+1+1 vs 5+5+5 -> 3
    assert coin_change([1, 2, 5], 11) == 3    # 5+5+1
    assert coin_change([2], 3) == -1
    assert coin_change([1], 0) == 0
    print("All tests passed.")
