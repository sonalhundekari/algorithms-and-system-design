# LeetCode 202 - Happy Number
# Difficulty: Easy
# Pattern: Fast/slow pointers (Floyd's cycle detection) on an implicit list
#
# Problem: Repeatedly replace n with the sum of the squares of its digits.
# n is "happy" if that process reaches 1; otherwise it falls into a cycle
# that never contains 1. Return whether n is happy.
#
# Key insight: `next_number` is a function, so each n has exactly one
# successor -- the sequence is a linked list whose nodes are numbers and
# whose "pointer" is next_number(). The sequence is also bounded (any number
# below 1000 maps below 250, so it can never run off to infinity), which
# means it MUST eventually repeat. So this is just cycle detection: run a
# slow and a fast pointer; if they meet at 1 the number is happy, and if
# they meet anywhere else we found the unhappy cycle.
#
# The cycle every unhappy number falls into is always the same one:
# 4 -> 16 -> 37 -> 58 -> 89 -> 145 -> 42 -> 20 -> 4
#
# Time: O(log n)  Space: O(1)


def next_number(n: int) -> int:
    """Sum of the squares of n's digits."""
    total = 0
    while n > 0:
        n, digit = divmod(n, 10)
        total += digit * digit
    return total


def is_happy(n: int) -> bool:
    slow, fast = n, next_number(n)
    # Stop when fast reaches 1 (happy) or the pointers meet (cycle).
    while fast != 1 and slow != fast:
        slow = next_number(slow)
        fast = next_number(next_number(fast))
    return fast == 1


# Hash-set version (bonus): easier to reach for in an interview, but it
# stores every number seen instead of running in constant space.
def is_happy_seen(n: int) -> bool:
    seen = set()
    while n != 1 and n not in seen:
        seen.add(n)
        n = next_number(n)
    return n == 1


# ---- Tests ----
if __name__ == "__main__":
    # The 20 happy numbers in [1, 100].
    happy_under_100 = {1, 7, 10, 13, 19, 23, 28, 31, 32, 44,
                       49, 68, 70, 79, 82, 86, 91, 94, 97, 100}

    assert is_happy(19) is True    # 19 -> 82 -> 68 -> 100 -> 1
    assert is_happy(2) is False    # falls into the 4 -> 16 -> ... -> 4 cycle
    assert is_happy(1) is True     # already there
    assert is_happy(7) is True
    assert is_happy(116) is False  # 116 -> 38 -> 73 -> 58 -> (cycle)

    for i in range(1, 101):
        expected = i in happy_under_100
        assert is_happy(i) is expected, f"is_happy({i}) should be {expected}"
        assert is_happy_seen(i) is expected, f"is_happy_seen({i}) should be {expected}"

    # Both approaches agree on a wider range.
    assert all(is_happy(i) == is_happy_seen(i) for i in range(1, 5000))

    print("19 ->", is_happy(19))
    print(" 2 ->", is_happy(2))
    print("All tests passed.")
