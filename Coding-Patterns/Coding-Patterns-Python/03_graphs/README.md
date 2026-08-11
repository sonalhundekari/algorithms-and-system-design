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

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Number of Islands | #200 | Medium | DFS/BFS flood fill |
| Course Schedule | #207 | Medium | Topological sort / cycle detection |
| Word Ladder | #127 | Hard | BFS shortest path |
| Clone Graph | #133 | Medium | DFS/BFS + HashMap |
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
```
