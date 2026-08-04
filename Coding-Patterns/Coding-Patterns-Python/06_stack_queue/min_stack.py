# LeetCode 155 - Min Stack
# Difficulty: Medium
# Pattern: Stack with auxiliary min tracking
#
# Problem: Design a stack that supports push, pop, top, and
# retrieving the minimum element — all in O(1).
#
# Approach: Store pairs (value, current_min) so each stack frame
# knows the minimum at that depth. No separate min-stack needed.
#
# Time: O(1) all ops  Space: O(n)

class MinStack:
    def __init__(self):
        self.stack = []  # stores (val, min_so_far)

    def push(self, val: int) -> None:
        current_min = min(val, self.stack[-1][1]) if self.stack else val
        self.stack.append((val, current_min))

    def pop(self) -> None:
        self.stack.pop()

    def top(self) -> int:
        return self.stack[-1][0]

    def get_min(self) -> int:
        return self.stack[-1][1]


# ---- Tests ----
if __name__ == "__main__":
    ms = MinStack()
    ms.push(-2)
    ms.push(0)
    ms.push(-3)
    assert ms.get_min() == -3
    ms.pop()
    assert ms.top() == 0
    assert ms.get_min() == -2
    print("All tests passed.")
