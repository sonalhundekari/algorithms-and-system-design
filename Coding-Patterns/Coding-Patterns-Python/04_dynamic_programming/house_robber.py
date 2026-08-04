# LeetCode 198 - House Robber
# Difficulty: Medium
# Pattern: 1D DP (space-optimized)
#
# Problem: Given an array of non-negative integers representing the
# amount of money in each house, return the maximum amount you can rob
# without robbing two adjacent houses.
#
# Approach: At each house, decide: rob it (prev_prev + current) or skip it (prev).
# Only need to track last two values — no full array needed.
#
# Time: O(n)  Space: O(1)

from typing import List

def rob(nums: List[int]) -> int:
    prev2, prev1 = 0, 0
    for num in nums:
        curr = max(prev1, prev2 + num)
        prev2, prev1 = prev1, curr
    return prev1


# ---- Tests ----
if __name__ == "__main__":
    assert rob([1, 2, 3, 1]) == 4    # rob houses 1 and 3
    assert rob([2, 7, 9, 3, 1]) == 12 # rob houses 1, 3, 5
    assert rob([0]) == 0
    print("All tests passed.")
