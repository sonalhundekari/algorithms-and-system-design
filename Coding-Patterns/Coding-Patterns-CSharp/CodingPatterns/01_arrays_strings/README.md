# Pattern: Arrays & Strings

## Key Techniques
- **HashMap / HashSet** — O(1) lookups for frequency counts, seen elements
- **Two Pointers** — left/right pointers moving toward each other
- **Sliding Window** — variable or fixed window that expands/shrinks
- **Sorting** — enables grouping, binary search, or greedy approaches

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Two Sum | #1 | Easy | HashMap |
| Best Time to Buy and Sell Stock | #121 | Easy | Single pass / min tracker |
| Group Anagrams | #49 | Medium | HashMap + sort key |
| Longest Substring Without Repeating Characters | #3 | Medium | Sliding window |
| Minimum Window Substring | #76 | Hard | Sliding window + frequency map |

## Pattern Cheat Sheet

```
# Sliding Window template
left = 0
for right in range(len(s)):
    # expand window
    while <window invalid>:
        # shrink from left
        left += 1
    # update answer
```
