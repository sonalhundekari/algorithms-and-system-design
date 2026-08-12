# Pattern: Stack & Queue

## Key Techniques
- **Monotonic stack** — maintain increasing or decreasing order; pop when invariant breaks
- **Stack for matching** — push open brackets/elements, pop and verify on closing
- **Stack of frames** — one entry per open bracket holds the state to restore when it closes (nested parsing/decoding)
- **Min-tracking stack** — pair each element with the current min at that depth
- **Keep the boundary, not the order** — an order statistic (median, k-th largest) depends on one position, so maintain the *split* around it with two heaps instead of a sorted list. A heap gives you one end cheaply and refuses to tell you anything else; that refusal is what makes insert O(log n)
- **Push through the far side** — to add across a two-heap split, route every value in through one heap and out of it into the other, then rebalance. The ordering invariant then holds by construction (what leaves a heap *is* its extreme), so there is no comparison to invert and no empty-heap branch

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Valid Parentheses | #20 | Easy | Stack matching |
| Daily Temperatures | #739 | Medium | Monotonic stack (decreasing) |
| Min Stack | #155 | Medium | Stack of (val, current_min) pairs |
| Largest Rectangle in Histogram | #84 | Hard | Monotonic stack (increasing) |
| Decode String | #394 | Medium | Stack of pending frames |
| Find Median from Data Stream | #295 | Hard | Two heaps split at the median |

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

# Median of a stream: don't keep the ORDER, keep the SPLIT. Two heaps put both
# candidate middles at a root, so findMedian is O(1) and addNum is one sift.
#
#   lo = max-heap of the smaller half     hi = min-heap of the larger half
#   invariants:  max(lo) <= min(hi)  and  len(lo) - len(hi) in {0, 1}
#
# Route every value THROUGH lo and out into hi: what leaves lo is lo's maximum,
# so it is >= everything left behind and the ordering invariant cannot break.
# No "is num <= lo[0]?" to get backwards, no empty-heap special case.
lo, hi = [], []                                   # lo is NEGATED; heapq is min-only

def add_num(num):
    heapq.heappush(lo, -num)
    heapq.heappush(hi, -heapq.heappop(lo))
    if len(hi) > len(lo):                         # lo holds the odd one out
        heapq.heappush(lo, -heapq.heappop(hi))

def find_median():
    return -lo[0] if len(lo) > len(hi) else (-lo[0] + hi[0]) / 2.0

# Three follow-ups, each of which changes the structure:
#   "values are in [0, 100]"  -> counts[101]; add is O(1), median is a 101-bucket
#                                scan. A Fenwick tree generalises it: O(log U),
#                                memory O(U) INDEPENDENT of n, removal + percentiles
#   "support removeNum"       -> heaps can't delete an interior element. Lazy
#                                deletion (delayed counts + separate LOGICAL sizes,
#                                pruned at the root) or the Fenwick tree
#   "a billion numbers"       -> exact medians need O(n): unlike a mean or a max,
#                                any value can still turn out to be the middle one.
#                                Bound the domain, or approximate (t-digest, P^2)
```
