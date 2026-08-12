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
- **When edges cost a network call, the complexity target is CALL COUNT** — an API-only graph is still a BFS, but the number to quote is "one call per reachable page", not O(V+E)
- **Mark visited on ENQUEUE, never on dequeue** — dequeue-marking still fetches once single-threaded, but it pushes every *edge* onto the queue, and it stops deduping at all the moment there are two workers
- **Threaded traversal: the check-and-add must be ONE operation** — `TryAdd`/`setdefault`, not `ContainsKey` then `Add`; a thread-safe container does not make a two-step decision thread-safe
- **An empty queue is not termination** — a worker may be mid-call and about to discover more; count *queued + in-flight*, incrementing before the child is visible and decrementing after its parent finishes expanding
- **Level-synchronous vs free-running parallelism** — a barrier per level keeps BFS distances exact; a free-running work queue is faster but its "first touch" is scheduling order, so it can only answer *which* pages are reachable, not how far
- **Delete by MASKING, not by removing** — when "gone" means "omit from the output", keep the node and carry a dead/hidden set; the node still connects its children, and the operation becomes O(1) and reversible
- **Recompute beats maintain when the answer is a whole traversal** — an ordered list rebuilt on demand is O(n) per query but O(1) per update; maintaining it live costs a mid-array insert per update and buys nothing, because the output is Θ(n) either way
- **Iterative preorder, two flavours with opposite worst cases** — a *node* stack (push children reversed, eldest last) is O(width); a *frame* stack of `(node, nextChildIndex)` is O(depth). Recursion is O(depth) on a 1 MB stack, i.e. dead at n = 10⁵
- **Validate an ORDER against the structure, not against a second traversal** — two walks that agree can be wrong the same way; assert "ancestor precedes descendant" and "elder sibling's whole subtree precedes the younger's" from the child lists
- **An O(1) cache is a claim about an INVARIANT** — "next free row per column" only means something while every column is a gap-free stack on the floor; the operation that punches holes (clearing a group) must either restore the invariant or admit the cache is dead and fall back to a scan
- **Flood fill in two phases** — collect every qualifying component first, clear them all at once; clearing mid-sweep makes the result depend on scan order even though components are disjoint
- **DSU is for merging, not deleting** — connected components suggest union-find, but a problem whose main move is *removing* a component has to rebuild the forest each round; a stamped flood fill is strictly cheaper
- **Stamp the visited array, don't reallocate it** — one `int[m,n]` for the object's lifetime with a monotonic round number beats a fresh `bool[m,n]` per call, which pays the operation's whole cost a second time
- **Seed the rescan from what changed** — if the board is *reduced* between calls, any new group must contain a cell that changed, so BFS from the dropped piece and from whatever gravity moved instead of rescanning m×n
- **A budget on the path is part of the STATE** — "cheapest within k stops" is not shortest-path over cities, it is shortest-path over `(city, hops used)`; Dijkstra's "first pop is final" is a theorem about the former and is flatly false about the latter
- **Bellman-Ford by ROUNDS, relaxed against a snapshot** — after round `r`, `dist[v]` is the cheapest cost using at most `r` edges; reading the *previous* round's array is the only thing stopping one round from chaining an entire path, and relaxing in place fails by returning a real route that overspends the budget
- **Dominance, not a visited set** — when the state has a second dimension, discard a popped state only if an already-popped one beat it on *both* (cheaper AND no more hops); that is the 1-D case of Pareto labelling, which is where the multi-budget follow-up goes
- **A hop bound makes negative cycles harmless** — round-bounded Bellman-Ford stays correct with negative weights because the walk length is capped; every Dijkstra variant dies. Know which of your solutions a sign change kills
- **Positive weights ⇒ optimal path is simple ⇒ a budget past n − 1 is vacuous** — clamp `k` and the constrained problem collapses back to plain Dijkstra
- **Dependency edges and blame edges are transposes** — "A calls B" means A *needs* B, so a failure travels callee→caller; reverse the graph before traversing, or you confidently report the one set of services that is provably fine
- **Memoize the LENGTH, not the path** — `[v] + dfs(u)` copies up to V entries on each of E edges, so the textbook "O(V+E)" longest-path DFS is really O(V·E) time and O(V²) space; store a length and a successor pointer and rebuild the one path you return
- **Kahn does four jobs in one pass** — topological order, the relaxation, cycle detection, *and* the seeding; inside a reachable-from-origin subgraph the origin is the unique zero-in-degree node, so the queue seeds itself
- **Longest simple path is NP-hard off a DAG** — condense SCCs and weight each component by its size; the result is cascade *depth* and an upper bound on the path, because a strongly connected component need not contain a Hamiltonian walk
- **Reachability is the all-dependencies-required special case** — the moment a service has two interchangeable backends, failure is a monotone AND/OR formula evaluated to a *least fixpoint*, not a traversal; the least fixpoint is what refuses "A is down because B is down because A is down"

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
| Wiki Shortest Clicks / crawl-all | #1971-like | Easy-Medium (Hard follow-up) | BFS over an API-only graph; visited-on-enqueue; threaded crawl with atomic dedupe + in-flight termination |
| Throne Inheritance | #1600 | Medium | Preorder DFS on demand over a growing n-ary tree; death as a mask, not a deletion; iterative walk for 10⁵ depth |
| Grid Drop and Remove Duplicates | #1263-like / Connect-4 | Easy → Medium-Hard composed | Cached column pointer for O(1) drop; flood-fill group clear; gravity + pointer repair; incremental cascade |
| Service Failure Forensics | #278/#2115-like | Easy → Medium-Hard composed | Monotone-predicate binary search (+ galloping); reverse-graph BFS blast radius; Kahn longest-path DP; SCC condensation for cycles |
| Cheapest Flights Within K Stops | #787 | Medium | Bellman-Ford over k+1 rounds against a snapshot; (city, hops) Dijkstra with hop-dominance; layered cost-relaxing BFS; layer table for the itinerary |

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

