# Pattern: Trees

## Key Techniques
- **DFS (recursive/iterative)** — preorder, inorder, postorder traversal
- **BFS (level-order)** — use a queue; process nodes level by level
- **BST properties** — left < node < right; inorder traversal gives sorted output
- **Divide and conquer** — solve left subtree, right subtree, combine

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Validate BST | #98 | Medium | DFS with min/max bounds |
| Lowest Common Ancestor | #236 | Medium | DFS post-order |
| Binary Tree Level Order Traversal | #102 | Medium | BFS with queue |
| Serialize / Deserialize Binary Tree | #297 | Hard | BFS or DFS + string encoding |

## Pattern Cheat Sheet

```python
# DFS template (recursive)
def dfs(node):
    if not node:
        return base_case
    left = dfs(node.left)
    right = dfs(node.right)
    return combine(left, right, node.val)

# BFS template
from collections import deque
queue = deque([root])
while queue:
    node = queue.popleft()
    # process node
    if node.left: queue.append(node.left)
    if node.right: queue.append(node.right)
```
