# LeetCode 153 - Find Minimum in Rotated Sorted Array
# Difficulty: Medium
# Pattern: Binary search on rotation pivot
#
# Problem: Given a rotated sorted array with distinct values,
# find the minimum element.
#
# Approach: The minimum is at the rotation pivot.
# If nums[mid] > nums[hi], the pivot is in the right half → lo = mid + 1.
# Otherwise the pivot is in the left half (or mid itself) → hi = mid.
# Loop ends when lo == hi == index of minimum.
#
# Time: O(log n)  Space: O(1)

from typing import List

def find_min(nums: List[int]) -> int:
    lo, hi = 0, len(nums) - 1

    while lo < hi:
        mid = (lo + hi) // 2
        if nums[mid] > nums[hi]:
            lo = mid + 1  # min is in right half
        else:
            hi = mid      # min is in left half (inclusive of mid)

    return nums[lo]


# ---- Tests ----
if __name__ == "__main__":
    assert find_min([3,4,5,1,2]) == 1
    assert find_min([4,5,6,7,0,1,2]) == 0
    assert find_min([11,13,15,17]) == 11  # not rotated
    assert find_min([2,1]) == 1
    print("All tests passed.")