# IMPLICIT GRAPH BEHIND AN API (wiki shortest clicks). The only way to read an
# edge is get_linked_pages(uri), so the cost model is API CALLS, not comparisons,
# and the answer is "exactly one call per reachable page".
def shortest_clicks(start, target, api):
    if start == target: return 0             # the case everyone forgets
    q, visited = deque([(start, 0)]), {start}
    while q:
        uri, clicks = q.popleft()
        for link in api(uri):
            if link in visited: continue
            if link == target: return clicks + 1   # recognised on DISCOVERY:
            visited.add(link)                      # the target is never expanded,
            q.append((link, clicks + 1))           # nor is anything beside it
    return -1
# visited.add at ENQUEUE, not at dequeue. Dequeue-marking still fetches each page
# once (the dequeue check catches duplicates) -- what it costs is a queue of size
# E instead of V, and it collapses entirely under threads. Say it precisely; "it
# refetches k times" is an overstatement an interviewer will call.
# Other edges: cycles + self-links (the visited set handles both), unreachable
# (-1 vs None -- pick one), start outside the graph, URI canonicalization
# ("/wiki/Cat" vs "/wiki/cat#Anatomy" is ONE page and dedupe dies without it).
# Bidirectional BFS halves the exponent (2*b^(d/2) vs b^d) but needs a BACKLINK
# API that a forward-only helper cannot provide -- mention it, check the
# precondition, and finish the level before returning or the first meeting found
# is not the shortest.

# CRAWL-ALL variant: same loop, no target. DFS returns the same SET, so the
# argument is no longer correctness: BFS wins on stack depth (link chains are
# long), on partial results ("everything within k clicks" vs one dangling
# thread), on host locality against rate limiters, and on being parallelizable
# at all -- a frontier is a batch of independent equal-cost calls, while DFS's
# next call depends on the last one's result.

# THREADED CRAWL -- the traversal is the easy part. Two things are not:
#   DEDUPE:      check-and-add must be ATOMIC. `if x not in seen: seen.add(x)`
#                across two workers enqueues x twice, on every diamond.
#   TERMINATION: empty queue != done, a worker may be mid-fetch. Track
#                queued + in-flight, incrementing BEFORE the child is visible.
def crawl_parallel(start, api, workers=4):        # queue.Queue does the counting
    seen, lock, q = {start}, threading.Lock(), queue.Queue(); q.put(start)
    def worker():
        while (uri := q.get()) is not STOP:
            try:
                with lock:                        # check and add under ONE lock
                    fresh = [l for l in api(uri) if l not in seen]
                    seen.update(fresh)
                for l in fresh: q.put(l)          # children counted BEFORE...
            finally: q.task_done()                # ...the parent is released
        q.task_done()
    ...start workers...; q.join(); ...send STOP...; return seen
