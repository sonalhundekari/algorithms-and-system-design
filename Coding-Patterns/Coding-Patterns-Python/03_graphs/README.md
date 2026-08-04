# Pattern: Graphs

## Key Techniques
- **BFS** — shortest path in unweighted graph, level-by-level exploration
- **DFS** — connected components, cycle detection, topological sort
- **Topological Sort** — BFS variant (Kahn's algorithm) for DAG ordering
- **Visited set** — always track visited nodes to avoid cycles

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Number of Islands | #200 | Medium | DFS/BFS flood fill |
| Course Schedule | #207 | Medium | Topological sort / cycle detection |
| Word Ladder | #127 | Hard | BFS shortest path |
| Clone Graph | #133 | Medium | DFS/BFS + HashMap |

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
```
