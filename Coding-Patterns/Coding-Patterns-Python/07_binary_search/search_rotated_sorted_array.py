# LeetCode 33 - Search in Rotated Sorted Array
# Difficulty: Medium
# Pattern: Binary search with rotation check
#
# Problem: Given a rotated sorted array with distinct values, search for target.
# Return its index or -1 if not found.
#
# Approach: At every mid, one of the two halves is always sorted.
# Determine which half is sorted, then check if target falls in that half.
# Eliminate the other half.
#
# Time: O(log n)  Space: O(1)

from typing import List

def search(nums: List[int], target: int) -> int:
    lo, hi = 0, len(nums) - 1

    while lo <= hi:
        mid = (lo + hi) // 2
        if nums[mid] == target:
            return mid

        # Left half is sorted
        if nums[lo] <= nums[mid]:
            if nums[lo] <= target < nums[mid]:
                hi = mid - 1
            else:
                lo = mid + 1
        # Right half is sorted
        else:
            if nums[mid] < target <= nums[hi]:
                lo = mid + 1
            else:
                hi = mid - 1

    return -1


# ---- Tests ----
if __name__ == "__main__":
    assert search([4,5,6,7,0,1,2], 0) == 4
    assert search([4,5,6,7,0,1,2], 3) == -1
    assert search([1], 0) == -1
    assert search([1], 1) == 0
    print("All tests passed.")
