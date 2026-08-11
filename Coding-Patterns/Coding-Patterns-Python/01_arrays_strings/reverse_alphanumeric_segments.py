# Reverse Alphanumeric Segments (LeetCode 917 variant)
# Difficulty: Easy/Medium
# Pattern: Two pointers, scoped to a segment
#
# Problem: Reverse each maximal run of alphanumeric characters ([A-Za-z0-9]).
# Every other character -- spaces, punctuation, apostrophes -- stays at its
# original index.
#
#   "Hello! 2026!!! Let's dance!!!"  ->  "olleH! 6202!!! teL's ecnad!!!"
#
# The apostrophe is the whole problem in miniature. It is not alphanumeric, so
# it *splits* the run: "Let's" is two segments, "Let" and "s", reversed
# independently -> "teL" + "'" + "s". It is NOT "s'teL".
#
# That split is the difference from LeetCode 917 (Reverse Only Letters), which
# looks nearly identical but is a different operation. There the non-letters are
# holes that letters *pass through* -- the letter sequence reverses as one global
# list, so "ab-cd" -> "dc-ba" and the 'a' teleports across the dash. Here
# non-alphanumerics are walls: "ab-cd" -> "ba-dc", nothing crosses one.
#
# Approach: scan; skip fixed points; on a run, find its end, reverse in place
# with two pointers, resume past it. Segments are disjoint and each reverse is
# confined to its own [i, j), so no write can land outside the run that produced
# it -- the "separators keep their index" rule holds structurally.
#
# Time:  O(n)  -- each index is found once by the scan and swapped at most once
# Space: O(n)  -- only because Python strings are immutable; the algorithm is O(1)
#
# Definition note: "alphanumeric" here is ASCII letters and digits. Python's
# str.isalnum() is Unicode-aware and would also accept 'e' or Arabic-Indic
# digits -- a superset. The explicit ASCII check keeps to the stated contract.


def reverse_alphanumeric_segments(s: str) -> str:
    chars = list(s)
    n = len(chars)
    i = 0

    while i < n:
        if not _is_alnum(chars[i]):
            i += 1  # fixed point: stays exactly where it is
            continue

        # [i, j) is the maximal alphanumeric run starting at i.
        j = i
        while j < n and _is_alnum(chars[j]):
            j += 1

        # Two pointers inward, confined to this run.
        left, right = i, j - 1
        while left < right:
            chars[left], chars[right] = chars[right], chars[left]
            left += 1
            right -= 1

        i = j  # resume past the run, never re-entering it

    return "".join(chars)


def _is_alnum(c: str) -> bool:
    return ("a" <= c <= "z") or ("A" <= c <= "Z") or ("0" <= c <= "9")


# ---- Tests ----
if __name__ == "__main__":
    cases = [
        # (input, expected, note)
        ("Hello! 2026!!! Let's dance!!!", "olleH! 6202!!! teL's ecnad!!!",
         "the worked example -- note teL's, not s'teL"),
        ("ab-cd", "ba-dc",
         "LeetCode 917 would say dc-ba; separators are walls, not holes"),
        ("Let's", "teL's", "apostrophe splits the run into 'Let' and 's'"),
        ("2026", "6202", "digits are alphanumeric too"),
        ("abc123", "321cba", "letters and digits in one run reverse together"),
        ("", "", "empty string"),
        ("!!!", "!!!", "all separators -- nothing to reverse"),
        ("racecar", "racecar", "single run, palindrome"),
        ("a", "a", "single character"),
        ("  hi  ", "  ih  ", "leading/trailing separators keep their indices"),
        ("a1!b2?c3", "1a!2b?3c", "many tiny runs"),
        ("...abc", "...cba", "run at the very end"),
        ("abc...", "cba...", "run at the very start"),
    ]

    for text, expected, note in cases:
        actual = reverse_alphanumeric_segments(text)
        assert actual == expected, f"{text!r}: got {actual!r}, want {expected!r}"

        # Separators must be fixed points -- check positionally, not by eye.
        for k, ch in enumerate(text):
            assert _is_alnum(ch) or actual[k] == ch, \
                f"{text!r}: separator {ch!r} drifted at index {k}"

        print(f"{text!r:35} -> {actual!r:35}  # {note}")

    # Reversing twice restores the original: each segment reverse is an involution
    # and the segment boundaries are unchanged by the first pass.
    for text, _, _ in cases:
        once = reverse_alphanumeric_segments(text)
        assert reverse_alphanumeric_segments(once) == text, f"not an involution: {text!r}"

    print("\nAll tests passed.")
