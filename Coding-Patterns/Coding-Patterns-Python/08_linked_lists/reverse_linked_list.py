# LeetCode 206 - Reverse Linked List
# Difficulty: Easy
# Pattern: Iterative reversal (prev/curr/next dance)
#
# Problem: Given the head of a singly linked list, reverse it and return it.
#
# Approach (iterative): Walk forward, flipping each .next pointer to point
# to the previous node. Track prev, curr, and save next before overwriting.
#
# Time: O(n)  Space: O(1)

from typing import Optional

class ListNode:
    def __init__(self, val=0, next=None):
        self.val = val
        self.next = next

def reverse_list(head: Optional[ListNode]) -> Optional[ListNode]:
    prev, curr = None, head
    while curr:
        next_node = curr.next
        curr.next = prev
        prev = curr
        curr = next_node
    return prev  # new head


# Recursive version (bonus)
def reverse_list_recursive(head: Optional[ListNode]) -> Optional[ListNode]:
    if not head or not head.next:
        return head
    new_head = reverse_list_recursive(head.next)
    head.next.next = head
    head.next = None
    return new_head


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
    assert to_list(reverse_list(from_list([1,2,3,4,5]))) == [5,4,3,2,1]
    assert to_list(reverse_list(from_list([1,2]))) == [2,1]
    assert to_list(reverse_list(None)) == []
    print("All tests passed.")
