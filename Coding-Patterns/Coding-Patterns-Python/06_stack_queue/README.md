# Pattern: Stack & Queue

## Key Techniques
- **Monotonic stack** — maintain increasing or decreasing order; pop when invariant breaks
- **Stack for matching** — push open brackets/elements, pop and verify on closing
- **Min-tracking stack** — pair each element with the current min at that depth

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Valid Parentheses | #20 | Easy | Stack matching |
| Daily Temperatures | #739 | Medium | Monotonic stack (decreasing) |
| Min Stack | #155 | Medium | Stack of (val, current_min) pairs |
| Largest Rectangle in Histogram | #84 | Hard | Monotonic stack (increasing) |

## Pattern Cheat Sheet

```python
# Monotonic decreasing stack (next greater element)
stack = []  # stores indices
result = [-1] * len(nums)
for i, val in enumerate(nums):
    while stack and nums[stack[-1]] < val:
        idx = stack.pop()
        result[idx] = val
    stack.append(i)

# Valid parentheses template
stack = []
pairs = {')': '(', '}': '{', ']': '['}
for ch in s:
    if ch in pairs:
        if not stack or stack[-1] != pairs[ch]:
            return False
        stack.pop()
    else:
        stack.append(ch)
return not stack
```
