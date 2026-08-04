# LeetCode 297 - Serialize and Deserialize Binary Tree
# Difficulty: Hard
# Pattern: BFS encoding
#
# Problem: Design an algorithm to serialize a binary tree to a string
# and deserialize that string back to the tree.
#
# Approach: BFS level-order. Use "null" for missing children.
# Serialize: encode each node value, "null" for None.
# Deserialize: rebuild using a queue of parent nodes.
#
# Time: O(n)  Space: O(n)

from collections import deque
from typing import Optional

class TreeNode:
    def __init__(self, val=0, left=None, right=None):
        self.val = val
        self.left = left
        self.right = right

class Codec:
    def serialize(self, root: Optional[TreeNode]) -> str:
        if not root:
            return "[]"
        result = []
        queue = deque([root])
        while queue:
            node = queue.popleft()
            if node:
                result.append(str(node.val))
                queue.append(node.left)
                queue.append(node.right)
            else:
                result.append("null")
        return "[" + ",".join(result) + "]"

    def deserialize(self, data: str) -> Optional[TreeNode]:
        data = data.strip("[]")
        if not data:
            return None
        vals = data.split(",")
        root = TreeNode(int(vals[0]))
        queue = deque([root])
        i = 1
        while queue and i < len(vals):
            node = queue.popleft()
            if vals[i] != "null":
                node.left = TreeNode(int(vals[i]))
                queue.append(node.left)
            i += 1
            if i < len(vals) and vals[i] != "null":
                node.right = TreeNode(int(vals[i]))
                queue.append(node.right)
            i += 1
        return root


# ---- Tests ----
if __name__ == "__main__":
    codec = Codec()
    root = TreeNode(1, TreeNode(2), TreeNode(3, TreeNode(4), TreeNode(5)))
    serialized = codec.serialize(root)
    deserialized = codec.deserialize(serialized)
    assert codec.serialize(deserialized) == serialized
    print(f"Serialized: {serialized}")
    print("All tests passed.")
