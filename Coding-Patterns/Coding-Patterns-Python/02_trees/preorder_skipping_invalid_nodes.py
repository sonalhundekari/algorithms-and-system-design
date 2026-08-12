# Pre-order Traversal Skipping Marked (Invalid) Nodes
# Difficulty: Easy / Medium  (N-ary pre-order, LC #589, with a predicate on emit)
# Pattern: DFS with a GATED VISIT — recurse unconditionally, emit conditionally.
#
# Problem: a tree arrives as an edge list of [parent, child] pairs. Some node
# ids are marked invalid. Return the pre-order traversal over the VALID nodes,
# where the children of an invalid node promote and attach to the nearest valid
# ancestor — i.e. they are still visited, in the slot the invalid node itself
# would have occupied, in the original child order.
#
# The whole problem is one observation: "delete the node and contract its
# children into the parent" changes WHICH nodes are printed, not the ORDER in
# which the walk reaches them. A pre-order DFS already reaches every node in
# exactly the promoted order, so the contraction is free — split the recursion
# step from the emit step and put the validity test on the emit alone:
#
#     def visit(node):
#         if valid(node): out.append(node)   # emit  <- gated
#         for child in children[node]:       # recurse <- NEVER gated
#             visit(child)
#
# Deleting a node from the output does not reorder anything that came after it,
# which is precisely what "promote in place" means.
#
#         0                edges = [[0,1],[0,2],[1,3],[1,4],[2,5],[2,6]]
#        / \               invalid = {1, 6}
#       1   2
#      / \  / \            walk: 0 emit | 1 skip -> 3 emit, 4 emit
#     3  4 5   6                 | 2 emit -> 5 emit, 6 skip
#                          -> [0, 3, 4, 2, 5]
#
#     contracted tree, for a sanity check — same sequence, no rebuild needed:
#         0
#       / | \
#      3  4  2
#            |
#            5
#
# Traps:
#   - Do not build the contracted tree first. Re-parenting every child of every
#     invalid node onto its nearest valid ancestor is a correct second pass, but
#     it is a second pass — extra code, extra memory, and one more place to get
#     the child ORDER wrong. Gated emission needs neither.
#   - Do not gate the RECURSION on validity (`if valid(node): for child ...`).
#     That deletes whole subtrees instead of contracting one node, which is a
#     different problem. An invalid node still has to hand its children down.
#   - Child order is the order the edges appear in the input, not sorted id
#     order. Append to a plain list as you scan the edges and that is automatic;
#     a set or a dict-of-sets loses it.
#   - An invalid ROOT is not a special case — the output simply starts with its
#     children. Only the emit is skipped; the loop below it runs as always.
#   - Iterative version: push children in REVERSE so they pop in input order.
#   - `invalid` must be a set. A list makes the membership test O(k) and the
#     whole traversal O(n·k).
#
# Time: O(n + m) to build the child lists, O(n) to walk — O(n) overall for a
#       tree, where m = n - 1.
# Space: O(n) for the child map and output, plus O(h) for the stack.
#
# Depth note: a path-shaped tree makes h = n. n in the 10^5 range blows
# CPython's default 1000-frame recursion limit, so the iterative version below
# is the one to reach for when the interviewer pushes on input size.

from collections import defaultdict
from typing import Iterable, Optional


def build_children(edges: Iterable[Iterable[int]]) -> dict[int, list[int]]:
    """parent -> children, in the order the edges appear in the input."""
    children: dict[int, list[int]] = defaultdict(list)
    for parent, child in edges:
        children[parent].append(child)
    return children


def find_root(n: int, edges: Iterable[Iterable[int]]) -> Optional[int]:
    """The one node that is never a child. Directed edge lists only.

    An undirected edge list carries no such asymmetry — there the root has to be
    given, and the DFS additionally needs a `parent` argument (or a visited set)
    so it does not walk back up the edge it arrived on.
    """
    has_parent = [False] * n
    for _, child in edges:
        has_parent[child] = True
    for node in range(n):
        if not has_parent[node]:
            return node
    return None  # a cycle, not a tree


# ---- Approach 1: recursive, gated emission ----
def preorder_valid(n: int, edges: list[list[int]], root: Optional[int],
                   invalid: Iterable[int]) -> list[int]:
    if n <= 0:
        return []

    children = build_children(edges)
    bad = set(invalid)
    if root is None:
        root = find_root(n, edges)
        if root is None:
            return []

    out: list[int] = []

    def visit(node: int) -> None:
        if node not in bad:
            out.append(node)        # emit: gated on validity
        for child in children[node]:
            visit(child)            # recurse: never gated — children promote

    visit(root)
    return out


# ---- Approach 2: iterative, same gated emission, O(h) explicit stack ----
# Push children in reverse so the leftmost child is on top and pops first;
# that single `reversed` is what preserves the input child order.
def preorder_valid_iterative(n: int, edges: list[list[int]], root: Optional[int],
                             invalid: Iterable[int]) -> list[int]:
    if n <= 0:
        return []

    children = build_children(edges)
    bad = set(invalid)
    if root is None:
        root = find_root(n, edges)
        if root is None:
            return []

    out: list[int] = []
    stack = [root]

    while stack:
        node = stack.pop()
        if node not in bad:
            out.append(node)
        for child in reversed(children[node]):
            stack.append(child)

    return out


