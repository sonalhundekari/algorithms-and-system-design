# LeetCode 121 - Best Time to Buy and Sell Stock
# Difficulty: Easy
# Pattern: Single pass / min tracker
#
# Problem: Given an array prices where prices[i] is the stock price on day i,
# return the maximum profit from one buy and one sell (buy before sell).
#
# Approach: Track the minimum price seen so far. At each day, the best
# profit is current_price - min_so_far. Track the max of these.
#
# Time: O(n)  Space: O(1)

from typing import List

def max_profit(prices: List[int]) -> int:
    min_price = float('inf')
    max_profit = 0
    for price in prices:
        min_price = min(min_price, price)
        max_profit = max(max_profit, price - min_price)
    return max_profit


# ---- Tests ----
if __name__ == "__main__":
    assert max_profit([7, 1, 5, 3, 6, 4]) == 5
    assert max_profit([7, 6, 4, 3, 1]) == 0   # no profit possible
    assert max_profit([1]) == 0
    print("All tests passed.")
