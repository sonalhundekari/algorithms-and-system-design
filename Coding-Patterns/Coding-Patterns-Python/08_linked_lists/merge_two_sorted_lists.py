# LeetCode 21 - Merge Two Sorted Lists
# Difficulty: Easy
# Pattern: Dummy head + two pointers
#
# Problem: Merge two sorted linked lists and return the merged list.
#
# Approach: Use a dummy head to simplify building the result list.
# Advance the pointer that points to the smaller value.
# Attach the remaining non-null list at the end.
#
# Time: O(m + n)  Space: O(1)

from typing import Optional

class ListNode:
    def __init__(self, val=0, next=None):
        self.val = val
        self.next = next

def merge_two_lists(list1: Optional[ListNode], list2: Optional[ListNode]) -> Optional[ListNode]:
    dummy = ListNode(0)
    curr = dummy

    while list1 and list2:
        if list1.val <= list2.val:
            curr.next = list1
            list1 = list1.next
        else:
            curr.next = list2
            list2 = list2.next
        curr = curr.next

    curr.next = list1 or list2  # attach remainder
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
    l1 = from_list([1, 2, 4])
    l2 = from_list([1, 3, 4])
    assert to_list(merge_two_lists(l1, l2)) == [1, 1, 2, 3, 4, 4]
    assert to_list(merge_two_lists(None, None)) == []
    assert to_list(merge_two_lists(None, from_list([0]))) == [0]
    print("All tests passed.")