# ---- Approach 3: the contraction, written out once for comparison ----
# What candidates usually reach for: rebuild the tree with the invalid nodes
# spliced out, then run a plain pre-order. It is correct, and it is strictly
# more work — two passes, a second adjacency structure, and the child order has
# to be reassembled by hand at each splice point. Kept here only to show that it
# produces the identical sequence.
def preorder_valid_by_contraction(n: int, edges: list[list[int]],
                                  root: Optional[int],
                                  invalid: Iterable[int]) -> list[int]:
    if n <= 0:
        return []

    children = build_children(edges)
    bad = set(invalid)
    if root is None:
        root = find_root(n, edges)
        if root is None:
            return []

    def valid_children(node: int) -> list[int]:
        """This node's children after invalid ones are spliced out, in order."""
        result: list[int] = []
        for child in children[node]:
            if child in bad:
                result.extend(valid_children(child))  # promote, in place
            else:
                result.append(child)
        return result

    contracted = {node: valid_children(node) for node in range(n)}
    roots = [root] if root not in bad else valid_children(root)

    out: list[int] = []

    def visit(node: int) -> None:
        out.append(node)
        for child in contracted[node]:
            visit(child)

    for r in roots:
        visit(r)
    return out


# ---- Tests ----
if __name__ == "__main__":
    #         0
    #        / \
    #       1   2
    #      / \  / \
    #     3  4 5   6
    full = [[0, 1], [0, 2], [1, 3], [1, 4], [2, 5], [2, 6]]

    #          0
    #        / | \
    #       1  2  3        n-ary, uneven, 7 nodes
    #      /   |
    #     4    5
    #          |
    #          6
    nary = [[0, 1], [0, 2], [0, 3], [1, 4], [2, 5], [5, 6]]

    cases = [
        ("stated example", 7, full, 0, [1, 6], [0, 3, 4, 2, 5]),
        ("nothing invalid", 7, full, 0, [], [0, 1, 3, 4, 2, 5, 6]),
        ("invalid root", 7, full, 0, [0], [1, 3, 4, 2, 5, 6]),
        # root + two interior nodes gone: 1 and 2 promote their children to the
        # top level, and with 0 gone the output is just the four leaves in order.
        ("invalid root + 2 interior", 7, full, 0, [0, 1, 2], [3, 4, 5, 6]),
        ("both interior, root kept", 7, full, 0, [1, 2], [0, 3, 4, 5, 6]),
        ("all invalid", 7, full, 0, [0, 1, 2, 3, 4, 5, 6], []),
        ("only leaves invalid", 7, full, 0, [3, 4, 5, 6], [0, 1, 2]),
        ("single valid leaf", 7, full, 0, [0, 1, 2, 3, 5, 6], [4]),
        # 2 and 5 both go: 6 promotes all the way to the root, and it lands in
        # 2's old slot -- BEFORE sibling 3, not after it.
        ("n-ary, chain contracted", 7, nary, 0, [2, 5], [0, 1, 4, 6, 3]),
        ("n-ary, mid of chain gone", 7, nary, 0, [5], [0, 1, 4, 2, 6, 3]),
        ("single node", 1, [], 0, [], [0]),
        ("single node, invalid", 1, [], 0, [0], []),
        ("empty tree", 0, [], None, [], []),
        # Invalid ids that are not in the tree are simply never matched.
        ("invalid ids off-tree", 7, full, 0, [42, 1], [0, 3, 4, 2, 5, 6]),
    ]

    for label, n, edges, root, invalid, expected in cases:
        rec = preorder_valid(n, edges, root, invalid)
        itr = preorder_valid_iterative(n, edges, root, invalid)
        con = preorder_valid_by_contraction(n, edges, root, invalid)
        ok = rec == expected and itr == expected and con == expected
        print(f"{'PASS' if ok else 'FAIL'}  {label:<26} {rec}"
              + ("" if ok else f"  expected {expected}"
                               f"  iterative {itr}  contracted {con}"))
        assert ok, label

    # Root derived from the edge list rather than given.
    assert find_root(7, full) == 0
    assert preorder_valid(7, full, None, [1, 6]) == [0, 3, 4, 2, 5]
    # A shuffled edge list still names 0 as root, and each parent's children
    # keep the order they appear in — here 2 before 1 under the root.
    shuffled = [[2, 5], [0, 2], [1, 4], [0, 1], [2, 6], [1, 3]]
    assert preorder_valid(7, shuffled, None, []) == [0, 2, 5, 6, 1, 4, 3]
    print(f"{'PASS'}  {'root derived from edges':<26} 0")

    # Depth: a 100,000-node path. Every other node is invalid, so the answer is
    # the 50,000 even ids. The recursive version needs a raised recursion limit
    # here; the iterative one does not care.
    depth = 100_000
    chain = [[i, i + 1] for i in range(depth - 1)]
    odds = range(1, depth, 2)
    deep = preorder_valid_iterative(depth, chain, 0, odds)
    assert deep == list(range(0, depth, 2))
    print(f"{'PASS'}  {'deep chain (100k nodes)':<26} {len(deep)} kept")

    print("All tests passed.")