# q.join() IS the "queued + in-flight == 0" primitive -- but only because
# task_done() fires after the put()s. Swap those two lines and the crawl exits
# early on some runs and not others. Wrap the body in try/finally or one raising
# worker deadlocks join() forever.
# DISTANCES DO NOT SURVIVE THIS. The depth recorded is the depth of whoever
# discovered the page first, which under concurrency is not the smallest. If the
# distances are wanted, go level-synchronous: fetch the whole frontier with a
# pool, then fold the results in single-threaded (no lock needed at all). Barrier
# cost = the level's slowest page. Exact distances OR max throughput, pick one.
# Rate-limited API: cache the linked-pages result, batch a whole level per
# request if the API allows it, size concurrency to the QUOTA (semaphore/token
# bucket), retry 429s with backoff inside the same in-flight unit.

# GRID DROP + REMOVE DUPLICATES (Connect-4 board utility). Three parts that look
# independent and are not. Part 1 wants an O(1) drop, which means caching a
# pointer per column; that pointer is only meaningful under an INVARIANT --
#
#       every column is a gap-free stack sitting on the floor
#
# -- and part 2 (clear connected groups) is precisely the operation that breaks
# it. THAT is why part 3 exists: gravity is not cosmetic, it restores the
# precondition part 1's optimisation was standing on. Say this before coding.
def drop(self, color, col):                  # part 1
    r = self.next_free[col] if self.settled else self.lowest_empty(col)
    if r < 0: raise ValueError("column is full")
    self.grid[r][col] = color
    if self.settled: self.next_free[col] = r - 1        # the O(1)
# THE BUG: keep the cached pointer across a clear-without-gravity and a column
# whose TOP piece was just cleared still reports "full" (pointer says -1, cell
# says empty); a hole punched UNDER a survivor hands out an occupied row. Two
# honest designs: clearing always settles (invariant never breaks), or clearing
# may leave the board floating and the pointer is declared dead until Settle(),
# with drop degrading to an O(m) bottom-up scan. Never patch the number.
# Note the interviewer's own part-2 example output has a floating piece in it,
# so "clear implies gravity" is an assumption to state, not to smuggle in.

def clear_groups(self, seeds):               # part 2 -- BFS, never recursive DFS:
    self.stamp += 1; doomed = []             # a one-colour board is ONE component
    for sr, sc in seeds:                     # of m*n cells = stack overflow
        color = self.grid[sr][sc]
        if color == EMPTY or self.seen[sr][sc] == self.stamp: continue
        self.seen[sr][sc] = self.stamp       # mark on ENQUEUE
        comp, q = [(sr, sc)], [(sr, sc)]
        for r, c in q:                       # list-as-queue, index walks forward
            for nr, nc in neighbours(r, c):
                if self.seen[nr][nc] == self.stamp or self.grid[nr][nc] != color: continue
                self.seen[nr][nc] = self.stamp; q.append((nr, nc)); comp.append((nr, nc))
        if len(comp) >= self.min_group: doomed += comp
    for r, c in doomed: self.grid[r][c] = EMPTY        # TWO PHASES: collect, then
    return len(doomed)                                 # clear all at once
# `seen` is ONE int array stamped with a round number, not a fresh bool[m][n] per
# call -- reallocating pays the operation's entire cost a second time.
# Two phases because clearing mid-sweep makes the result depend on scan order
# even though components are disjoint. "All groups on the board as it stands."
# DSU is the wrong instinct here: it MERGES, and this problem's main move is
# deletion, which a disjoint-set forest cannot undo.

def apply_gravity(self):                     # part 3 -- two pointers per column
    for c in range(cols):
        write = rows - 1
        for read in range(rows - 1, -1, -1):
            if self.grid[read][c] == EMPTY: continue
            if write != read:
                self.grid[write][c] = self.grid[read][c]; self.grid[read][c] = EMPTY
            write -= 1
        self.next_free[c] = write            # REPAIR THE POINTER. every bulk
    self.settled = True                      # mutation owes the cache this.
# No zero-fill above `write` is needed and it is worth being able to say why:
# write only decrements when a piece is placed, so a piece read at row r lands at
# >= r and leaves write <= r-1 -- nothing occupied can survive at or below it.

