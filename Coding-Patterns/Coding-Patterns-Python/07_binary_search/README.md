# Pattern: Binary Search

## Key Techniques
- **Classic binary search** — find target in sorted array, O(log n)
- **Binary search on answer space** — search over possible answer values, not array indices
- **Rotated array** — one half is always sorted; determine which half and eliminate
- **Binary lifting over a Fenwick tree** — when the searchable thing is a *count*
  rather than an array: index the value domain, and descend the tree's own power-of-two
  node layout to turn a rank back into a value
- **Two bounds, one subtraction** — a range *count* over sorted data is
  `upper_bound(end) - lower_bound(start)`. Pairing *left* on the start with *right* on
  the end is what makes both endpoints inclusive; any other pairing is silently wrong
  only when timestamps tie

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Search in Rotated Sorted Array | #33 | Medium | Binary search with rotation check |
| Find Minimum in Rotated Sorted Array | #153 | Medium | Binary search on rotation pivot |
| Koko Eating Bananas | #875 | Medium | Binary search on answer space |
| Leaderboard | #1244† | Hard | Fenwick tree over the score domain + binary lifting |
| Event Stream Count in Time Range | #981‡ | Medium | Per-type sorted list + `lower_bound`/`upper_bound` |

† A superset of LeetCode #1244, which only asks for `top(K)`. This version adds
rank of an **arbitrary** player, "players near me" windows, and score-range counts —
the questions a real leaderboard actually gets asked.

‡ The data-structure half of LeetCode #981 (Time Based Key-Value Store): same per-key
sorted list and same binary search, reduced to *counting* the window instead of
retrieving the value at its right edge.

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

## Design note: range counts over a stream

The monotonic-timestamp guarantee is the whole interview. It means the per-type
list arrives sorted, so a write is an append and no ordered structure is needed —
resist reaching for a tree before asking whether inserts can ever go backwards.

| Guarantee | Write | Range count | Structure |
|---|---|---|---|
| Timestamps non-decreasing | O(1) | O(log n) | Sorted list + two binary searches |
| Bounded lateness | O(1) amortized | O(log n) | Reorder buffer flushed past a watermark, then as above |
| Arbitrary order | O(log n) | O(log n) | Order-statistic tree, or Fenwick over compressed timestamps |
| Unbounded stream, approximation OK | O(1) | O(1) | Fixed time buckets + prefix sums — memory tracks *time*, not events |

Note that a plain `TreeMap` / `SortedSet` is **not** the answer to the arbitrary-order
follow-up: it finds the window's two edges but cannot report how many nodes lie
between them without walking. The subtree counts have to be maintained.

## Pattern Cheat Sheet

```python
# Inclusive range count on a sorted list: the two bounds are the whole problem.
# lower_bound == bisect_left (first >= target), upper_bound == bisect_right (first > target).
def lower_bound(a, target):     # hi starts at len(a): "no such index" is a legal answer
    lo, hi = 0, len(a)
    while lo < hi:
        mid = (lo + hi) // 2
        if a[mid] < target: lo = mid + 1    # too early, cannot be the answer
        else:               hi = mid        # qualifies, but look left for an earlier one
    return lo

def upper_bound(a, target):     # identical but for `<=`, which walks past a run of ties
    lo, hi = 0, len(a)
    while lo < hi:
        mid = (lo + hi) // 2
        if a[mid] <= target: lo = mid + 1
        else:                hi = mid
    return lo

count_in(a, start, end) == upper_bound(a, end) - lower_bound(a, start)   # both ends inclusive

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
