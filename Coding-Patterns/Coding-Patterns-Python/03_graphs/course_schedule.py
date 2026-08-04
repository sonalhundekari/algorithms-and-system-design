# LeetCode 207 - Course Schedule
# Difficulty: Medium
# Pattern: Topological sort / cycle detection (Kahn's BFS)
#
# Problem: There are n courses. Given prerequisites [a, b] meaning
# you must take course b before a, return True if you can finish all courses.
#
# Approach: Build directed graph + in-degree array.
# BFS from all nodes with in_degree=0. If we process all n nodes, no cycle exists.
#
# Time: O(V + E)  Space: O(V + E)

from typing import List
from collections import deque

def can_finish(num_courses: int, prerequisites: List[List[int]]) -> bool:
    in_degree = [0] * num_courses
    adj = [[] for _ in range(num_courses)]

    for course, prereq in prerequisites:
        adj[prereq].append(course)
        in_degree[course] += 1

    queue = deque(i for i in range(num_courses) if in_degree[i] == 0)
    completed = 0

    while queue:
        course = queue.popleft()
        completed += 1
        for next_course in adj[course]:
            in_degree[next_course] -= 1
            if in_degree[next_course] == 0:
                queue.append(next_course)

    return completed == num_courses


# ---- Tests ----
if __name__ == "__main__":
    assert can_finish(2, [[1, 0]]) == True         # 0 -> 1, no cycle
    assert can_finish(2, [[1, 0], [0, 1]]) == False # cycle
    assert can_finish(3, [[1,0],[2,1],[3,2] if False else [2,0]]) == True
    print("All tests passed.")