# ASK: does removal CASCADE? Gravity drops survivors into new adjacencies, so
#   R Y 0                  0 0 0
#   B Y Y  --clear+fall--> 0 0 0  <- the two R's are now a group that did not
#   B R 0                  R R 0     exist before. Match-3 games cascade; the
# stated problem is silent. Fixpoint terminates because each round clears >= 2
# cells and nothing ever adds one: < m*n/2 rounds, O((m*n)^2) worst case.
# INCREMENTAL follow-up -- maintain "reduced AND settled between calls", then a
# full rescan is waste: after a drop the only new group must contain the dropped
# piece (any other group predates it, contradiction with reduced); after gravity
# any new group must contain a cell whose contents CHANGED. So seed from the
# dropped cell, then from the cells gravity moved. Cost becomes proportional to
# what actually moved. An invariant argument that is not tested is a hypothesis:
# cross-check it against the full-scan cascade on randomised move sequences.
# TESTING: the properties ARE the tests -- after every op the board must be
# REDUCED, SETTLED, and its pointers must AGREE with its contents. Three O(m*n)
# predicates that catch the stale-pointer and bad-seed bugs instantly; example
# boards catch neither.

# THRONE INHERITANCE (#1600). The order is a pure FUNCTION of (tree, dead set) --
# there is nothing to maintain. Birth appends, death flags, the walk is on demand.
class ThroneInheritance:
    def __init__(self, king):
        self.king, self.kids, self.dead = king, {king: []}, set()
    def birth(self, parent, child):
        self.kids[parent].append(child)          # append IS "youngest last"
        self.kids[child] = []                    # every node has a list, never a KeyError
    def death(self, name):
        self.dead.add(name)                      # the TREE does not change. ever.
    def getInheritanceOrder(self):
        order, stack = [], [self.king]
        while stack:
            name = stack.pop()
            if name not in self.dead: order.append(name)   # a PRINT filter, not a prune:
            stack.extend(reversed(self.kids[name]))        # a dead parent still passes
        return order                                       # the crown to its children
# reversed() is the whole "older children first" rule -- the one line to get backwards.
# birth O(1), death O(1), order O(n). Maintaining a live sorted list instead means a
# mid-array insert per birth: strictly worse, and the insertion point is its own bug.
#
# THE TRAP, stated precisely. Deleting the dead node and splicing its children into
# its slot gives the SAME list -- splicing preserves preorder -- so do not claim it
# outputs the wrong order. It breaks on: (1) the KING dying, no slot to splice into;
# (2) a birth naming a dead parent, still legal since he is in the tree; (3) undo /
# resurrect, O(1) as a flag, impossible once the shape is gone; (4) O(children) per
# death. Masking wins on all four and is less code.
#
# DEPTH. 10^5 births with no shape guarantee = a 10^5-deep chain. Python's recursion
# limit (and C#'s 1 MB stack) dies there; in .NET StackOverflowException cannot even
# be caught. Iterate. Node stack = O(width) (a star pushes all 10^5 children); frame
# stack of (node, next_child_index) = O(depth). Opposite worst cases, both O(n).
#
# Q*O(n) is a real cost, and CACHING (invalidate on birth/death) only collapses runs
# of consecutive queries -- the output is Theta(alive), so no structure beats it while
# the API returns the whole list. If the ask is "who is k-th in line?", THAT is where
# you win: store living-subtree counts per node and descend, skipping a child whose
# count <= remaining k. O(depth) per query, O(depth) to update on birth/death.
#
# TESTING an order: two traversals that agree can be wrong the same way. Assert
# against the SHAPE -- ancestor before descendant, and elder sibling's entire subtree
# before the younger's -- computed from the child lists. Reverse every walk at once
# and membership checks, list length, and cross-traversal agreement all still pass.

# SERVICE FAILURE FORENSICS -- first error log, blast radius, longest cascade.
#
# PART 1. Binary search needs a MONOTONE PREDICATE, not a sorted array:
#   p(i) = logs[i].startswith("[Error]")   is false...false true...true
# The "line before an error must be a [Warn]" rule is a RED HERRING: it says
# error => prev is warn, NOT warn => next is error, so warns are not a suffix and
# searching for one returns garbage on a clean log full of warns. Use it as a
# POSTCONDITION (k == 0 or is_warn(logs[k-1])), never as the search key.
def first_error(logs):
    lo, hi = 0, len(logs)              # below lo: clean.  at/above hi: error.
    while lo < hi:
        mid = lo + (hi - lo) // 2      # in C# never (lo+hi)/2 -- it overflows
        if logs[mid].startswith("[Error]"): hi = mid
        else:                           lo = mid + 1
    return -1 if lo == len(logs) else lo
