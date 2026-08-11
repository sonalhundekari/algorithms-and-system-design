# Pattern: Trees

## Key Techniques
- **DFS (recursive/iterative)** — preorder, inorder, postorder traversal
- **BFS (level-order)** — use a queue; process nodes level by level
- **BST properties** — left < node < right; inorder traversal gives sorted output
- **Divide and conquer** — solve left subtree, right subtree, combine
- **N-ary tree keyed by name** — one walk primitive; every path API is a thin wrapper over it
- **Partitioned traversal** — when the answer is an ordered concatenation of groups, define the groups so they cannot overlap, then walk each
- **Leaf sequence as a fingerprint** — reduce a tree to the left-to-right list of its leaves; any DFS that goes left before right produces it
- **Post-order fold into a mirror tree** — when two trees share a shape, one walk can both return a value and write it into the twin; no node→node map needed

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Validate BST | #98 | Medium | DFS with min/max bounds |
| Lowest Common Ancestor | #236 | Medium | DFS post-order |
| Binary Tree Level Order Traversal | #102 | Medium | BFS with queue |
| Serialize / Deserialize Binary Tree | #297 | Hard | BFS or DFS + string encoding |
| Design In-Memory File System | #588 | Hard | N-ary tree + ordinal-sorted children; mkdir -p walk |
| Boundary of Binary Tree | #545 | Medium | Three disjoint DFS walks: left spine, leaves, reversed right spine |
| Leaf-Similar Trees | #872 | Easy | DFS leaf sequence; lockstep stacks for O(h) space |
| Rewrite Tree With Subtree Sums | Snowflake | Easy | Post-order fold over two same-shape trees; parallel fork with depth cutoff |

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

# Path-keyed n-ary tree (in-memory file system). ONE walk, four APIs.
# A node is either a file (content) or a directory (children) -- never both.
def descend(parts, count, create):
    node = root
    for name in parts[:count]:
        if node.is_file: raise NotADirectory      # "/a/f.txt/b" means nothing
        if name not in node.children:
            if not create: raise NotFound
            node.children[name] = Directory(name) # mkdir -p: create on the way down
        node = node.children[name]
    return node

# mkdir             -> descend(parts, len(parts), create=True)
# addContentToFile  -> descend(parts, len(parts)-1, create=True), then APPEND
# readContentFromFile -> descend all, must land on a file
# ls                -> descend all; file -> [its name], dir -> sorted children

# Ordering trap in C#: "lexicographical" means ORDINAL (character code), so
# uppercase sorts before lowercase. `names.Sort()` and `new SortedDictionary<>()`
# are culture-aware and quietly give a different answer -- pass
# StringComparer.Ordinal. Keeping children in a sorted map makes ls O(k) with no
# sort; a hash map + sort-at-ls is O(1) writes but O(k log k) reads.

# Boundary of a binary tree = root + left spine + leaves + reversed right spine.
# The only real work is keeping the three groups DISJOINT:
#   root  -> emitted once up front; every walk below starts at a CHILD of root
#   spine -> stop at the first leaf and do NOT emit it
#   leaves-> any left-to-right DFS emits them in the required order
def boundary(root):
    if not root: return []
    out = [root.val]                       # root leads even if it is childless
    if is_leaf(root): return out           # single node / root is never a "leaf"

    node = root.left                       # left spine, top-down
    while node and not is_leaf(node):
        out.append(node.val)
        node = node.left or node.right     # bend right ONLY if left is missing

    add_leaves(root)                       # DFS: if is_leaf -> emit, else recurse

    right, node = [], root.right           # right spine, collected top-down
    while node and not is_leaf(node):
        right.append(node.val)
        node = node.right or node.left
    return out + right[::-1]               # ...then reversed for bottom-up

# Skewed trees need no special case: the missing side contributes an empty
# group, so each node is still emitted exactly once.

# Leaf-similar trees: compare the LEAF SEQUENCE, ignore everything else.
# Preorder/inorder/postorder all give the same leaf order -- they all descend
# left before right -- so pick whichever is easiest to write.
def leaves(node, out):
    if not node: return
    if not node.left and not node.right:   # one child != leaf
        out.append(node.val); return
    leaves(node.left, out); leaves(node.right, out)
# leaves(a) == leaves(b)  -- SEQUENCE equality: order and length both count,
# so (1,2) != (2,1) and a prefix is not a match.

# Follow-up when the trees are huge: don't materialise both lists. Run two
# explicit DFS stacks in lockstep, pulling one leaf from each and bailing on
# the first mismatch -- O(h) space and it short-circuits. Push right before
# left so left pops first. Both stacks must empty on the same iteration;
# if one still has nodes, the other was only a prefix.

# Rewrite tree with subtree sums: two trees of IDENTICAL shape, walked in
# lockstep. sum(node) = node.val + sum(left) + sum(right) -- a post-order fold,
# so the parent is filled on the way BACK UP. One walk does both jobs: return
# the sum to the caller AND assign it into the mirror node. No node->node map.
def fill(a, b):                      # a from tree1, b the node to overwrite
    if not a: return 0               # same shape => b is None too
    s = a.val + fill(a.left, b.left) + fill(a.right, b.right)
    b.val = s                        # assign AFTER recursing, never before
    return s

# Deep/skewed input -> explicit stack instead of recursion. Push root-right-left
# onto stack1, move each pop to stack2; popping stack2 yields left-right-root
# (post-order). By then b's children already hold their sums, so the OUTPUT TREE
# is the memo table: b.val = a.val + b.left.val + b.right.val.

# Follow-up "many subtrees, few threads": siblings are independent, so fork
# them -- but a task per node costs more than the addition. Use a DEPTH CUTOFF
# (~ceil(log2(cores)) + 1, then go sequential): 2-4 tasks per core, enough for
# the pool's work-stealing to rebalance a lopsided tree. Fork ONE side and run
# the other inline so the current thread never parks. No locks needed -- tasks
# own disjoint subtrees and the join at each parent publishes the child writes.
# The ceiling on speedup is core count, not subtree count.
```
