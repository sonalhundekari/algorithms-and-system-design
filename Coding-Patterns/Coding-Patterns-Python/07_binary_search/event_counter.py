# Event Stream Count in Time Range
# Difficulty: Medium (design)
# Pattern: one sorted timestamp list per key + two binary searches
#
# Problem: A stream delivers (event_type, timestamp) pairs with non-decreasing
# timestamps. Support:
#   receive(eventType, timestamp)                  -> None
#   count(eventType, startTime, endTime)           -> int, inclusive [start, end]
# Many event types share the stream; counts are per-type.
#
# The clarifying answers that pick this design:
#   1. Timestamps are non-decreasing across receive calls -> the per-type list
#      is ALREADY sorted, so a write is a plain append. No tree required.
#   2. Reads are range COUNTS, not value lookups -> we never need to touch the
#      events in the window, only find its two edges.
#
# Approach: bucket by type first, then binary search inside the bucket.
#
#   _events{}   event_type -> list of timestamps, ascending, append-only
#
# count(t, lo, hi) = upper_bound(times, hi) - lower_bound(times, lo)
#                    |                         |
#                    first index past hi       first index at or after lo
#
# The two bounds are the whole problem. lower_bound is bisect_left (>= lo) and
# upper_bound is bisect_right (> hi); pairing left-on-start with right-on-end is
# exactly what makes BOTH endpoints inclusive. Any other pairing silently drops
# or double-counts events that land on a boundary, which is the bug an
# interviewer is looking for -- see the boundary drill in the tests below.
#
# Both are written out by hand here rather than called from `bisect` because
# reproducing them is the point of the exercise; the tests cross-check every
# call against the stdlib to prove the hand-rolled versions agree.
#
# Time:  receive  O(1) amortized (append to a list that is already in order)
#        count    O(log n) with n = events of THAT type, independent of the
#                 total stream size and of the number of events in the window
# Space: O(n) total across all types -- 8 bytes per event, nothing per query
#
# This is the data-structure half of "Time Based Key-Value Store" (LC 981):
# same per-key sorted list, same binary search, but reduced to counting the
# window instead of retrieving the value at its right edge.

import bisect
from typing import Dict, List


class EventCounter:
    def __init__(self) -> None:
        self._events: Dict[str, List[int]] = {}
        self._last_timestamp: int | None = None

    # ---- binary search primitives (bisect_left / bisect_right, by hand) ----

    @staticmethod
    def _lower_bound(times: List[int], target: int) -> int:
        """First index i with times[i] >= target. Equivalent to bisect_left.

        Invariant: the answer always lives in [lo, hi]. `hi` starts at len(times)
        rather than len - 1 because "no such index" is a legal answer -- every
        timestamp being smaller than target must return len(times), not -1.
        """
        lo, hi = 0, len(times)
        while lo < hi:
            mid = (lo + hi) // 2
            if times[mid] < target:
                lo = mid + 1        # mid is too early, it cannot be the answer
            else:
                hi = mid            # mid qualifies, but something left of it might too
        return lo

    @staticmethod
    def _upper_bound(times: List[int], target: int) -> int:
        """First index i with times[i] > target. Equivalent to bisect_right.

        Identical to _lower_bound except for `<=`, which is what walks past a
        run of duplicates instead of stopping at its front.
        """
        lo, hi = 0, len(times)
        while lo < hi:
            mid = (lo + hi) // 2
            if times[mid] <= target:
                lo = mid + 1
            else:
                hi = mid
        return lo

    # ---- API ----

    def receive(self, eventType: str, timestamp: int) -> None:
        """Record one event. O(1) amortized while timestamps stay non-decreasing."""
        times = self._events.setdefault(eventType, [])

        # The fast path the problem's guarantee buys us: the new timestamp is
        # >= every timestamp already stored, so appending keeps the list sorted.
        if not times or timestamp >= times[-1]:
            times.append(timestamp)
        else:
            # The guarantee was violated. Splice it into place so count() stays
            # correct rather than silently returning garbage. This costs O(n)
            # per out-of-order arrival -- fine for the occasional straggler, not
            # a substitute for the real fix (see the follow-up notes at the end).
            bisect.insort(times, timestamp)

        self._last_timestamp = timestamp

    def count(self, eventType: str, startTime: int, endTime: int) -> int:
        """Events of `eventType` with startTime <= timestamp <= endTime. O(log n)."""
        if startTime > endTime:
            return 0                                  # empty window, not an error

        times = self._events.get(eventType)
        if not times:
            return 0                                  # type never seen

        first = self._lower_bound(times, startTime)   # first event at or after the start
        past = self._upper_bound(times, endTime)      # first event strictly after the end
        return past - first

    # ---- small conveniences, same structure ----

    def total(self, eventType: str) -> int:
        """All-time count for a type. O(1) -- no need to binary search for it."""
        return len(self._events.get(eventType, ()))

    def event_types(self) -> List[str]:
        return sorted(self._events)

    def __len__(self) -> int:
        return sum(len(times) for times in self._events.values())


