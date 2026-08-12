# Pattern: Trees

## Key Techniques
- **DFS (recursive/iterative)** — preorder, inorder, postorder traversal
- **BFS (level-order)** — use a queue; process nodes level by level
- **BST properties** — left < node < right; inorder traversal gives sorted output
- **Divide and conquer** — solve left subtree, right subtree, combine
- **Preorder + close marker** — the one extra token that makes an n-ary preorder walk uniquely decodable by a stack
- **Arrow down vs. path bending here** — the global answer sums both children; the return value picks one side, because a parent can only extend one branch
- **Gated emission** — split "recurse" from "emit" and put the predicate on the emit alone; filtering a traversal never reorders what survives, so node deletion/contraction needs no second pass

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Validate BST | #98 | Medium | DFS with min/max bounds |
| Lowest Common Ancestor | #236 | Medium | DFS post-order |
| Binary Tree Level Order Traversal | #102 | Medium | BFS with queue |
| Serialize / Deserialize Binary Tree | #297 | Hard | BFS or DFS + string encoding |
| Serialize / Deserialize Dictionary Trie | #428 variant | Medium | Preorder DFS + close marker; stack decode |
| Longest Univalue Path | #687 | Medium | Post-order DFS; bend = left + right, return = max(left, right) |
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

# Serializing an N-ARY tree (a trie is one). Preorder alone is ambiguous --
# "ab" could be a->b or two siblings -- so emit ONE token per node meaning
# "subtree finished". Then the string decodes with a stack and no lookahead.
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
        if   ch == ")": stack.pop()
        elif ch == "$": stack[-1].is_word = True
        else:
            child = TrieNode()
            stack[-1].children[ch] = child   # keys arrive already sorted
            stack.append(child)
    return root

# ["app","apple","bat"] -> "app$le$)))))bat$)))"
# Collecting words: emit a node BEFORE descending, so "app" precedes "apple"
# with no special case for one word being a prefix of another.
# Honest sizing: the trie costs 2 chars/node, "\n".join(words) costs 1
# char/character -- the trie only wins when prefixes are actually shared.

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
# n goes to 10^4, so a skewed tree blows CPython's 1000-frame recursion limit ->
# iterative post-order: push each node twice (expand, then combine) and memo the
# arrow lengths in a dict keyed by id(node).

# Pre-order over a tree given as an EDGE LIST, skipping invalid nodes (their
# children promote to the nearest valid ancestor). Deleting a node changes WHICH
# nodes print, not the ORDER the walk reaches them -- so gate the EMIT, never
# the recursion, and the contraction is free.
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
# invalid root is NOT a special case; `bad` must be a set, not a list; building
# the contracted tree first is a correct but redundant second pass.
# Iterative for deep trees: stack, push children REVERSED so they pop in order.
```
