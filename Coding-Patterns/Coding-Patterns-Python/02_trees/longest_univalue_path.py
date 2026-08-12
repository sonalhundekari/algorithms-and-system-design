# LeetCode 687 - Longest Univalue Path
# Difficulty: Medium
# Pattern: Post-order DFS, "arrow down" vs "path bending at this node"
#
# Problem: return the number of EDGES on the longest path whose nodes all share
# one value. The path may start and end anywhere; it need not touch the root.
#
# The whole family (#543 diameter, #124 max path sum, this one) rests on one
# split. Every path has a single highest node — the point where it bends — so
# ask two different questions and never confuse them:
#
#   answer at a node = left arrow + right arrow   (the bend; both sides count)
#   value returned   = max(left, right) + ...     (one arrow, extendable upward)
#
# A parent can only extend ONE branch, so the return value picks a side; the
# global best records the bend. Because the recursion visits every node, every
# possible bend point is considered exactly once.
#
#     5         node 4:  neither child is a 4  -> arrows 0, 0; bend 0; returns 0
#    / \        node 5 (right): right child is a 5 -> arrows 0, 1; bend 1; returns 1
#   4   5       root 5:  left child 4 differs   -> left arrow 0
#  / \    \              right child 5 matches  -> right arrow = 1 + 1 = 2
# 1   1    5             bend 0 + 2 = 2  <- the answer, the 5-5-5 spine
#
# Traps:
#   - Edges, not nodes. A single node scores 0, not 1.
#   - Recurse into BOTH children unconditionally, then compare values. Guarding
#     the recursion behind `child.val == node.val` skips whole subtrees, and the
#     longest path is often inside a subtree that disagrees with its parent
#     ([1,4,5,4,4,null,5]: the answer 4-4-4 hangs under a root valued 1).
#   - The child's arrow is only usable when the EDGE matches, so the +1 and the
#     value check belong together: `child.val == node.val ? arrow(child) + 1 : 0`.
#   - Null children contribute 0, which falls out of the same expression.
#
# Time: O(n)   Space: O(h) — O(n) on a degenerate skewed tree.
#
# Depth note: n can be 10^4, so a skewed input overflows CPython's default
# recursion limit (1000). The iterative post-order version below is the safe
# one; it is the same fold with an explicit stack and a memo of arrow lengths.

from typing import Optional


class TreeNode:
    def __init__(self, val=0, left=None, right=None):
        self.val = val
        self.left = left
        self.right = right


# ---- Approach 1: recursive post-order ----
def longest_univalue_path(root: Optional[TreeNode]) -> int:
    best = 0

    def arrow(node: Optional[TreeNode]) -> int:
        """Longest univalue path in EDGES that starts at `node` and goes down."""
        nonlocal best
        if not node:
            return 0

        # Recurse first, unconditionally — the answer may live in a subtree
        # whose values have nothing to do with this node.
        left_down = arrow(node.left)
        right_down = arrow(node.right)

        # An arrow only crosses the edge when the child agrees with us.
        left = left_down + 1 if node.left and node.left.val == node.val else 0
        right = right_down + 1 if node.right and node.right.val == node.val else 0

        best = max(best, left + right)  # the path that bends here
        return max(left, right)         # what the parent can extend

    arrow(root)
    return best


# ---- Approach 2: iterative post-order, O(h) stack instead of recursion ----
# Same fold, no call stack: each node is pushed twice — once to expand its
# children, once (after they are done) to combine. `down` memoises each node's
# arrow length, keyed by identity so equal values never collide.
def longest_univalue_path_iterative(root: Optional[TreeNode]) -> int:
    best = 0
    down: dict[int, int] = {}
    stack: list[tuple[Optional[TreeNode], bool]] = [(root, False)]

    while stack:
        node, expanded = stack.pop()
        if not node:
            continue

        if not expanded:
            stack.append((node, True))       # revisit after both children
            stack.append((node.left, False))
            stack.append((node.right, False))
            continue

        left = down.get(id(node.left), 0) + 1 \
            if node.left and node.left.val == node.val else 0
        right = down.get(id(node.right), 0) + 1 \
            if node.right and node.right.val == node.val else 0

        best = max(best, left + right)
        down[id(node)] = max(left, right)

    return best


# ---- Tests ----
def build(values) -> Optional[TreeNode]:
    """Build a tree from a LeetCode level-order list (None marks a missing child)."""
    if not values or values[0] is None:
        return None
    root = TreeNode(values[0])
    queue = [root]
    i = 1
    while queue and i < len(values):
        node = queue.pop(0)
        if i < len(values):
            if values[i] is not None:
                node.left = TreeNode(values[i])
                queue.append(node.left)
            i += 1
        if i < len(values):
            if values[i] is not None:
                node.right = TreeNode(values[i])
                queue.append(node.right)
            i += 1
    return root


if __name__ == "__main__":
    cases = [
        ("LC example 1", [5, 4, 5, 1, 1, None, 5], 2),   # 5 -> 5 -> 5 on the right
        ("LC example 2", [1, 4, 5, 4, 4, None, 5], 2),   # 4 -> 4 -> 4 under a root of 1
        ("empty", [], 0),
        ("single node", [1], 0),                          # edges, not nodes
        ("two equal", [1, 1], 1),
        ("two different", [1, 2], 0),
        ("all distinct", [1, 2, 3, 4, 5, 6, 7], 0),
        ("all equal, full", [7, 7, 7, 7, 7, 7, 7], 4),    # leaf -> root -> leaf
        ("bend at a child", [1, 2, 2, 2, 2], 2),          # 2 -> 2 -> 2 through the left child
        ("answer hidden below", [9, 9, 1, 1, 1, 1, 1], 2),  # the 1-1-1 bend under the right child
        ("negative values", [-1, -1, -1], 2),
    ]

    for label, values, expected in cases:
        tree = build(values)
        got = longest_univalue_path(tree)
        got_it = longest_univalue_path_iterative(build(values))
        ok = got == expected and got_it == expected
        print(f"{'PASS' if ok else 'FAIL'}  {label:<20} {got}"
              + ("" if ok else f"  expected {expected}  iterative {got_it}"))
        assert ok

    # Depth: a 10,000-node chain of equal values. The recursive version would
    # need a raised recursion limit here; the iterative one does not care.
    chain = TreeNode(3)
    node = chain
    for _ in range(9_999):
        node.left = TreeNode(3)
        node = node.left
    assert longest_univalue_path_iterative(chain) == 9_999
    print(f"{'PASS'}  {'deep skewed chain':<20} 9999")

    print("All tests passed.")
