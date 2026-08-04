# LeetCode 98 - Validate Binary Search Tree
# Difficulty: Medium
# Pattern: DFS with min/max bounds
#
# Problem: Given the root of a binary tree, determine if it is a valid BST.
#
# Approach: Pass down valid range [min_val, max_val] at each node.
# Each node must satisfy min_val < node.val < max_val.
# Left child tightens max, right child tightens min.
#
# Time: O(n)  Space: O(h) where h = tree height

from typing import Optional

class TreeNode:
    def __init__(self, val=0, left=None, right=None):
        self.val = val
        self.left = left
        self.right = right

def is_valid_bst(root: Optional[TreeNode]) -> bool:
    def validate(node, min_val, max_val):
        if not node:
            return True
        if not (min_val < node.val < max_val):
            return False
        return (validate(node.left, min_val, node.val) and
                validate(node.right, node.val, max_val))

    return validate(root, float('-inf'), float('inf'))


# ---- Tests ----
if __name__ == "__main__":
    # Valid BST:   2
    #            / \
    #           1   3
    root = TreeNode(2, TreeNode(1), TreeNode(3))
    assert is_valid_bst(root) == True

    # Invalid:   5
    #           / \
    #          1   4
    #             / \
    #            3   6
    root2 = TreeNode(5, TreeNode(1), TreeNode(4, TreeNode(3), TreeNode(6)))
    assert is_valid_bst(root2) == False
    print("All tests passed.")
