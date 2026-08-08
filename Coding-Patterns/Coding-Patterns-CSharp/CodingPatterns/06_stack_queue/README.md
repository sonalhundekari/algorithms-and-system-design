# Pattern: Stack & Queue

## Key Techniques
- **Monotonic stack** — maintain increasing or decreasing order; pop when invariant breaks
- **Stack for matching** — push open brackets/elements, pop and verify on closing
- **Stack of frames** — one entry per open bracket holds the state to restore when it closes (nested parsing/decoding)
- **Min-tracking stack** — pair each element with the current min at that depth

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Valid Parentheses | #20 | Easy | Stack matching |
| Daily Temperatures | #739 | Medium | Monotonic stack (decreasing) |
| Min Stack | #155 | Medium | Stack of (val, current_min) pairs |
| Largest Rectangle in Histogram | #84 | Hard | Monotonic stack (increasing) |
| Decode String | #394 | Medium | Stack of pending frames |

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

# Nested decoding: one stack frame per open bracket
counts, parents, current = [], [], []
for ch in s:
    if ch.isdigit():
        k = k * 10 + int(ch)      # multi-digit counts
    elif ch == '[':               # descend: park the parent state
        counts.append(k); parents.append(current)
        k, current = 0, []
    elif ch == ']':               # ascend: fold the finished segment in
        segment = current
        current = parents.pop()
        current.append(''.join(segment) * counts.pop())
    else:
        current.append(ch)
```
