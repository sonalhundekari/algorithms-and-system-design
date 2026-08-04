# LeetCode 49 - Group Anagrams
# Difficulty: Medium
# Pattern: HashMap with sort key
#
# Problem: Given an array of strings, group the anagrams together.
#
# Approach: Sort each word — anagrams have the same sorted form.
# Use sorted word as key, group original words by key.
#
# Time: O(n * k log k)  where k = max word length
# Space: O(n * k)

from typing import List
from collections import defaultdict

def group_anagrams(strs: List[str]) -> List[List[str]]:
    groups = defaultdict(list)
    for word in strs:
        key = tuple(sorted(word))  # e.g. "eat" -> ('a','e','t')
        groups[key].append(word)
    return list(groups.values())


# ---- Tests ----
if __name__ == "__main__":
    result = group_anagrams(["eat", "tea", "tan", "ate", "nat", "bat"])
    # Each group sorted for comparison
    result_sorted = sorted([sorted(g) for g in result])
    assert result_sorted == [["ate", "eat", "tea"], ["bat"], ["nat", "tan"]]
    assert group_anagrams([""]) == [[""]]
    assert group_anagrams(["a"]) == [["a"]]
    print("All tests passed.")
