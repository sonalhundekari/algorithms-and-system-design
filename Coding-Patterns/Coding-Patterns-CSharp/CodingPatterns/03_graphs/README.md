# Pattern: Graphs

## Key Techniques
- **BFS** — shortest path in unweighted graph, level-by-level exploration
- **DFS** — connected components, cycle detection, topological sort
- **Topological Sort** — BFS variant (Kahn's algorithm) for DAG ordering
- **Visited set** — always track visited nodes to avoid cycles
- **Trie-driven grid DFS** — when matching MANY words against one board, invert the loop: walk the board once carrying a trie pointer, instead of re-running the board search per word
- **BFS over non-grid states** — the "graph" is often implicit (integers, strings, board configurations); the only new requirement is a *ceiling*, because an unbounded state space makes BFS non-terminating on an unreachable target
- **Reverse the undo-trace** — to build a path *to* a target, walk the target BACKWARDS to a known hub and reverse the list; deciding "what op produced this value" is far easier than guessing forwards
- **Old→new index map** — when a node-indexed array shrinks, never patch indices in place; build `oldToNew[]` once and rewrite every stored index through it
- **Parent-direction grid** — BFS that stores *which move arrived here* turns a distance field into replayable moves for one byte per cell, instead of a path per cell
- **"Visit all targets" is a TSP, not a BFS** — nearest-first is a heuristic; the exact answer is Held-Karp over the targets with BFS distances as the metric
- **Propagate the INPUTS down a DAG, never the answer** — when a value is `f(accumulated A, accumulated B)`, push A and B separately and combine at each node; combining early (subtracting, clamping, min-ing) destroys information a descendant still needs
- **Pick the decision level so a constraint becomes structural** — one queen per row is forced, so recursing on rows deletes the row constraint and turns "choose n of n^2 squares" into "choose a column n times"
- **Constraint keys, not board scans** — a conflict test is O(1) once each constraint has an index that is *constant along it* (`col`, `row - col`, `row + col`); mark on the way down, unmark on the way out
- **Bitmask the frontier** — when the constraint sets are all "which columns are blocked in this row", they collapse to three ints, and shifting them by one row is the diagonal
- **Halve the search by symmetry** — if a mirror/rotation maps solutions to solutions with no fixed points, explore one half and double, handling the self-symmetric case separately

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Number of Islands | #200 | Medium | DFS/BFS flood fill |
| Course Schedule | #207 | Medium | Topological sort / cycle detection |
| Word Ladder | #127 | Hard | BFS shortest path |
| Clone Graph | #133 | Medium | DFS/BFS + HashMap |
| Closest Bathroom / Desk | #542-like | Medium (Hard follow-up) | Multi-source BFS; 1-D sweep; DP matching |
| Course Schedule + timing | #207/#210/#2050 | Medium (Hard follow-up) | Kahn; layered BFS; topological DP; DAG relaxation |
| Tree Node Deletion → Max Height | #1273-like | Medium (Hard follow-up) | Post-order DFS with promotion; tree DP over a height budget; bottom-up greedy |
| Word Search II | #212 | Hard | Trie + grid DFS/backtracking with prefix & leaf pruning |
| Reach Target via Add / Double / Halve | #991-like | Medium | BFS over integer states; reverse-the-undo-trace construction |
| Forest Parent Array → Delete Node | — | Medium | Old→new index remap; nearest-surviving-ancestor lift; subtree marking |
| Robot Eats Candies | — | Easy sim (Hard planner) | Grid simulation; BFS with path reconstruction; Held-Karp TSP over the targets |
| DAG Allow / Disallow Propagation | — | Medium | Kahn topological sort + two inherited bitmasks; role-privilege inheritance |
| N-Queens / N-Queens II | #51/#52 | Hard | Row-by-row backtracking with column & diagonal sets; bitmask + mirror symmetry for the count |

## Pattern Cheat Sheet

```python
# BFS shortest path template
from collections import deque
def bfs(start, end, graph):
    queue = deque([(start, 0)])  # (node, distance)
    visited = {start}
    while queue:
        node, dist = queue.popleft()
        if node == end: return dist
        for neighbor in graph[node]:
            if neighbor not in visited:
                visited.add(neighbor)
                queue.append((neighbor, dist + 1))
    return -1

# Topological sort (Kahn's BFS)
from collections import deque
def topo_sort(n, edges):
    in_degree = [0] * n
    adj = [[] for _ in range(n)]
    for u, v in edges:
        adj[u].append(v)
        in_degree[v] += 1
    queue = deque(i for i in range(n) if in_degree[i] == 0)
    order = []
    while queue:
        node = queue.popleft()
        order.append(node)
        for nb in adj[node]:
            in_degree[nb] -= 1
            if in_degree[nb] == 0:
                queue.append(nb)
    return order if len(order) == n else []  # empty = cycle

# Wall-clock timing on top of the same skeleton -- pick the RIGHT model:
#   batch/barrier  total  = sum over waves of max(times in wave)
#   earliest finish (2050) finish[v] = time[v] + max(finish[p] for p in prereqs)
#   any-one-prereq         finish[v] = time[v] + min(finish[p] for p in prereqs)

# MANY words vs ONE board (Word Search II) -- put the DICTIONARY in a trie and
# walk the BOARD once. Per-word search is O(W * m*n * 4^L) and re-walks shared
# prefixes W times; this bounds the search by the dictionary's SHAPE instead.
def find_words(board, words):
    root = {}
    for w in words:                       # build trie; '$' marks a terminal
        node = root
        for ch in w:
            node = node.setdefault(ch, {})
        node['$'] = w                     # store the WORD, not a path string
    found = []
    def dfs(r, c, parent):
        ch = board[r][c]
        node = parent.get(ch)
        if node is None: return           # prefix pruning -- the whole point
        if '$' in node: found.append(node.pop('$'))   # pop => dedupe
        board[r][c] = '#'                 # in-place visited mark
        for nr, nc in ((r-1,c),(r+1,c),(r,c-1),(r,c+1)):
            if 0 <= nr < len(board) and 0 <= nc < len(board[0]):
                dfs(nr, nc, node)
        board[r][c] = ch                  # backtrack
        if not node: parent.pop(ch)       # LEAF PRUNING: spent branch, unlink it
    for r in range(len(board)):
        for c in range(len(board[0])):
            dfs(r, c, root)
    return found
# Extra pruning that matters at 12x12 with ~30k words:
#   - reject any word whose letter counts exceed the board's letter counts
#   - insert a word REVERSED when its last letter is rarer on the board than its
#     first (adjacency is symmetric, so the same paths are found from the rare end)
#     ...but then a node can hold TWO words ("ba" forwards, "ab" backwards share a
#     path), so the terminal payload must be a LIST or you silently drop words.

# Deletion with PROMOTION (children rise to the nearest surviving ancestor).
# One post-order pass; a deleted node just does not add a level:
#   contrib(v) = max(contrib(c) for c in children, default 0) + (0 if deleted else 1)
#   height in levels = contrib(root)   # a deleted root gives the forest max for free
# Min deletions for height <= k  ==  every root-to-leaf path keeps at most k nodes:
#   dp[v][h] = min(1 + sum(dp[c][h]),        # delete v: no level spent
#                      sum(dp[c][h-1]))      # keep v:   one level spent, h >= 1
#   no-DP greedy: bottom-up, delete v only when 1 + max(contrib(children)) > k

# Forest as a PARENT ARRAY: parent[i] is i's parent and a ROOT STORES ITSELF
# (parent[i] == i, the union-find convention). Deleting a node shrinks the array,
# so every index above the hole moves and every stored parent is a STALE index.
# ASK FIRST: do the children become roots, attach to the grandparent, or die with
# the subtree? Three legal answers, three different arrays.
#
# Repair in OLD indices, THEN translate. Never patch in place:
def delete(parent, victim):                  # children -> roots convention
    old_to_new, kept = [-1] * len(parent), 0
    for i in range(len(parent)):             # survivors keep their relative order
        if i != victim:
            old_to_new[i] = kept; kept += 1  # == i - (i > victim), derived not assumed
    out = [0] * kept
    for i, p in enumerate(parent):
        if i == victim: continue
        out[old_to_new[i]] = (old_to_new[i]          # parent gone -> be a root:
                              if p == victim         # OWN NEW index, never the old one
                              else old_to_new[p])
    return out
# The bug this avoids: writing the promoted root as `i`, then running a
# `if p > victim: p -= 1` sweep over it -- the node silently points at its old
# neighbour and the result is still a structurally valid forest, so nothing
# throws. There is no sentinel in this encoding; every int is a legal index.
# A parent may have a LARGER index than its child ([2,2,2] is root 2 + children
# 0,1) -- nothing orders parents first. Attach-to-ancestor needs a memoised climb
# (lift[v] = nearest surviving node on v's chain, -1 if none) to stay O(n).

# ANY path a -> b under add(n)=n+2, dub(n)=n*2, split(n)=n//2.
# 1 is a universal hub: everything splits down to it, and everything builds up
# from it. Build the UP half by UNDOING b and reversing -- "what op produced this
# value" is easy, "which op should I apply next" is not. Stop at 1, never 0:
# split(1) = 0 leaves the positive integers, and that `> 1` is the whole bug.
def func(a, b):
    ops = []
    while a > 1:                       # down: split-only, needs no knowledge of b
        a //= 2; ops.append("split")
    up = []
    while b > 1:                       # up: undo b, then reverse the trace
        if b % 2 == 0: b //= 2; up.append("dub")   # even  => last op was a dub
        else:          b -= 2; up.append("add")    # odd>1 => last op was an add
    return ops + up[::-1]              # odd stays odd under -2, so it lands on 1
# Cost is O(log a + b) ops, and the O(b) is NOT laziness. With only add/dub,
# every value reachable from 1 has the form 2^k + 2*sum(2^d_i) where k = total
# dubs and d_i = dubs after the i-th add; an odd b forces k = 0, so every term
# is 2^0 and you pay exactly (b-1)/2 adds. split is the ONLY escape, by arriving
# from ABOVE: split(2b) == split(2b+1) == b. So for odd t > 1,
#   reach 2*(t-1)  [even, odd part <= (t-1)/2]  --add--> 2t --split--> t
# which recurses to O(log^2 b) ops with no search at all.
# "Robot eats every candy in the fewest moves" -- the trap is that this LOOKS
# like a BFS and is not. Nearest-candy-first is a heuristic that loses:
#   *...*@.*   greedy takes the candy 1 step LEFT, then crosses twice  -> 11
#              optimal goes RIGHT first; the long walk back eats col 4 -> 9
# Eating-by-walking is what breaks greedy, but it does NOT break the cost model:
# any winning route induces an EAT ORDER, and its length is >= the sum of
# pairwise shortest paths in that order -- and that sum is achievable, since
# eating extras early only helps. So optimal = open-path TSP from the robot:
#   dp[mask][i] = shortest walk eating exactly `mask`, ending on candy i
#   dp[mask | 1<<j][j] = min(dp[mask][i] + d[i][j])   j not in mask
#   seed dp[1<<i][i] = d[start][i];  answer = min_i dp[full][i]   (no return leg)
# O(2^K * K^2) after K+1 BFS runs for d[][]. Exact to ~18 candies, greedy past
# that. Battery of N moves / weighted candies reuse the SAME table: the best
# mask with min_i dp[mask][i] <= N. BFS stores the arriving DIRECTION per cell,
# so a distance field replays as a command string.
#
# SHORTEST path is a BFS over the integers (neighbours n+2, 2n, n//2) -- but it
# needs a CEILING, or the state space is infinite and BFS never terminates on an
# unreachable target. Any ceiling >= max(a,b) already guarantees a path exists
# (the construction above never leaves [1, max(a,b)]), so the honest claim is
# "shortest under the ceiling", never "globally shortest".

# N-QUEENS -- the whole problem is choosing the decision level. n queens in n
# rows with none sharing a row means EVERY solution has exactly one queen per
# row, so recurse on rows and pick a column: C(n^2, n) candidates become n^n,
# and the row constraint can no longer be violated. Then index each remaining
# constraint by something CONSTANT along it, so a check is O(1), not a scan:
#   column        col          diagonal "\"  row - col     anti "/"  row + col
def solve_n_queens(n):
    boards, queen_col = [], [0] * n
    cols, diag, anti = set(), set(), set()
    def place(row):
        if row == n:
            boards.append(["." * c + "Q" + "." * (n-c-1) for c in queen_col]); return
        for col in range(n):
            if col in cols or row-col in diag or row+col in anti: continue
            cols.add(col); diag.add(row-col); anti.add(row+col)   # mark
            queen_col[row] = col
            place(row + 1)
            cols.discard(col); diag.discard(row-col); anti.discard(row+col)  # unmark
    place(0)
    return boards
# O(n!) upper bound (row r has <= n - r free columns) -- but n = 8 explores ~2k
# nodes, not 40320. In C# use bool[n] / bool[2n-1], indexing the diagonal as
# row - col + (n - 1) to keep it non-negative.

# COUNT ONLY (#52) -- two upgrades, neither of which changes the O(n!) shape:
#   bitmask: the three sets become ints of "columns blocked in THIS row", so
#            available = ~(cols | diag | anti) & full  and  bit = avail & -avail.
#            Descending a row slides a diagonal block by one -- that is what a
#            diagonal is -- so diag <<= 1, anti >>= 1, & full drops what fell off.
#   symmetry: left-right mirroring maps solutions to solutions and fixes none of
#            them, so search row 0's LEFT HALF and double; for odd n the middle
#            column is its own mirror and is counted once, separately.
def total_n_queens(n):
    full = (1 << n) - 1
    def count(cols, diag, anti):
        if cols == full: return 1              # all columns used == n rows placed
        total, avail = 0, ~(cols | diag | anti) & full
        while avail:
            bit = avail & -avail; avail -= bit
            total += count(cols|bit, ((diag|bit) << 1) & full, (anti|bit) >> 1)
        return total
    half = sum(2 * count(1<<c, (1<<c << 1) & full, (1<<c) >> 1) for c in range(n//2))
    mid  = count(1<<(n//2), (1<<(n//2) << 1) & full, (1<<(n//2)) >> 1) if n % 2 else 0
    return half + mid
# Sanity anchors: n=1..9 -> 1, 0, 0, 2, 10, 4, 40, 92, 352.  n=2 and n=3 have NO
# solution, so an empty answer is correct, not a bug. n=12 -> 14200 counts fine
# and should never be asked to render.

# ALLOW / DISALLOW down a DAG. Each node annotates letters to allow and to
# disallow; both inherit transitively and disallow wins ("once disallowed, always
# disallowed"). Carry TWO masks and combine only at the end:
def effective(n, edges, allow, disallow):          # edges are [parent, child]
    children = [[] for _ in range(n)]
    indeg = [0] * n
    for p, c in edges:
        children[p].append(c); indeg[c] += 1
    a = [mask(allow[i])    for i in range(n)]      # mask("abc") -> 0b111
    d = [mask(disallow[i]) for i in range(n)]
    q = deque(i for i in range(n) if indeg[i] == 0)
    seen = 0
    while q:
        v = q.popleft(); seen += 1
        for c in children[v]:                      # v is final: all ITS ancestors
            a[c] |= a[v]; d[c] |= d[v]             # are already folded in
            indeg[c] -= 1
            if indeg[c] == 0: q.append(c)
    if seen != n: raise ValueError("cycle")        # ask up front if it's a DAG
    return [a[i] & ~d[i] for i in range(n)]        # disallow wins, computed last
# THE TRAP: pushing the ANSWER down instead -- eff[c] = union(eff[parents]) |
# own_allow & ~own_disallow -- resurrects letters. On 0 allows x -> 1 disallows x
# -> 2 allows x it returns x at node 2; the correct answer is empty. Subtracting
# early throws away the fact that x is forbidden, and a descendant re-allows it.
# Disallow-wins is not arbitrary: on a DAG two parents can disagree at EQUAL
# distance, so "nearest ancestor wins" is under-specified. Disallow-wins is
# order-independent and monotone (disallow only grows downward), which is what
# licenses one topological pass and per-node caching.
# Allow-only variant (role privileges): same loop, drop the d[] half.
#   privileges [[A],[B],[C]], grants [[0,1],[1,2],[2,3]] -> [A], [A,B], [A,B,C]
#   ...and role 3, which grants mention but privileges never listed -- size n off
#   BOTH arrays or you throw on it (or silently drop trailing ungranted roles).
```