if __name__ == "__main__":
    import random
    import time

    # -- the worked example --
    ec = EventCounter()
    ec.receive("login", 100)
    ec.receive("click", 110)
    ec.receive("login", 120)
    ec.receive("login", 200)

    assert ec.count("login", 100, 150) == 2
    assert ec.count("login", 100, 250) == 3
    assert ec.count("click", 0, 1000) == 1
    print(f"login in [100,150]: {ec.count('login', 100, 150)}  (expect 2)")
    print(f"login in [100,250]: {ec.count('login', 100, 250)}  (expect 3)")
    print(f"click in [0,1000]:  {ec.count('click', 0, 1000)}  (expect 1)")

    # -- edge cases --
    assert EventCounter().count("anything", 0, 10) == 0     # empty stream
    assert ec.count("logout", 0, 10_000) == 0               # type never seen
    assert ec.count("login", 250, 100) == 0                 # start > end
    assert ec.count("login", 0, 99) == 0                    # window entirely before
    assert ec.count("login", 201, 10_000) == 0              # window entirely after
    assert ec.count("login", 150, 150) == 0                 # empty point window
    assert ec.count("login", 120, 120) == 1                 # point window on an event
    assert ec.total("login") == 3 and len(ec) == 4
    assert ec.event_types() == ["click", "login"]

    # -- boundary drill: inclusive on BOTH ends is the thing to get right --
    # Duplicated timestamps make the left/right distinction visible: a window
    # touching 120 must take the whole run of 120s, from either side.
    dup = EventCounter()
    for t in [100, 120, 120, 120, 140]:
        dup.receive("e", t)

    assert dup.count("e", 120, 120) == 3        # the run alone
    assert dup.count("e", 100, 120) == 4        # left edge includes 100, right includes all 120s
    assert dup.count("e", 120, 140) == 4        # right edge includes 140
    assert dup.count("e", 101, 119) == 0        # strictly between two runs
    assert dup.count("e", 100, 140) == 5        # everything

    # And the same claim stated against the stdlib, which is what the hand-rolled
    # bounds are imitating.
    times = dup._events["e"]
    for lo in range(95, 146):
        for hi in range(95, 146):
            expected = sum(1 for t in times if lo <= t <= hi)
            assert dup.count("e", lo, hi) == expected, (lo, hi)
            if lo <= hi:
                assert (bisect.bisect_right(times, hi)
                        - bisect.bisect_left(times, lo)) == expected, (lo, hi)

    # A left/right mix-up is silent on distinct timestamps and wrong on ties --
    # bisect_left on both ends would report 0 here instead of 3.
    assert (bisect.bisect_left(times, 120) - bisect.bisect_left(times, 120)) == 0

    # -- randomized cross-check against brute force --
    random.seed(7)
    ec = EventCounter()
    truth: Dict[str, List[int]] = {}
    clock = 0
    for _ in range(5_000):
        clock += random.randrange(0, 3)             # non-decreasing, with ties
        kind = f"type{random.randrange(6)}"
        ec.receive(kind, clock)
        truth.setdefault(kind, []).append(clock)

    for _ in range(2_000):
        kind = f"type{random.randrange(8)}"         # includes types never sent
        lo, hi = random.randrange(-5, clock + 5), random.randrange(-5, clock + 5)
        expected = sum(1 for t in truth.get(kind, ()) if lo <= t <= hi)
        assert ec.count(kind, lo, hi) == expected, (kind, lo, hi)

    print(f"2,000 randomized queries over {len(ec):,} events agree with brute force")

    # -- out-of-order arrivals still answer correctly --
    messy = EventCounter()
    for t in [50, 10, 30, 30, 20, 90, 5]:
        messy.receive("e", t)
    assert messy._events["e"] == [5, 10, 20, 30, 30, 50, 90]
    assert messy.count("e", 20, 50) == 4
    assert messy.count("e", 0, 4) == 0

    # -- the point of the design: reads do not track the window size --
    big = EventCounter()
    for i in range(1_000_000):
        big.receive("hit" if i % 2 else "miss", i)

    start = time.perf_counter()
    total = sum(big.count("hit", i, i + 400_000) for i in range(0, 500_000, 500))
    elapsed = time.perf_counter() - start
    print(f"1,000 range queries over 1,000,000 events: {elapsed * 1000:.1f} ms "
          f"({total:,} events counted without visiting one of them)")

    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "What if timestamps were NOT monotonic?"
#     The insort fallback above keeps answers correct but degrades writes to
#     O(n). The real fix depends on how unordered the stream is:
#       - Bounded lateness (events arrive at most D behind): keep a small
#         reorder buffer, sort it, and flush to the append-only list once the
#         watermark passes. Writes stay amortized O(1).
#       - Genuinely random order: replace the list with an order-statistic
#         structure keyed by timestamp -- a balanced BST / skip list carrying
#         subtree counts, or a Fenwick tree over compressed timestamps. Both
#         give O(log n) insert AND O(log n) range count. In Python that is
#         `sortedcontainers.SortedList`, whose `bisect_left/right` have the
#         same signature as the ones here, so `count` is unchanged.
#
# "The stream never ends -- this grows without bound."
#     Counting queries almost always target a recent window, so age events out.
#     Because the list is sorted and append-only, expiry is a prefix drop:
#     `del times[:lower_bound(times, now - retention)]`, or a deque with
#     popleft(). If even the retained window is too large to store, switch from
#     exact counts to buckets: keep per-type counts in fixed time buckets
#     (say 1s) and answer a range as a prefix-sum difference over buckets --
#     O(1) reads, O(1) writes, memory proportional to TIME rather than events,
#     at the cost of approximation at the two partial edge buckets.
#
# "Millions of distinct event types."
#     Nothing changes structurally -- the dict shards cleanly by type, since no
#     query ever spans two types. That is also what makes this trivially
#     horizontal: hash the type to a node and the reads stay single-node.
#
# "Now give me the count of events across ALL types in a range."
#     A second list holding every timestamp (also append-only) answers it with
#     the same two binary searches, doubling the memory. Cheaper if types are
#     few: sum count() per type, O(T log n).
#
# "Make it a rate limiter -- how many in the last 60 seconds?"
#     count(type, now - 60, now). This structure is the exact backing store for
#     a sliding-window-log limiter, and the retention drop above is what keeps
#     it from leaking.
