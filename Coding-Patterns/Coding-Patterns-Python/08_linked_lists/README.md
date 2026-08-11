# Pattern: Linked Lists

## Key Techniques
- **Two pointers (fast/slow)** — cycle detection, find middle, kth from end
- **Dummy head node** — simplifies edge cases when head itself may change
- **Iterative reversal** — prev, curr, next pointer dance
- **HashMap for O(1) access** — LRU Cache, clone with random pointer
- **Min-heap over k heads** — k-way merge in O(N log k) instead of O(N·k)

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Merge Two Sorted Lists | #21 | Easy | Dummy head + two pointers |
| Merge k Sorted Lists | #23 | Hard | Min-heap of heads / divide & conquer |
| Reverse Linked List | #206 | Easy | Iterative / recursive reversal |
| LRU Cache | #146 | Medium | HashMap + doubly linked list |
| Happy Number | #202 | Easy | Floyd's cycle detection on an implicit list |

## Pattern Cheat Sheet

```python
# Iterative reversal
prev, curr = None, head
while curr:
    next_node = curr.next
    curr.next = prev
    prev = curr
    curr = next_node
return prev  # new head

# Floyd's cycle detection — works on any "each node has one successor"
# sequence, even when there is no real list (see Happy Number)
slow, fast = start, step(start)
while fast != target and slow != fast:
    slow = step(slow)
    fast = step(step(fast))
return fast == target  # target reached, or the pointers met inside a cycle

# Dummy head pattern (avoids special-casing the head)
dummy = ListNode(0)
dummy.next = head
curr = dummy
# ... manipulate ...
return dummy.next

# k-way merge: heap holds one node per list, never all N
# (order breaks ties so heapq never compares the nodes themselves)
heap = [(n.val, i, n) for i, n in enumerate(lists) if n]
heapq.heapify(heap)
while heap:
    _, i, node = heapq.heappop(heap)
    if node.next:
        heapq.heappush(heap, (node.next.val, i, node.next))
```
