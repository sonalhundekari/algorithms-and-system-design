# LeetCode 76 - Minimum Window Substring
# Difficulty: Hard
# Pattern: Sliding window + frequency map
#
# Problem: Given strings s and t, return the minimum window substring
# of s that contains all characters in t. Return "" if none exists.
#
# Approach:
#   1. Build frequency map for t (need).
#   2. Expand right pointer, adding chars to window map.
#   3. When all required chars are satisfied (formed == required),
#      try to shrink from the left to minimize window.
#   4. Track the minimum valid window found.
#
# Time: O(|s| + |t|)  Space: O(|s| + |t|)

from collections import Counter

def min_window(s: str, t: str) -> str:
    if not t or not s:
        return ""

    need = Counter(t)
    required = len(need)   # distinct chars needed
    formed = 0             # distinct chars currently satisfied in window
    window = {}

    left = 0
    min_len = float('inf')
    result = ""

    for right in range(len(s)):
        c = s[right]
        window[c] = window.get(c, 0) + 1
        if c in need and window[c] == need[c]:
            formed += 1

        while formed == required:
            # Update result
            if right - left + 1 < min_len:
                min_len = right - left + 1
                result = s[left:right + 1]
            # Shrink from left
            lc = s[left]
            window[lc] -= 1
            if lc in need and window[lc] < need[lc]:
                formed -= 1
            left += 1

    return result


# ---- Tests ----
if __name__ == "__main__":
    assert min_window("ADOBECODEBANC", "ABC") == "BANC"
    assert min_window("a", "a") == "a"
    assert min_window("a", "aa") == ""
    print("All tests passed.")
