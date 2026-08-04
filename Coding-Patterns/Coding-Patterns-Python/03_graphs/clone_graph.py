# LeetCode 133 - Clone Graph
# Difficulty: Medium
# Pattern: DFS/BFS + HashMap
#
# Problem: Given a node in a connected undirected graph,
# return a deep copy of the graph.
#
# Approach: DFS with a visited map: original_node -> cloned_node.
# Before recursing into neighbors, create the clone and store it in the map.
#
# Time: O(V + E)  Space: O(V)

from typing import Optional

class Node:
    def __init__(self, val=0, neighbors=None):
        self.val = val
        self.neighbors = neighbors if neighbors is not None else []

def clone_graph(node: Optional['Node']) -> Optional['Node']:
    if not node:
        return None

    visited = {}  # original -> clone

    def dfs(n):
        if n in visited:
            return visited[n]
        clone = Node(n.val)
        visited[n] = clone
        for neighbor in n.neighbors:
            clone.neighbors.append(dfs(neighbor))
        return clone

    return dfs(node)
