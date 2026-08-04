# LeetCode 875 - Koko Eating Bananas
# Difficulty: Medium
# Pattern: Binary search on answer space
#
# Problem: Koko has piles of bananas and h hours. She eats at speed k
# bananas/hour (one pile per hour, leftover goes to next hour).
# Find the minimum k that lets her finish all piles within h hours.
#
# Approach: Binary search on k in range [1, max(piles)].
# For a given k, hours needed = sum(ceil(pile / k)).
# Find smallest k where hours_needed <= h.
#
# Time: O(n log m)  where m = max(piles)  Space: O(1)

from typing import List
import math

def min_eating_speed(piles: List[int], h: int) -> int:
    def hours_needed(k: int) -> int:
        return sum(math.ceil(pile / k) for pile in piles)

    lo, hi = 1, max(piles)
    while lo < hi:
        mid = (lo + hi) // 2
        if hours_needed(mid) <= h:
            hi = mid      # mid works, try slower
        else:
            lo = mid + 1  # too slow, need faster

    return lo


# ---- Tests ----
if __name__ == "__main__":
    assert min_eating_speed([3,6,7,11], 8) == 4
    assert min_eating_speed([30,11,23,4,20], 5) == 30
    assert min_eating_speed([30,11,23,4,20], 6) == 23
    print("All tests passed.")
