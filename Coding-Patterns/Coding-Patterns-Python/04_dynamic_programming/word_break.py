# LeetCode 139 - Word Break
# Difficulty: Medium
# Pattern: 1D DP + set lookup
#
# Problem: Given a string s and a dictionary wordDict, return True if
# s can be segmented into space-separated words from the dictionary.
#
# Approach: dp[i] = True if s[:i] can be segmented.
# For each i, try all j < i: if dp[j] and s[j:i] in wordDict -> dp[i] = True.
#
# Time: O(n^2)  Space: O(n)

from typing import List

def word_break(s: str, word_dict: List[str]) -> bool:
    word_set = set(word_dict)
    n = len(s)
    dp = [False] * (n + 1)
    dp[0] = True  # empty string is always valid

    for i in range(1, n + 1):
        for j in range(i):
            if dp[j] and s[j:i] in word_set:
                dp[i] = True
                break

    return dp[n]


# ---- Tests ----
if __name__ == "__main__":
    assert word_break("leetcode", ["leet", "code"]) == True
    assert word_break("applepenapple", ["apple", "pen"]) == True
    assert word_break("catsandog", ["cats", "dog", "sand", "and", "cat"]) == False
    print("All tests passed.")
