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
- **Preorder + close marker** — the one extra token per node that makes an n-ary preorder walk uniquely decodable by a stack; sort the children and the encoding becomes canonical
- **Arrow down vs. path bending here** — the global answer sums both children; the return value picks one side, because a parent can only extend one branch
- **Gated emission** — split "recurse" from "emit" and put the predicate on the emit alone; filtering a traversal never reorders what survives, so node deletion/contraction needs no second pass

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
| Longest Univalue Path | #687 | Medium | Post-order DFS; bend = left + right, return = max(left, right) |
| Serialize / Deserialize Dictionary Trie | #428 variant | Medium | Preorder DFS + close marker; stack decode; sorted children = canonical |
| Pre-order Skipping Invalid Nodes | #589 variant | Medium | Edge list → child map; pre-order DFS with the validity test on emit only |

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

# Longest univalue path (#687) -- same shape as #543 diameter and #124 max path
# sum. Every path has ONE highest node, the point where it bends, so each node
# answers two DIFFERENT questions:
#     best   = left + right        <- the path that bends here; both sides count
#     return = max(left, right)    <- one arrow; a parent extends only one side
def arrow(node):                   # longest univalue path DOWNWARD, in EDGES
    if not node: return 0
    l, r = arrow(node.left), arrow(node.right)   # recurse FIRST, always
    left  = l + 1 if node.left  and node.left.val  == node.val else 0
    right = r + 1 if node.right and node.right.val == node.val else 0
    best[0] = max(best[0], left + right)
    return max(left, right)

# Traps: EDGES not nodes (a single node scores 0). Never guard the recursion
# behind the value test -- [1,4,5,4,4,null,5] hides its 4-4-4 answer under a root
# valued 1, so a subtree that disagrees with its parent must still be walked. The
# +1 and the value check are ONE expression, because an arrow is only usable when
# the EDGE matches; null children fall out of it as 0, so there is no leaf case.
# Deep/skewed input (n up to 10^4) -> iterative post-order: push each node twice
# (expand, then combine) and memo the arrow lengths in a dict keyed by identity.

# Serializing an N-ARY tree (a trie is one). Preorder alone is AMBIGUOUS --
# "ab" could be a->b or two siblings -- so emit ONE token per node meaning
# "subtree finished". The string then decodes with a stack, no lookahead:
#   a..z -> descend    '$' -> this node ends a word    ')' -> pop to parent
def encode(node, out):
    if node.is_word: out.append("$")
    for ch in sorted(node.children):      # sorted => CANONICAL encoding, and
        out.append(ch)                    # the decoded trie is already in
        encode(node.children[ch], out)    # lexicographic order (no final sort)
        out.append(")")                   # the root is never closed

def decode(data):                          # iterative: depth is irrelevant
    root = TrieNode()
    stack = [root]
    for ch in data:
        if   ch == ")": stack.pop()        # stack empty here => malformed input
        elif ch == "$": stack[-1].is_word = True
        else:
            child = TrieNode()
            stack[-1].children[ch] = child # keys arrive already sorted
            stack.append(child)
    return root

# ["app","apple","bat"] -> "app$le$)))))bat$)))"
# Collect words by emitting a node BEFORE descending, so "app" precedes "apple"
# with no special case for one word being a prefix of another.
# C#: SortedDictionary<char, Node> compares code points (ordinal), so the
# List<string>.Sort() culture trap does not apply to char keys. A Node[26]
# indexed by ch - 'a' is faster and sorted by construction.
# Honest sizing: the trie costs 2 chars/node, string.Join costs 1 char per
# character -- the trie only wins when prefixes are actually shared. Suffix
# sharing needs a DAWG; ~2 bits/node needs a succinct (LOUDS) encoding.

# Pre-order over a tree given as an EDGE LIST, skipping invalid nodes (their
# children promote to the nearest valid ancestor). Deleting a node changes WHICH
# nodes print, not the ORDER the walk reaches them -- so gate the EMIT, never
# the recursion, and the contraction comes out for free.
children = defaultdict(list)
for parent, child in edges:      # a LIST per parent: edge order == child order
    children[parent].append(child)

def visit(node):
    if node not in bad: out.append(node)   # emit   <- gated on validity
    for child in children[node]:           # recurse <- NEVER gated
        visit(child)

# root = the node that is never a child (directed edges); an undirected list has
# no such asymmetry, so the root must be given and the DFS needs a parent arg.
# edges=[[0,1],[0,2],[1,3],[1,4],[2,5],[2,6]], invalid={1,6} -> [0,3,4,2,5]
# Traps: gating the RECURSION deletes whole subtrees (a different problem); an
# invalid root is NOT a special case, the output just starts with its children;
# `bad` must be a HashSet; a Dictionary<int, List<int>> keeps edge order while a
# SortedDictionary/HashSet of children silently throws it away; building the
# contracted tree first is correct but a redundant second pass.
# Deep/skewed input -> explicit Stack<int>, pushing children REVERSED so the
# leftmost pops first. A CLR stack overflow cannot be caught: it kills the
# process, so "n is 10^5" means iterative, not "probably fine".
```
