# LeetCode 84 - Largest Rectangle in Histogram
# Difficulty: Hard
# Pattern: Monotonic stack (increasing)
#
# Problem: Given an array of bar heights, find the area of the
# largest rectangle that can be formed within the histogram.
#
# Approach: Monotonic increasing stack of indices.
# When we find a bar shorter than the stack top, that top bar can no
# longer extend right — compute its max rectangle (height * width).
# Width = current_index - stack_new_top - 1 (span between boundaries).
# Append sentinel 0 at end to flush remaining stack elements.
#
# Time: O(n)  Space: O(n)

from typing import List

def largest_rectangle_area(heights: List[int]) -> int:
    stack = []  # indices, increasing heights
    max_area = 0
    heights = heights + [0]  # sentinel to flush stack

    for i, h in enumerate(heights):
        while stack and heights[stack[-1]] > h:
            height = heights[stack.pop()]
            width = i if not stack else i - stack[-1] - 1
            max_area = max(max_area, height * width)
        stack.append(i)

    return max_area


# ---- Tests ----
if __name__ == "__main__":
    assert largest_rectangle_area([2,1,5,6,2,3]) == 10   # bars 5,6 -> 2*5=10
    assert largest_rectangle_area([2,4]) == 4
    assert largest_rectangle_area([1]) == 1
    assert largest_rectangle_area([0]) == 0
    print("All tests passed.")
