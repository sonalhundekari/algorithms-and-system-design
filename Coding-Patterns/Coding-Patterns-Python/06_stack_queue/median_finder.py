# LeetCode 295 - Find Median from Data Stream
# Difficulty: Hard
# Pattern: Two heaps split at the median
#
# Problem: addNum(num) into a growing stream, findMedian() at any time.
#
# Insight: the median depends on ONE position in the sorted order, so don't
# maintain the order — maintain the SPLIT. A max-heap of the smaller half and a
# min-heap of the larger half put both candidate middles at a root.
#
#     ... 1  3  4  |  8  9  12 ...
#              ^      ^
#         -lo[0]      hi[0]
#
# Invariants:  every lo value <= every hi value;  len(lo) - len(hi) in {0, 1}.
#
# The add pushes through the FAR side every time (into lo, out of lo, into hi),
# so the first invariant holds by construction — what leaves lo is lo's maximum,
# so it is >= everything left behind. No comparison to get backwards, no
# empty-heap special case.
#
# heapq is a min-heap only, so lo stores NEGATED values to act as a max-heap.
#
# Time: addNum O(log n), findMedian O(1)   Space: O(n)

import heapq


class MedianFinder:
    def __init__(self):
        self.lo = []  # max-heap of the smaller half, stored negated
        self.hi = []  # min-heap of the larger half

    def addNum(self, num: int) -> None:
        heapq.heappush(self.lo, -num)
        heapq.heappush(self.hi, -heapq.heappop(self.lo))  # lo's max moves to hi
        if len(self.hi) > len(self.lo):                   # lo holds the odd one out
            heapq.heappush(self.lo, -heapq.heappop(self.hi))

    def findMedian(self) -> float:
        if not self.lo:
            raise ValueError("median of an empty stream is undefined")
        if len(self.lo) > len(self.hi):
            return float(-self.lo[0])
        return (-self.lo[0] + self.hi[0]) / 2.0


# ---- Tests ----
if __name__ == "__main__":
    import bisect
    import random

    mf = MedianFinder()
    mf.addNum(1)
    mf.addNum(2)
    assert mf.findMedian() == 1.5      # even count -> mean of the two middles
    mf.addNum(3)
    assert mf.findMedian() == 2.0      # odd count -> the middle itself

    # Duplicates, negatives, and a singleton are all ordinary cases.
    for values, expected in [
        ([7], 7.0),
        ([5, 5, 5, 5], 5.0),
        ([-3, -2, -1], -2.0),
        ([-5, 5], 0.0),
        ([1, 2, 3, 4], 2.5),
    ]:
        finder = MedianFinder()
        for v in values:
            finder.addNum(v)
        assert finder.findMedian() == expected, values

    # Cross-check against a sorted list after EVERY add — the intermediate states
    # are where an off-by-one in the size rule hides, not the final answer.
    random.seed(295)
    for _ in range(300):
        finder, sorted_view = MedianFinder(), []
        for _ in range(random.randint(1, 60)):
            num = random.randint(-6, 6)   # tiny range: make ties the common case
            finder.addNum(num)
            bisect.insort(sorted_view, num)

            n = len(sorted_view)
            expected = (
                float(sorted_view[n // 2])
                if n % 2
                else (sorted_view[n // 2 - 1] + sorted_view[n // 2]) / 2.0
            )
            assert finder.findMedian() == expected
            assert -finder.lo[0] <= (finder.hi[0] if finder.hi else -finder.lo[0])
            assert 0 <= len(finder.lo) - len(finder.hi) <= 1

    try:
        MedianFinder().findMedian()
        raise AssertionError("empty stream should raise")
    except ValueError:
        pass  # unspecified by the prompt, so pick a behaviour and say so

    print("All tests passed.")

# ---- Follow-ups ----
#
# "All numbers are in [0, 100]."  Drop the heaps: counts = [0] * 101 makes
# addNum O(1) and the median a scan of 101 buckets. A Fenwick tree over the
# domain generalises it to O(log U) with removal and arbitrary percentiles —
# see MedianFinder.cs for that version. Note the stated limits (-1e5..1e5)
# already permit it: 200,001 buckets is 800 KB regardless of stream length.
#
# "Support removeNum."  A heap can only address its root, so use lazy deletion
# (a `delayed` counter per value, logical sizes tracked separately, corpses
# dropped when they surface) or the Fenwick tree. Sliding-window median
# (LeetCode 480) is the same problem: the window forces the removal.
#
# "A billion numbers, bounded memory."  Exact medians need O(n) — the median is
# not summarizable, since any value can still turn out to be the middle one.
# Either bound the domain (counts: memory independent of n) or accept
# approximation: t-digest / P^2 answer any quantile from a few hundred bytes.
