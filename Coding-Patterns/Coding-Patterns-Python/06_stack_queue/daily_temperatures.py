# LeetCode 739 - Daily Temperatures
# Difficulty: Medium
# Pattern: Monotonic stack (decreasing)
#
# Problem: Given an array temperatures, return an array answer where
# answer[i] is the number of days until a warmer temperature.
# If no future warmer day exists, answer[i] = 0.
#
# Approach: Monotonic decreasing stack of indices.
# When current temp > stack top temp, pop and record the gap.
#
# Time: O(n)  Space: O(n)

from typing import List

def daily_temperatures(temperatures: List[int]) -> List[int]:
    n = len(temperatures)
    answer = [0] * n
    stack = []  # stack of indices, decreasing temperatures

    for i, temp in enumerate(temperatures):
        while stack and temperatures[stack[-1]] < temp:
            idx = stack.pop()
            answer[idx] = i - idx
        stack.append(i)

    return answer


# ---- Tests ----
if __name__ == "__main__":
    assert daily_temperatures([73,74,75,71,69,72,76,73]) == [1,1,4,2,1,1,0,0]
    assert daily_temperatures([30,40,50,60]) == [1,1,1,0]
    assert daily_temperatures([30,60,90]) == [1,1,0]
    print("All tests passed.")
