# LeetCode 102 - Binary Tree Level Order Traversal
# Difficulty: Medium
# Pattern: BFS with queue
#
# Problem: Return the level-order traversal of a binary tree's node values
# (i.e., left to right, level by level).
#
# Approach: BFS using a deque. At each level, process exactly len(queue)
# nodes (snapshot of current level size before adding children).
#
# Time: O(n)  Space: O(n)

from typing import Optional, List
from collections import deque

class TreeNode:
    def __init__(self, val=0, left=None, right=None):
        self.val = val
        self.left = left
        self.right = right

def level_order(root: Optional[TreeNode]) -> List[List[int]]:
    if not root:
        return []
    result = []
    queue = deque([root])
    while queue:
        level_size = len(queue)
        level = []
        for _ in range(level_size):
            node = queue.popleft()
            level.append(node.val)
            if node.left:  queue.append(node.left)
            if node.right: queue.append(node.right)
        result.append(level)
    return result


# ---- Tests ----
if __name__ == "__main__":
    #     3
    #    / \
    #   9  20
    #     /  \
    #    15   7
    root = TreeNode(3, TreeNode(9), TreeNode(20, TreeNode(15), TreeNode(7)))
    assert level_order(root) == [[3], [9, 20], [15, 7]]
    assert level_order(None) == []
    print("All tests passed.")
