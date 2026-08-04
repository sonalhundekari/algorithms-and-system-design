# LeetCode 3 - Longest Substring Without Repeating Characters
# Difficulty: Medium
# Pattern: Sliding window + HashSet
#
# Problem: Given a string s, find the length of the longest
# substring without repeating characters.
#
# Approach: Maintain a sliding window [left, right]. Expand right.
# When a duplicate is found, shrink from the left until no duplicate.
#
# Time: O(n)  Space: O(min(n, alphabet_size))

def length_of_longest_substring(s: str) -> int:
    char_set = set()
    left = 0
    max_len = 0

    for right in range(len(s)):
        while s[right] in char_set:
            char_set.remove(s[left])
            left += 1
        char_set.add(s[right])
        max_len = max(max_len, right - left + 1)

    return max_len


# ---- Tests ----
if __name__ == "__main__":
    assert length_of_longest_substring("abcabcbb") == 3  # "abc"
    assert length_of_longest_substring("bbbbb") == 1     # "b"
    assert length_of_longest_substring("pwwkew") == 3    # "wke"
    assert length_of_longest_substring("") == 0
    print("All tests passed.")
