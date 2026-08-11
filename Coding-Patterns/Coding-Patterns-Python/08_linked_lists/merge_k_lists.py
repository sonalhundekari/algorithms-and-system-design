# LeetCode 23 - Merge k Sorted Lists
# Difficulty: Hard
# Pattern: Min-heap over list heads / divide-and-conquer pairwise merge
#
# Problem: Given k sorted linked lists, merge them into one sorted list.
#
# Key insight: the answer's next node is always the smallest of the k current
# heads. Both approaches below exploit that, they just differ in how they keep
# track of "smallest":
#
#   merge_k_lists          - heap of the k live heads.      O(N log k) / O(k)
#   merge_k_lists_divide   - merge lists pairwise in rounds. O(N log k) / O(log k)
#
# The naive alternative - merge list 1 into the accumulator, then list 2, ... -
# is O(N * k): the accumulator gets rescanned on every merge.
#
# Both reuse the input nodes rather than allocating new ones, so the input
# lists are consumed (their `next` pointers get rewired).

import heapq
from typing import List, Optional

class ListNode:
    def __init__(self, val=0, next=None):
        self.val = val
        self.next = next


def merge_k_lists(lists: List[Optional[ListNode]]) -> Optional[ListNode]:
    """Min-heap of the k live heads. Time: O(N log k)  Space: O(k)"""
    # ListNode is not comparable, and equal values would make heapq fall through
    # to comparing nodes. The monotonic `order` breaks every tie first.
    heap = []
    order = 0
    for node in lists:
        if node:                      # skip the empty lists the constraints allow
            heapq.heappush(heap, (node.val, order, node))
            order += 1

    dummy = ListNode(0)
    tail = dummy

    while heap:
        _, _, node = heapq.heappop(heap)
        tail.next = node
        tail = node

        if node.next:
            heapq.heappush(heap, (node.next.val, order, node.next))
            order += 1

    tail.next = None                  # cut the tail loose from its old list
    return dummy.next


def merge_k_lists_divide(lists: List[Optional[ListNode]]) -> Optional[ListNode]:
    """Pairwise merge, halving the list count each round.

    log k rounds, each touching all N nodes.
    Time: O(N log k)  Space: O(log k) recursion
    """
    if not lists:
        return None
    return _merge_range(lists, 0, len(lists) - 1)


def _merge_range(lists, left, right):
    if left == right:
        return lists[left]

    mid = (left + right) // 2
    return merge_two(_merge_range(lists, left, mid),
                     _merge_range(lists, mid + 1, right))


def merge_two(a: Optional[ListNode], b: Optional[ListNode]) -> Optional[ListNode]:
    """Standard two-pointer merge (see merge_two_sorted_lists.py)."""
    dummy = ListNode(0)
    curr = dummy

    while a and b:
        if a.val <= b.val:
            curr.next = a
            a = a.next
        else:
            curr.next = b
            b = b.next
        curr = curr.next

    curr.next = a or b
    return dummy.next


# ---- Helpers ----
def to_list(node):
    result = []
    while node:
        result.append(node.val)
        node = node.next
    return result

def from_list(vals):
    dummy = ListNode(0)
    curr = dummy
    for v in vals:
        curr.next = ListNode(v)
        curr = curr.next
    return dummy.next


# ---- Tests ----
if __name__ == "__main__":
    cases = [
        ([[1, 4, 5], [1, 3, 4], [2, 6]], [1, 1, 2, 3, 4, 4, 5, 6]),
        ([], []),                              # k == 0
        ([[]], []),                            # one empty list
        ([[], [], []], []),                    # all empty
        ([[], [1], []], [1]),                  # empties interleaved with content
        ([[1, 2, 3]], [1, 2, 3]),              # k == 1
        ([[2, 2], [2], [2, 2, 2]], [2] * 6),   # all-duplicate values (tie-break)
        ([[-10000, 0], [-5, 10000]], [-10000, -5, 0, 10000]),
    ]

    for lists, expected in cases:
        # Each solver consumes its input, so build the nodes fresh per solver.
        assert to_list(merge_k_lists([from_list(v) for v in lists])) == expected
        assert to_list(merge_k_lists_divide([from_list(v) for v in lists])) == expected

    # k at the constraint edge: 10^4 lists, 10^4 nodes total.
    wide = [from_list([i]) for i in range(10_000)]
    assert to_list(merge_k_lists(wide)) == list(range(10_000))

    print("All tests passed.")
