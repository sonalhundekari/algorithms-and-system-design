# LeetCode 1 - Two Sum
# Difficulty: Easy
# Pattern: HashMap
#
# Problem: Given an array of integers nums and an integer target,
# return indices of the two numbers that add up to target.
#
# Approach: For each number, check if (target - num) is already in
# the hashmap. If yes, we found our pair. If no, store num -> index.
#
# Time: O(n)  Space: O(n)

from typing import List

def two_sum(nums: List[int], target: int) -> List[int]:
    seen = {}  # value -> index
    for i, num in enumerate(nums):
        complement = target - num
        if complement in seen:
            return [seen[complement], i]
        seen[num] = i
    return []


# ---- Tests ----
if __name__ == "__main__":
    assert two_sum([2, 7, 11, 15], 9) == [0, 1]
    assert two_sum([3, 2, 4], 6) == [1, 2]
    assert two_sum([3, 3], 6) == [0, 1]
    print("All tests passed.")