# On an in-memory list log n vs n is noise. It earns its keep when a read is a
# PAGE FETCH -- and then you usually do not know n, so you cannot compute a mid:
# GALLOP first. p'(i) = "error OR past-the-end" is monotone too (both are suffixes)
#   lo, hi = -1, 0                     # lo = -1 is the virtual always-false slot
#   while not p2(hi): lo, hi = hi, max(1, hi*2)
#   while hi - lo > 1: mid = lo + (hi-lo)//2; (hi if p2(mid) else lo) = mid
# O(log k) reads in the ANSWER, not the log size: 10^6 lines, error at 40 -> 14.
# THE HONEST CAVEAT: real multi-host logs are NOT a suffix (clock skew, late
# flushes). Binary search then returns *an* error, not *the first*, and says
# nothing about which. Ask; if the answer is "mostly", the linear scan is correct.

# PART 2. calls is caller -> callees; failure travels the OTHER WAY, so the blame
# graph is the TRANSPOSE. Reverse it, then BFS. Reversing wrong gives a confident
# answer listing the one set of services that is provably fine (the dependencies).
def impacted(calls, origins):          # multi-source: outages are rarely one node
    rev = defaultdict(list)
    for caller, callees in calls.items():
        for callee in callees: rev[callee].append(caller)   # LEAVES ARE NEVER KEYS
    hops, q = {s: 0 for s in origins}, deque(origins)       # in `calls` -- rev fixes it
    while q:
        s = q.popleft()
        for caller in rev.get(s, ()):
            if caller not in hops: hops[caller] = hops[s] + 1; q.append(caller)
    return hops                        # BFS over DFS: hop count free, no stack limit
# THE ASSUMPTION WORTH CHALLENGING: "every caller of a failed service fails" means
# zero resilience. Give a service two interchangeable backends and failure becomes
# a monotone AND/OR formula -- fails iff some dependency GROUP is entirely down --
# which is not a traversal. Evaluate to a LEAST FIXPOINT (repeat until no change);
# that is what refuses to conclude "A is down because B is down because A is down".
# Reachability is the special case where every group has exactly one member.

# PART 3. Longest chain from the origin = longest path in the blame graph. The
# textbook memoized DFS is CORRECT but its advertised O(V+E) is FALSE:
#       candidate = [v] + dfs(u)       # copies up to V cells, on each of E edges
# -> O(V*E) time, O(V^2) memo. Measured on a 200-node transitive tournament: 1.35M
# cells copied vs V+E = 20k. Memoize the LENGTH + a successor pointer instead and
# rebuild the single path at the end. Iterative, because 10^5-deep chains exist and
# .NET's StackOverflowException cannot even be caught.
def longest_chain(calls, origin):      # Kahn over the IMPACTED subgraph only
    rev = ...; hit = set(impacted(calls, [origin]))
    # in-degree inside `hit`; skip u == v (a self-call is recursion, not a cycle)
    deg = {v: sum(1 for u in hit for w in rev.get(u, ()) if w == v and u != v) for v in hit}
    depth, prev = {v: (1 if v == origin else 0) for v in hit}, {}
    q = deque(v for v in hit if deg[v] == 0)    # SEEDS ITSELF: the origin is the
    seen, best = 0, origin                      # unique zero-in-degree node here,
    while q:                                    # every other was reached THROUGH one
        v = q.popleft(); seen += 1
        if depth[v] > depth[best]: best = v
        for u in rev.get(v, ()):
            if u == v or u not in deg: continue
            if depth[v] + 1 > depth[u]: depth[u], prev[u] = depth[v] + 1, v
            deg[u] -= 1
            if deg[u] == 0: q.append(u)
    if seen != len(hit): raise ValueError("cycle")   # detection, free, same pass
    chain = []
    while best: chain.append(best); best = prev.get(best)
    return chain[::-1]
