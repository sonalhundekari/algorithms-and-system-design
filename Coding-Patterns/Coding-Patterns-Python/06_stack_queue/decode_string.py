# LeetCode 394 - Decode String
# Difficulty: Medium
# Pattern: Stack of pending frames
#
# Problem: Decode a string of the form k[encoded_string], where the substring
# inside the brackets repeats k times. k may have multiple digits and the
# encodings nest. Input is guaranteed valid; the plain text contains no digits.
#
#   3[a]2[bc]      -> aaabcbc
#   3[a2[c]]       -> accaccacc
#   2[abc]3[cd]ef  -> abcabccdcdcdef
#
# Approach: One left-to-right pass with two stacks. `current` accumulates the
# segment being built at the present nesting depth.
#   digit -> fold into k (k = k * 10 + d handles multi-digit counts)
#   '['   -> a new depth begins: park k and `current`, start a fresh buffer
#   ']'   -> the depth ends: pop the parent buffer and append the just-finished
#            segment to it k times
# The stacks hold exactly one entry per open bracket, so they are O(depth).
#
# Time: O(n + m)  Space: O(m)   [n = input length, m = decoded length]
#
# Follow-up (decoded string too large to hold in memory): see decode_streaming.

from itertools import islice
from typing import Iterator


def decode_string(s: str) -> str:
    counts = []       # repetition count per open bracket
    parents = []      # parent buffer per open bracket
    current = []      # segment being built at the current depth
    k = 0

    for c in s:
        if c.isdigit():
            k = k * 10 + int(c)
        elif c == "[":
            counts.append(k)
            parents.append(current)
            k = 0
            current = []
        elif c == "]":
            segment = current
            current = parents.pop()
            current.append("".join(segment) * counts.pop())
        else:
            current.append(c)

    return "".join(current)


# ---- Follow-up: bounded extra memory ----
#
# decode_string costs O(m) because it materializes the answer, and worse, an
# inner segment gets copied again at every enclosing level. If the caller only
# needs to consume the output once (write to a socket, hash it, scan for a
# pattern), it never has to exist in memory at all.
#
# Instead of building text, re-walk the input: keep a stack of
# (content_start, remaining) and, on ']', jump the cursor back to content_start
# until the count is exhausted. The input string is the only buffer, so extra
# memory is O(depth) -- independent of the decoded length.
#
# Time: O(m)  Space: O(depth)
def decode_streaming(s: str) -> Iterator[str]:
    frames = []  # (content_start, remaining)
    i = 0

    while i < len(s):
        c = s[i]

        if c.isdigit():
            k = 0
            while s[i].isdigit():
                k = k * 10 + int(s[i])
                i += 1

            # s[i] is now '[', so the repeated content starts at i + 1.
            frames.append((i + 1, k))
            i += 1
        elif c == "]":
            start, remaining = frames.pop()
            remaining -= 1

            if remaining > 0:
                # Another pass over the same span: rewind rather than copy.
                frames.append((start, remaining))
                i = start
            else:
                i += 1
        else:
            yield c
            i += 1


# ---- Tests ----
if __name__ == "__main__":
    assert decode_string("3[a]2[bc]") == "aaabcbc"
    assert decode_string("3[a2[c]]") == "accaccacc"
    assert decode_string("2[abc]3[cd]ef") == "abcabccdcdcdef"

    assert decode_string("abc") == "abc"           # no encoding at all
    assert decode_string("12[a]") == "a" * 12      # multi-digit count
    assert decode_string("2[2[2[ab]]]") == "ab" * 8
    assert decode_string("x2[y3[z]]w") == "xyzzzyzzzw"

    # The streaming decoder must agree with the materializing one.
    for case in ["3[a]2[bc]", "3[a2[c]]", "2[abc]3[cd]ef", "abc", "2[2[2[ab]]]", "x2[y3[z]]w"]:
        assert "".join(decode_streaming(case)) == decode_string(case), case

    # ...but it never holds the output: this expands to 10^9 characters, and
    # taking the first 10 touches only a couple of stack frames.
    assert "".join(islice(decode_streaming("1000000[10000[ab]]"), 10)) == "ababababab"

    print("All tests passed.")
