# Pattern: Arrays & Strings

## Key Techniques
- **HashMap / HashSet** — O(1) lookups for frequency counts, seen elements
- **Map to a *set*, not a counter** — when the metric is *distinct* somethings, a counter is unfixable after the fact; the dedup must happen at ingest
- **Size-K heap with an inverted comparator** — a min-heap of the K best must order by *worst first*, so every tie-break rule flips
- **Derived predicate, emit on its edges** — when output is driven by several independent bits of state, name the predicate they combine into and report only when it *flips*; every "when does this fire?" case collapses into one edge check
- **Two Pointers** — left/right pointers moving toward each other
- **Sliding Window** — variable or fixed window that expands/shrinks
- **Sorting** — enables grouping, binary search, or greedy approaches

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Two Sum | #1 | Easy | HashMap |
| Best Time to Buy and Sell Stock | #121 | Easy | Single pass / min tracker |
| Reverse Alphanumeric Segments | #917 (variant) | Easy | Two pointers scoped to each maximal run |
| Group Anagrams | #49 | Medium | HashMap + sort key |
| Longest Substring Without Repeating Characters | #3 | Medium | Sliding window |
| Filter System with Dynamic Blacklist | design classic | Easy (Medium follow-up) | Two sets; emit on the edges of `seen and not blocked` |
| Top-K Hashtags, Deduped Per User | #692 (variant) | Medium | `term -> set<user>` + size-K heap with an inverted comparator |
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

# Segment-scoped two pointers: transform each maximal run, separators are FIXED.
# The scoping is the whole point -- confining the swap to [i, j) is what keeps
# separators at their original index, no post-check needed.
i = 0
while i < n:
    if not is_member(s[i]):
        i += 1                      # fixed point, scan past it
    else:
        j = i
        while j < n and is_member(s[j]): j += 1   # [i, j) is the run
        reverse(s, i, j - 1)                      # or any in-run transform
        i = j                                     # resume past the run

# Walls vs holes -- the distinction that decides the answer:
#   walls (segment-scoped): "ab-cd" -> "ba-dc"   nothing crosses a separator
#   holes (LeetCode 917):   "ab-cd" -> "dc-ba"   members reverse as ONE global list
# For holes, run left/right over the whole string and skip non-members instead.

# Emit on the EDGES of a derived predicate (dynamic blacklist / feed filter).
# Two independent bits per key, one predicate they combine into:
#     seen(v)     v has arrived at least once      (one-way)
#     blocked(v)  v is filtered right now          (toggles)
#     visible(v) = seen(v) and not blocked(v)
# Then EVERY mutator is the same three lines, and the case analysis vanishes:
was = visible(v)
flip_one_bit()
if visible(v) != was:
    emit(not was, v)                 # `not was` == the new state
# Two traps worth saying out loud:
#   - the flag inverts. Adding to the filter (True) makes the value INVISIBLE,
#     so it emits False. Passing update_flag straight through to emit is wrong.
#   - "seen" means EVER ARRIVED, not "was emitted". A value swallowed while
#     filtered must still be remembered, or un-filtering it emits nothing.
# Test it with the invariant, not with fixtures: for every value, the LAST emit
# about it == visible(v), asserted after every single operation.

# Top-K by DISTINCT users (top_k_hashtags.py). Two independent traps.
# 1. The metric is distinct, so the counter template is wrong and unrecoverable:
users[tag].add(user_id)          # right -- popularity == len(users[tag])
counts[tag] += 1                 # wrong -- one user spamming inflates the tag
# 2. A size-K MIN-heap holds the K best, so its root is the one to EVICT, and
#    every half of the tie-break inverts. `(count, tag)` looks right and is not:
#    on ties it evicts the smallest tag, which is the one to keep.
#        best  = highest count, then SMALLEST tag
#        worst = lowest  count, then LARGEST  tag   <- what the heap must order by
#    Only the count inverts with a minus sign; strings need a real comparator.
for tag, count in counts.items():
    if len(heap) < k:      heapq.heappush(heap, Worst(count, tag))
    elif heap[0] < entry:  heapq.heapreplace(heap, Worst(count, tag))   # beats the worst kept
# Then sort the K survivors -- a heap is only partially ordered.
# Complexity is part of the answer: O(T log K) + O(K log K), vs O(T log T) to just
# sort. The heap only wins for K << T. Counting-sort the counts into buckets for
# O(T + U) when counts are small. Never claim O(T) for the heap.
# Watch `ranked[:k]` with k <= 0 -- a negative k slices from the END.
# Scale-out: swap set -> HyperLogLog (fixed bytes/term, merges by max across
# shards). Then cache the count, because estimating is O(registers), not O(1).
```
