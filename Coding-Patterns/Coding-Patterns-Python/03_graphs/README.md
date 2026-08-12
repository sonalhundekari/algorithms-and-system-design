# Pattern: Graphs

## Key Techniques
- **BFS** — shortest path in unweighted graph, level-by-level exploration
- **DFS** — connected components, cycle detection, topological sort
- **Topological Sort** — BFS variant (Kahn's algorithm) for DAG ordering
- **Visited set** — always track visited nodes to avoid cycles
- **Pick the decision level so a constraint becomes structural** — one queen per row is forced, so recursing on rows deletes the row constraint and turns "choose n of n^2 squares" into "choose a column n times"
- **Constraint keys, not board scans** — a conflict test is O(1) once each constraint has an index that is *constant along it* (`col`, `row - col`, `row + col`); mark on the way down, unmark on the way out
- **Bitmask the frontier** — when the constraint sets are all "which columns are blocked in this row", they collapse to three ints, and shifting them by one row is the diagonal
- **Halve the search by symmetry** — if a mirror/rotation maps solutions to solutions with no fixed points, explore one half and double, handling the self-symmetric case separately
- **A budget on the path is part of the STATE** — "cheapest within k stops" is not shortest-path over cities, it is shortest-path over `(city, hops used)`; Dijkstra's "first pop is final" is a theorem about the former and is flatly false about the latter
- **Bellman-Ford by ROUNDS, relaxed against a snapshot** — after round `r`, `dist[v]` is the cheapest cost using at most `r` edges; reading the *previous* round's list is the only thing stopping one round from chaining an entire path, and relaxing in place fails by returning a real route that overspends the budget
- **Dominance, not a visited set** — when the state has a second dimension, discard a popped state only if an already-popped one beat it on *both* (cheaper AND no more hops); that is the 1-D case of Pareto labelling, which is where the multi-budget follow-up goes
- **A hop bound makes negative cycles harmless** — round-bounded Bellman-Ford stays correct with negative weights because the walk length is capped; every Dijkstra variant dies. Know which of your solutions a sign change kills
- **Positive weights ⇒ optimal path is simple ⇒ a budget past n − 1 is vacuous** — clamp `k` and the constrained problem collapses back to plain Dijkstra

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Number of Islands | #200 | Medium | DFS/BFS flood fill |
| Course Schedule | #207 | Medium | Topological sort / cycle detection |
| Word Ladder | #127 | Hard | BFS shortest path |
| Clone Graph | #133 | Medium | DFS/BFS + HashMap |
| N-Queens / N-Queens II | #51/#52 | Hard | Row-by-row backtracking with column & diagonal sets; bitmask + mirror symmetry for the count |
| Wiki Shortest Clicks / crawl-all | #1971-like | Easy-Medium (Hard follow-up) | BFS over an API-only graph; visited-on-enqueue; threaded crawl with atomic dedupe + in-flight termination |
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
# nodes, not 40320.

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
# THE TRAP: relaxing in place. One shared list lets a round read what the same
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
