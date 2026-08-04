# Pattern: Linked Lists

## Key Techniques
- **Two pointers (fast/slow)** — cycle detection, find middle, kth from end
- **Dummy head node** — simplifies edge cases when head itself may change
- **Iterative reversal** — prev, curr, next pointer dance
- **HashMap for O(1) access** — LRU Cache, clone with random pointer

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Merge Two Sorted Lists | #21 | Easy | Dummy head + two pointers |
| Reverse Linked List | #206 | Easy | Iterative / recursive reversal |
| LRU Cache | #146 | Medium | HashMap + doubly linked list |

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

# Dummy head pattern (avoids special-casing the head)
dummy = ListNode(0)
dummy.next = head
curr = dummy
# ... manipulate ...
return dummy.next
```
