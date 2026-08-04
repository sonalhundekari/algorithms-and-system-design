# Pattern: Binary Search

## Key Techniques
- **Classic binary search** — find target in sorted array, O(log n)
- **Binary search on answer space** — search over possible answer values, not array indices
- **Rotated array** — one half is always sorted; determine which half and eliminate

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Search in Rotated Sorted Array | #33 | Medium | Binary search with rotation check |
| Find Minimum in Rotated Sorted Array | #153 | Medium | Binary search on rotation pivot |
| Koko Eating Bananas | #875 | Medium | Binary search on answer space |

## Pattern Cheat Sheet

```python
# Classic binary search
lo, hi = 0, len(nums) - 1
while lo <= hi:
    mid = (lo + hi) // 2
    if nums[mid] == target: return mid
    elif nums[mid] < target: lo = mid + 1
    else: hi = mid - 1

# Binary search on answer space
lo, hi = min_possible, max_possible
while lo < hi:
    mid = (lo + hi) // 2
    if feasible(mid):
        hi = mid        # mid could be the answer, search left
    else:
        lo = mid + 1    # mid is too small, search right
return lo
```
