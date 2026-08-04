# LeetCode 236 - Lowest Common Ancestor of a Binary Tree
# Difficulty: Medium
# Pattern: DFS post-order
#
# Problem: Given a binary tree and two nodes p and q,
# find their lowest common ancestor (LCA).
#
# Approach: Post-order DFS. If current node is p or q, return it.
# If both left and right subtrees return non-null, current node is LCA.
# Otherwise propagate the non-null result upward.
#
# Time: O(n)  Space: O(h)

from typing import Optional

class TreeNode:
    def __init__(self, val=0, left=None, right=None):
        self.val = val
        self.left = left
        self.right = right

def lowest_common_ancestor(root: TreeNode, p: TreeNode, q: TreeNode) -> TreeNode:
    if not root or root == p or root == q:
        return root

    left = lowest_common_ancestor(root.left, p, q)
    right = lowest_common_ancestor(root.right, p, q)

    if left and right:
        return root   # p and q are in different subtrees
    return left or right  # both in same subtree


# ---- Tests ----
if __name__ == "__main__":
    #        3
    #       / \
    #      5   1
    #     / \ / \
    #    6  2 0  8
    #      / \
    #     7   4
    n6, n7, n4, n0, n8 = TreeNode(6), TreeNode(7), TreeNode(4), TreeNode(0), TreeNode(8)
    n2 = TreeNode(2, n7, n4)
    n5 = TreeNode(5, n6, n2)
    n1 = TreeNode(1, n0, n8)
    root = TreeNode(3, n5, n1)

    assert lowest_common_ancestor(root, n5, n1).val == 3
    assert lowest_common_ancestor(root, n5, n4).val == 5
    print("All tests passed.")
