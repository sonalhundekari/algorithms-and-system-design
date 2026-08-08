# Pattern: Binary Search

## Key Techniques
- **Classic binary search** — find target in sorted array, O(log n)
- **Binary search on answer space** — search over possible answer values, not array indices
- **Rotated array** — one half is always sorted; determine which half and eliminate
- **Binary lifting over a Fenwick tree** — when the searchable thing is a *count*
  rather than an array: index the value domain, and descend the tree's own power-of-two
  node layout to turn a rank back into a value

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Search in Rotated Sorted Array | #33 | Medium | Binary search with rotation check |
| Find Minimum in Rotated Sorted Array | #153 | Medium | Binary search on rotation pivot |
| Koko Eating Bananas | #875 | Medium | Binary search on answer space |
| Leaderboard | #1244† | Hard | Fenwick tree over the score domain + binary lifting |

† A superset of LeetCode #1244, which only asks for `top(K)`. This version adds
rank of an **arbitrary** player, "players near me" windows, and score-range counts —
the questions a real leaderboard actually gets asked.

## Design note: which structure for a ranking?

Two clarifying answers decide it. **Do you need the rank of an arbitrary player,
and is the score domain bounded?**

| Requirements | Structure |
|---|---|
| Top-K only | Size-K min-heap + hash map — O(log K) update |
| Arbitrary rank, unbounded scores | Order-statistic tree (skip list / BST with subtree counts) — O(log P), this is a Redis `ZSET` |
| Arbitrary rank, **bounded** scores | Fenwick tree over the *score* axis — O(log MaxScore), independent of player count |
| Batch refresh acceptable | Sorted array rebuilt periodically + binary search |

The Fenwick version wins when it applies because rank never needed the players
ordered — only a *count* of players scoring higher.

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

# Fenwick tree: O(log n) point update + prefix count
def add(tree, i, delta):        # i is 1-based
    while i < len(tree):
        tree[i] += delta
        i += i & -i             # next node covering i

def prefix(tree, i):            # sum of 1..i
    total = 0
    while i > 0:
        total += tree[i]
        i -= i & -i             # drop the lowest set bit
    return total

# Binary lifting: smallest index whose prefix >= target, in O(log n)
# `pos` walks down the tree keeping prefix(pos) < target the whole way.
def find(tree, target, span):   # span = largest power of two < len(tree)
    pos = 0
    step = span
    while step > 0:
        if pos + step < len(tree) and tree[pos + step] < target:
            pos += step
            target -= tree[pos]
        step >>= 1
    return pos + 1
```
