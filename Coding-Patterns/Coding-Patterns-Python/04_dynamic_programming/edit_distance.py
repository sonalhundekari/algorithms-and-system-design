# LeetCode 72 - Edit Distance
# Difficulty: Medium
# Pattern: 2D DP
#
# Problem: Given two strings word1 and word2, return the minimum number
# of operations (insert, delete, replace) to convert word1 to word2.
#
# Approach: dp[i][j] = edit distance between word1[:i] and word2[:j].
# If chars match: dp[i][j] = dp[i-1][j-1]
# Else: dp[i][j] = 1 + min(dp[i-1][j],   # delete from word1
#                           dp[i][j-1],   # insert into word1
#                           dp[i-1][j-1]) # replace
#
# Time: O(m * n)  Space: O(m * n)

def min_distance(word1: str, word2: str) -> int:
    m, n = len(word1), len(word2)
    dp = [[0] * (n + 1) for _ in range(m + 1)]

    # Base cases: transforming to/from empty string
    for i in range(m + 1): dp[i][0] = i
    for j in range(n + 1): dp[0][j] = j

    for i in range(1, m + 1):
        for j in range(1, n + 1):
            if word1[i-1] == word2[j-1]:
                dp[i][j] = dp[i-1][j-1]
            else:
                dp[i][j] = 1 + min(dp[i-1][j], dp[i][j-1], dp[i-1][j-1])

    return dp[m][n]


# ---- Tests ----
if __name__ == "__main__":
    assert min_distance("horse", "ros") == 3
    assert min_distance("intention", "execution") == 5
    assert min_distance("", "abc") == 3
    assert min_distance("abc", "abc") == 0
    print("All tests passed.")
