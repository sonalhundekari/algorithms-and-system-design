# LeetCode 200 - Number of Islands
# Difficulty: Medium
# Pattern: DFS flood fill
#
# Problem: Given a 2D grid of '1's (land) and '0's (water),
# count the number of islands.
#
# Approach: For each unvisited land cell ('1'), run DFS and mark
# all connected land cells as visited ('0'). Count DFS calls.
#
# Time: O(m * n)  Space: O(m * n) recursive stack

from typing import List

def num_islands(grid: List[List[str]]) -> int:
    if not grid:
        return 0

    rows, cols = len(grid), len(grid[0])

    def dfs(r, c):
        if r < 0 or r >= rows or c < 0 or c >= cols or grid[r][c] != '1':
            return
        grid[r][c] = '0'  # mark visited
        dfs(r + 1, c)
        dfs(r - 1, c)
        dfs(r, c + 1)
        dfs(r, c - 1)

    count = 0
    for r in range(rows):
        for c in range(cols):
            if grid[r][c] == '1':
                dfs(r, c)
                count += 1
    return count


# ---- Tests ----
if __name__ == "__main__":
    grid1 = [
        ["1","1","1","1","0"],
        ["1","1","0","1","0"],
        ["1","1","0","0","0"],
        ["0","0","0","0","0"]
    ]
    assert num_islands(grid1) == 1

    grid2 = [
        ["1","1","0","0","0"],
        ["1","1","0","0","0"],
        ["0","0","1","0","0"],
        ["0","0","0","1","1"]
    ]
    assert num_islands(grid2) == 3
    print("All tests passed.")
