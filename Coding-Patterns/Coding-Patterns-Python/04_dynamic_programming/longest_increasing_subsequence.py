# LeetCode 300 - Longest Increasing Subsequence
# Difficulty: Medium
# Pattern: 1D DP (or patience sort with binary search for O(n log n))
#
# Problem: Given an integer array nums, return the length of the
# longest strictly increasing subsequence.
#
# Approach 1 (O(n^2) DP): dp[i] = LIS ending at index i.
# Approach 2 (O(n log n)): Maintain a "tails" array where tails[i]
# is the smallest tail element of all increasing subsequences of length i+1.
# Use binary search to update tails.
#
# Time: O(n log n)  Space: O(n)

from typing import List
import bisect

def length_of_lis(nums: List[int]) -> int:
    tails = []  # tails[i] = smallest tail for LIS of length i+1
    for num in nums:
        pos = bisect.bisect_left(tails, num)
        if pos == len(tails):
            tails.append(num)
        else:
            tails[pos] = num  # replace with smaller tail
    return len(tails)


# ---- Tests ----
if __name__ == "__main__":
    assert length_of_lis([10, 9, 2, 5, 3, 7, 101, 18]) == 4  # [2,3,7,101]
    assert length_of_lis([0, 1, 0, 3, 2, 3]) == 4
    assert length_of_lis([7, 7, 7, 7]) == 1
    print("All tests passed.")