# CYCLES ARE REAL (payments <-> ledger is a Tuesday) and longest SIMPLE path on a
# general digraph is NP-hard, so there is no cleverer loop. Condense SCCs
# (Kosaraju: iterative DFS finish order on the blame graph, then DFS its transpose
# in reverse finish order), weight each component by |SCC|, run the SAME
# relaxation on the condensation. Be precise about the result: it is cascade
# DEPTH IN SERVICES and an UPPER BOUND on the longest simple path -- an SCC need
# not contain a Hamiltonian walk, so those services all fail without necessarily
# being orderable into one chain.
#
# WHICH NUMBER DOES ANYONE WANT? Blast radius (part 2) is what pages people; chain
# length is worst-case propagation DEPTH. A long chain can have a tiny radius. Say
# which you are answering. And neither one finds a ROOT CAUSE -- reachability tells
# you what could have propagated, and every service on the chain alerts; the
# usable signal is alert ORDER intersected with the reverse-reachable set, which is
# exactly why this problem has both halves.
#
# TEST WITH PROPERTIES, not examples: the impacted set closed under "calls an
# impacted service"; the chain made of real edges, distinct, starting at origin;
# on a DAG the SCC version's levels must all be singletons and total to the plain
# chain length. Cross-check against brute-forced simple paths on n <= 8 graphs --
# that is where a transpose-direction bug shows up and a hand-written example
# never will.

# CHEAPEST FLIGHTS WITHIN K STOPS (#787). "At most k stops" == at most k + 1
# FLIGHTS -- write that once, at the top. The example is the whole problem:
#   flights = [[0,1,100],[1,2,100],[2,0,100],[1,3,600],[2,3,200]], 0 -> 3
#   k = 1 -> 700 (0->1->3)      k = 2 -> 400 (0->1->2->3)
# The cheapest route and the shortest route are DIFFERENT routes, so no single
# number per city can answer both. The state is (city, flights used):
#   best[r][v] = min(best[r-1][v], min over u->v of best[r-1][u] + w)
def find_cheapest_price(n, flights, src, dst, k):
    best = [inf] * n; best[src] = 0
    for _ in range(k + 1):                  # k + 1 rounds == k + 1 flights
        previous = best[:]                  # THE SNAPSHOT. not optional.
        for frm, to, price in flights:
            best[to] = min(best[to], previous[frm] + price)
        if best == previous: break          # converged; a generous k is then free
    return -1 if best[dst] == inf else best[dst]
# THE TRAP: relaxing in place. One shared array lets a round read what the same
# round just wrote, so scanning 0->1 before 1->2 chains two flights in one round
# and the answer above becomes 400 at k = 1. It is not garbage -- it is a REAL
# route that overspends the budget, so it only ever under-reports, and whether it
# does depends on the ORDER of the flight list. That is why it survives testing.
#
# WHY PLAIN DIJKSTRA IS WRONG: "first pop is final" is a theorem about a graph
# whose only state is the node. Arriving at v cheaply in 3 hops does not dominate
# arriving expensively in 1 -- if the budget dies at v, only the expensive one can
# finish.   0-1->1-1->2-1->3  plus  0-5->2,  k = 1: answer is 0->2->3 = 6, and a
# visited-set Dijkstra settles city 2 at cost 2 and returns -1. Not a worse
# answer -- NO answer. The fix is DOMINANCE, not a bigger visited set: pop by
# cost, keep min_hops[v] over popped states, skip a state whose hops >= it.
#   heap of (cost, city, used); pop dst -> return; O(E*k log(E*k)), wins only on a
#   big sparse graph with generous k, where it stops as soon as dst surfaces.
# ITINERARY: two rows cannot rebuild it -- which LAYER settled a city is exactly
# what they discard. Keep the (k+2) x n table with from[r][v] = predecessor or
# CARRIED, then walk back alternating "step a layer" / "step a layer and a city".
# It always lands on src, since best[r][src] = 0 and every price is >= 1.
# FREE FACTS worth saying out loud: prices > 0 means an optimal route is SIMPLE,
# so k >= n-1 is a vacuous constraint (clamp it, run plain Dijkstra); the same
# rounds answer EVERY destination at once (one O(k*E) pass, O(1) per query);
# and the round-bounded form is the only version here that survives NEGATIVE
# prices, because a capped walk length cannot milk a negative cycle.
# Adding a second budget (total flight TIME) adds a dimension -- best[r][v][t] if
# it is small and discrete, otherwise Pareto labelling on (cost, time), of which
# the hop-dominance rule above is the one-dimensional case.
```
