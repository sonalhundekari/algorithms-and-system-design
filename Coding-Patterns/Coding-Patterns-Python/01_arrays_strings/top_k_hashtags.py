# Top-K Search Terms / Hashtags, Deduped Per User
# Difficulty: Medium
# Pattern: term -> set<user> (NOT a counter) + a size-K heap whose comparator is
#          the tie-break run backwards
#
#     def topKHashtags(events: list[list[str]], k: int) -> list[str]: ...
#
# Each event is [user_id, hashtag]. A hashtag's POPULARITY is the number of
# DISTINCT users who searched it -- one user searching #python fifty times is
# worth exactly one. Return the k most popular, sorted by descending popularity,
# then by hashtag ascending (lexicographic) to break ties.
#
# THE ONE THING THIS PROBLEM IS ABOUT. It looks exactly like LC 692 (top-K
# frequent words) and the muscle memory -- Counter + heap of counts -- is wrong:
#
#     counts[tag] += 1          # WRONG: one user spamming a tag inflates it
#     users[tag].add(user_id)   # RIGHT: popularity == len(users[tag])
#
# That single line is the whole difference, and it is the thing being tested.
# Say the word "distinct" back to the interviewer before writing anything, then
# write the set. Everything after it is standard top-K machinery.
#
#     events = [["u1","#a"], ["u1","#a"], ["u1","#a"], ["u2","#b"], ["u3","#b"]]
#     counter says  #a: 3, #b: 2   -> ["#a", "#b"]     wrong
#     sets say      #a: 1, #b: 2   -> ["#b", "#a"]     right
#
# THE SECOND TRAP: A SIZE-K MIN-HEAP RUNS THE TIE-BREAK BACKWARDS.
# The heap holds the K BEST seen so far, so its root -- the thing heapq hands you
# and the thing you evict -- must be the WORST of them. Invert both keys:
#
#     best  = highest count, then SMALLEST tag
#     worst = lowest  count, then LARGEST  tag       <- the heap's ordering
#
#     heappush(heap, (count, tag))     # WRONG on ties, and silently so
#
# is the bug, because at equal counts it evicts the lexicographically SMALLEST
# tag -- precisely the one the tie-break says to keep. Ties on the K'th boundary
# are rare in a random test and universal in an adversarial one. Only the count
# half is invertible with a minus sign; strings have no negation, so the
# comparator has to be spelled out (see `_Worst` below).
#
# COMPLEXITY, which is explicitly part of the signal here. E events, T distinct
# terms, U distinct users.
#
#     ingest    O(E) expected, O(1) per event         -- one set insert
#     top_k     O(T log K) with the size-K heap, + O(K log K) to order the output
#               O(T log T) if you just sort everything
#     space     O(E) worst case: sum of set sizes is the number of (user, term)
#               pairs, which is min(E, T x U)
#
# The heap only wins for K << T, and log K vs log T is a small constant apart in
# practice -- so say the sort out loud as the default and reach for the heap when
# K is genuinely small. What you must NOT do is claim O(T) for the heap version.
#
# EDGE CASES, all of them one line each:
#     k >= T                 -> every term, still fully sorted
#     k <= 0                 -> [].  Watch `ranked[:k]` -- a negative k SLICES
#                               FROM THE END and returns garbage instead of []
#     no events              -> []
#     one user, many terms   -> every count is 1, so the answer is purely
#                               lexicographic; the strongest tie-break test there is
#
# Time:  see above.
# Space: O(distinct (user, term) pairs).

import heapq
import math
import random
from collections import defaultdict
from hashlib import blake2b


# ---------------------------------------------------------------------------
# Part 1 -- the interview answer
# ---------------------------------------------------------------------------

def topKHashtags(events, k):
    """Batch form. O(E + T log T); the sort is the honest default."""
    users = defaultdict(set)
    for user_id, hashtag in events:
        users[hashtag].add(user_id)

    ranked = sorted(users, key=lambda tag: (-len(users[tag]), tag))
    return ranked[:k] if k > 0 else []          # NOT ranked[:k] -- negative k slices


class _Worst:
    """Heap entry ordered so that heapq's minimum is the element to EVICT.

    Worst = fewest distinct users; among equal counts, the lexicographically
    LATER tag, because the tie-break keeps the earlier one. The count inverts
    with a minus sign, the tag cannot -- which is the whole reason this is a
    class and not a tuple."""

    __slots__ = ("count", "tag")

    def __init__(self, count, tag):
        self.count = count
        self.tag = tag

    def __lt__(self, other):
        if self.count != other.count:
            return self.count < other.count
        return self.tag > other.tag             # inverted on purpose


def top_k_heap(counts, k):
    """Top k from a term -> distinct-user-count mapping, in O(T log K).

    Keeps a min-heap of size k: push while it is short, and once it is full
    replace the root whenever the candidate beats it."""
    if k <= 0:
        return []

    heap = []
    for tag, count in counts.items():
        entry = _Worst(count, tag)
        if len(heap) < k:
            heapq.heappush(heap, entry)
        elif heap[0] < entry:                   # candidate beats the worst kept
            heapq.heapreplace(heap, entry)

    # The heap is only partially ordered, so the K survivors still need sorting.
    return [e.tag for e in sorted(heap, key=lambda e: (-e.count, e.tag))]


# ---------------------------------------------------------------------------
# Part 2 -- the streaming version, which is what the follow-up asks for
# ---------------------------------------------------------------------------

class ExactUsers:
    """The default per-term structure: a plain set, with add() reporting whether
    the value was NEW. That boolean is the entire interface the stream needs, and
    it is the one question a HyperLogLog can also answer in O(1) -- which is why
    the sketch swap in Part 3 is a one-word change."""

    __slots__ = ("_users",)

    def __init__(self):
        self._users = set()

    def add(self, value):
        before = len(self._users)
        self._users.add(value)
        return len(self._users) != before

    def __len__(self):
        return len(self._users)


class TopKSearchTerms:
    """Events arrive one at a time; top_k(k) may be called at any moment.

    Two levels of state:
        _users[tag]   membership structure  -- absorbs repeats, O(1) per event
        _counts[tag]  int                   -- its cardinality, refreshed lazily
        _stale        tags whose cached count is behind

    Why cache a count that `len()` already answers: with exact sets it buys
    nothing -- len is a stored field. It becomes load-bearing the moment the
    membership structure's cardinality query stops being free, which is exactly
    what a HyperLogLog does: estimating is an O(registers) pass over 16K bytes.
    Call len() per event there and ingest is 16,000x slower than it should be;
    call it per term per top_k and every query pays for terms that never changed.
    So counting is decoupled from membership: a new user marks the term stale, and
    top_k re-estimates only the terms that actually moved. With exact sets the
    refresh is O(1) per stale term and the whole mechanism costs nothing.

    Keeping counts in their own flat int map also means top_k walks ints instead
    of chasing T set objects, and it is the only piece a sharded or remote version
    has to ship around."""

    def __init__(self, sketch_factory=ExactUsers):
        self._users = defaultdict(sketch_factory)
        self._counts = {}
        self._stale = set()

    def record(self, user_id, term):
        """One (user, term) event. O(1) expected -- no cardinality query here."""
        if self._users[term].add(user_id):      # only a NEW user moves the count
            self._stale.add(term)

    def record_all(self, events):
        for user_id, term in events:
            self.record(user_id, term)

    def _refresh(self):
        """Bring the cached counts up to date. O(stale terms) x the cost of one
        cardinality query -- free for sets, one estimate per changed term for HLL."""
        for term in self._stale:
            self._counts[term] = len(self._users[term])
        self._stale.clear()

    def popularity(self, term):
        self._refresh()
        return self._counts.get(term, 0)

    def top_k(self, k):
        """O(T log K + K log K). Ties broken by tag ascending."""
        self._refresh()
        return top_k_heap(self._counts, k)

    def top_k_sorted(self, k):
        """O(T log T). Simpler, and faster than the heap once K approaches T."""
        if k <= 0:
            return []
        self._refresh()
        ranked = sorted(self._counts, key=lambda tag: (-self._counts[tag], tag))
        return ranked[:k]

    def top_k_buckets(self, k):
        """O(T + U + B log B) where B is the size of the one bucket that straddles
        the K'th place. Counts are bounded by the number of distinct users, so
        they can be bucketed instead of compared -- counting sort on the count,
        and a real lexicographic sort only inside the buckets you actually reach.
        Beats both of the above when T is huge and counts are small."""
        if k <= 0:
            return []

        self._refresh()
        buckets = defaultdict(list)
        for tag, count in self._counts.items():
            buckets[count].append(tag)

        result = []
        for count in sorted(buckets, reverse=True):
            result.extend(sorted(buckets[count]))   # tie-break, within one bucket
            if len(result) >= k:
                break
        return result[:k]


# ---------------------------------------------------------------------------
# Part 3 -- HyperLogLog, for when the user sets stop fitting in memory
# ---------------------------------------------------------------------------

def _hash64(value):
    return int.from_bytes(blake2b(str(value).encode(), digest_size=8).digest(), "big")


class HyperLogLog:
    """Approximate distinct count in FIXED memory: m one-byte registers per term,
    independent of how many users are inserted.

    The idea in one sentence: hash each user, use the top p bits to pick a
    register and the position of the leftmost 1 in the rest as evidence of how
    many distinct values it has seen (a run of r zeros shows up about once every
    2^r values), keep the maximum per register, and take the harmonic mean.

    Drop-in for ExactUsers -- same add() -> bool and len() -- which is the point
    of passing a sketch_factory to TopKSearchTerms. add() reports "a register
    moved", not "this user is new"; those differ (a register saturates), but the
    caller only uses it to mark the count stale, and a stale-but-unchanged count
    is harmless while a missed update is not.

    Standard error is 1.04 / sqrt(m), so accuracy is a memory dial:
        p=11   m=2048    2 KB     +/- 2.3%
        p=14   m=16384  16 KB     +/- 0.81%   (Redis: 12 KB, 6-bit registers)
    """

    def __init__(self, p=14):
        assert 7 <= p <= 16, "p in [7, 16]; the alpha below assumes m >= 128"
        self.p = p
        self.m = 1 << p
        self.registers = bytearray(self.m)
        self.alpha = 0.7213 / (1 + 1.079 / self.m)

    def add(self, value):
        h = _hash64(value)
        index = h >> (64 - self.p)                      # top p bits pick a register
        rest = (h << self.p) & ((1 << 64) - 1)          # the other 64 - p bits
        rank = (64 - self.p + 1) if rest == 0 else (64 - rest.bit_length() + 1)
        if rank > self.registers[index]:
            self.registers[index] = rank
            return True
        return False

    def __len__(self):
        return round(self.estimate())

    def estimate(self):
        """O(m) -- which is exactly why the caller caches this instead of calling
        it per event."""
        raw = self.alpha * self.m * self.m / sum(2.0 ** -r for r in self.registers)
        zeros = self.registers.count(0)
        if raw <= 2.5 * self.m and zeros:               # small range: linear counting
            return self.m * math.log(self.m / zeros)
        return raw                                      # 64-bit hash: no large-range fix


# ---------------------------------------------------------------------------
# Part 4 -- an independent reference, for cross-checking
# ---------------------------------------------------------------------------

def reference(events, k):
    """The slow obvious way: full distinct-user table, full sort, slice. Shares no
    logic with the heap, which is what makes agreement between them evidence."""
    table = {}
    for user_id, tag in events:
        table.setdefault(tag, set()).add(user_id)
    order = sorted(table.items(), key=lambda kv: (-len(kv[1]), kv[0]))
    return [tag for tag, _ in order[:k]] if k > 0 else []


# ---- Tests ----
if __name__ == "__main__":
    # --- the dedup trap, stated as a test ------------------------------------
    spam = [["u1", "#a"], ["u1", "#a"], ["u1", "#a"], ["u2", "#b"], ["u3", "#b"]]
    naive = defaultdict(int)
    for user, tag in spam:
        naive[tag] += 1

    assert topKHashtags(spam, 2) == ["#b", "#a"]
    assert sorted(naive, key=lambda t: -naive[t]) == ["#a", "#b"]        # the wrong answer

    print("== the dedup trap ==")
    print(f"  events: u1 searched #a three times, u2 and u3 searched #b once each")
    print(f"  raw counter    -> #a:{naive['#a']}  #b:{naive['#b']}   ranks ['#a', '#b']  WRONG")
    print(f"  distinct users -> #a:1  #b:2   ranks {topKHashtags(spam, 2)}  right")

    # --- the tie-break, which is where the heap version breaks ---------------
    # Every tag has exactly one distinct user, so the answer is purely
    # lexicographic and any comparator slip shows up immediately.
    tags = ["#zebra", "#apple", "#mango", "#apple", "#kiwi", "#banana"]
    one_user = [["u1", t] for t in tags]
    expected = ["#apple", "#banana", "#kiwi"]
    assert topKHashtags(one_user, 3) == expected

    stream = TopKSearchTerms()
    stream.record_all(one_user)
    assert stream.top_k(3) == expected
    assert stream.top_k_sorted(3) == expected
    assert stream.top_k_buckets(3) == expected

    print()
    print("== the tie-break ==")
    print(f"  one user searches {len(set(tags))} distinct tags -> every count is 1")
    print(f"  top 3 = {stream.top_k(3)}  (pure lexicographic order)")
    print("  A size-K min-heap keyed (count, tag) evicts the SMALLEST tag on ties --")
    print("  the exact one to keep. The comparator has to invert both halves.")

    # --- mixed counts and ties straddling the K'th place ---------------------
    mixed = TopKSearchTerms()
    mixed.record_all(
        [["u1", "#b"], ["u2", "#b"], ["u3", "#b"]] +        # 3
        [["u1", "#d"], ["u2", "#d"]] +                      # 2
        [["u9", "#a"], ["u9", "#a"], ["u9", "#a"]] +        # 1  (same user thrice)
        [["u4", "#c"]] +                                    # 1
        [["u5", "#e"]]                                      # 1
    )
    assert mixed.popularity("#a") == 1 and mixed.popularity("#b") == 3
    full = ["#b", "#d", "#a", "#c", "#e"]
    for k in range(len(full) + 3):
        assert mixed.top_k(k) == full[:k], (k, mixed.top_k(k))

    print()
    print("== counts, then ties, at every k ==")
    print(f"  popularity: " + "  ".join(f"{t}:{mixed.popularity(t)}" for t in full))
    print(f"  k=3 cuts through the three-way tie at count 1 -> {mixed.top_k(3)}")
    print(f"  k=99 (> number of tags) -> {mixed.top_k(99)}")

    # --- degenerate inputs ---------------------------------------------------
    empty = TopKSearchTerms()
    for k in (-5, -1, 0, 1, 10):
        assert empty.top_k(k) == [] and topKHashtags([], k) == []
    for k in (-5, -1, 0):
        assert mixed.top_k(k) == [] and topKHashtags(spam, k) == []
    # The bug this guards: ranked[:-1] is the whole list minus its last element.
    assert sorted(mixed._counts, key=lambda t: (-mixed._counts[t], t))[:-1] != []

    print()
    print("== degenerate inputs ==")
    print("  no events, k <= 0, k > T: all handled. Negative k is the sharp one --")
    print("  `ranked[:k]` silently returns len-|k| tags instead of nothing.")

    # --- the three top_k strategies agree, on random streams -----------------
    rng = random.Random(20260810)
    trials = events_run = 0

    for _ in range(800):
        n_users = 1 + rng.randrange(5)          # tiny universes on purpose:
        n_tags = 1 + rng.randrange(6)           # collisions and ties everywhere
        events = [[f"u{rng.randrange(n_users)}", f"#{chr(97 + rng.randrange(n_tags))}"]
                  for _ in range(rng.randrange(30))]

        live = TopKSearchTerms()
        live.record_all(events)

        for k in range(-1, n_tags + 2):
            expected = reference(events, k)
            assert live.top_k(k) == expected, (events, k)
            assert live.top_k_sorted(k) == expected, (events, k)
            assert live.top_k_buckets(k) == expected, (events, k)
            assert topKHashtags(events, k) == expected, (events, k)

        # top_k must never depend on WHEN it is called, only on what has arrived.
        incremental = TopKSearchTerms()
        for i, (user, tag) in enumerate(events):
            incremental.record(user, tag)
            if i % 7 == 0:
                assert incremental.top_k(3) == reference(events[:i + 1], 3)
        assert incremental.top_k(n_tags) == live.top_k(n_tags)

        # A repeated event is a no-op on the answer -- the property being tested.
        if events:
            replayed = TopKSearchTerms()
            replayed.record_all(events + [rng.choice(events) for _ in range(5)])
            assert replayed.top_k(n_tags) == live.top_k(n_tags)

        trials += 1
        events_run += len(events)

    print()
    print("== the invariant, on random streams ==")
    print(f"  {trials:,} random streams, {events_run:,} events, every k from -1 to T+1:")
    print("    heap == full sort == bucket sort == brute-force reference")
    print("    replaying any event changes nothing (that IS the dedup requirement)")
    print("    top_k mid-stream == top_k over the prefix that has arrived")

    # --- HyperLogLog as the scale-out story ----------------------------------
    print()
    print("== HyperLogLog: fixed memory per term ==")

    sketched = TopKSearchTerms(sketch_factory=lambda: HyperLogLog(p=14))
    truth = TopKSearchTerms()
    # 40,000 sits at 2.44m, deliberately: it is the worst spot for plain HLL.
    sizes = {"#viral": 150000, "#big": 40000, "#niche": 3000, "#rare": 120}

    for tag, n in sizes.items():
        for user in range(n):
            event = (f"user-{tag}-{user}", tag)
            sketched.record(*event)
            truth.record(*event)

    for tag, n in sizes.items():
        estimate = sketched.popularity(tag)
        error = abs(estimate - n) / n
        assert error < 0.05, (tag, estimate, n)
        print(f"  {tag:<8} true {n:>7,}   estimated {estimate:>7,}   error {error:6.2%}")

    ranking = list(sizes)
    assert sketched.top_k(4) == truth.top_k(4) == ranking
    print(f"  ranking agrees with the exact sets: {sketched.top_k(4)}")
    print(f"  exact sets here hold {sum(sizes.values()):,} user ids (tens of MB);")
    print(f"  the sketches are 16 KB per term flat, however many users arrive.")
    print("  #big is the honest one: 40,000 ~= 2.44m, right where linear counting")
    print("  hands off to the raw estimator and plain HLL is biased a few percent.")
    print("  HLL++ fixes exactly that band with an empirical bias table.")

    print()
    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "Why not a Counter, really?"
#     Because the metric is distinct users and a counter cannot un-count. There is
#     no post-hoc fix: once u1's fiftieth search is folded into an int, the
#     information needed to remove forty-nine of them is gone. The dedup has to
#     happen at ingest, in a structure that answers "have I seen this pair?" --
#     a set per term, or a set of (user, term) pairs guarding a counter, which is
#     the same memory in a different shape.
#
# "What is the actual complexity of top_k, and when is the heap worth it?"
#     O(T log K) + O(K log K) to order the K survivors, versus O(T log T) for the
#     plain sort. The heap wins only for K << T, and Timsort on a nearly-arbitrary
#     T-element list is fast enough that the crossover is further out than people
#     expect. `top_k_buckets` is the one with a genuinely different shape: counts
#     are bounded by the number of distinct users, so counting-sort them into
#     buckets in O(T) and only lexicographically sort the buckets you actually
#     walk. Quickselect is the fourth option -- O(T) expected -- and is worth
#     naming, but partitioning on (-count, tag) then sorting the K survivors is
#     more code than it is usually worth.
#
# "top_k is called after every event and T is in the millions."
#     Stop recomputing. Counts here only ever INCREASE, and by exactly one, which
#     is strictly easier than the LFU-cache problem: keep terms in count-buckets
#     wired as a doubly linked list, and a new distinct user moves one term one
#     bucket up in O(1). top_k then walks buckets from the top and stops after K.
#     The cost is the tie-break -- terms inside a bucket are unordered, so the one
#     bucket straddling the K'th place still needs a sort. Keep each bucket in a
#     sorted structure if the tie-break has to be exact and cheap.
#
# "How much memory is HyperLogLog, exactly?"
#     Standard error is 1.04 / sqrt(m), so accuracy is chosen, not given:
#         +/- 2.3%  needs m = 2048    -> 2 KB at one byte per register, 1.5 KB packed to 6 bits
#         +/- 1.0%  needs m ~ 10,800  -> ~11 KB at one byte, ~8 KB packed
#     Redis quotes 12 KB for 0.81% (m = 16384, 6-bit registers), and that is the
#     number to have ready. "1.5 KB for 1%" is the folk version and it is wrong by
#     roughly an order of magnitude -- derive it from 1.04/sqrt(m) instead of
#     quoting it. Low-cardinality terms cost far less in practice: a sparse
#     encoding stores explicit register indices until the dense form is smaller,
#     which matters here because the tag distribution is a long tail of terms with
#     three users each.
#
# "Doesn't HLL break the ranking?"
#     Only where the ranking is already a coin flip. Two terms whose true counts
#     are within ~1% of each other can swap places, which for a top-K feed is
#     noise, not a bug. What it DOES break is the tie-break contract: estimates
#     are floats that essentially never collide, so exact ties -- the case the
#     lexicographic rule exists for -- silently stop happening. If ties must be
#     honoured, bucket the estimates before sorting. And note the small-count
#     regime is exactly where linear counting kicks in and HLL is near-exact, so
#     the long tail ranks fine.
#
# "Sharded across machines."
#     This is HLL's real selling point over a set: registers merge with a
#     pairwise max, so each shard keeps its own sketch per term and the union is
#     computed without moving any user ids, with no double-counting of a user who
#     hit two shards. Exact sets can be unioned too, but you pay by shipping every
#     id. Each shard sends its top ~K + a margin; merging shard-local top-Ks is
#     approximate in general (a term ranked K+1 everywhere can be globally top-K),
#     so either send a margin, or run a second round asking every shard for the
#     counts of the candidate set.
#
# "Only the last 24 hours."
#     The set has no notion of time, so it has to become windowed: bucket by hour,
#     keep 24 sets (or 24 sketches) per term, and roll the oldest off. Popularity
#     is the union of the live buckets -- cheap with HLL (max the registers),
#     expensive with sets. This is also the answer to the unbounded-memory
#     objection: without eviction, `_users` grows forever, and a window is the
#     only reason the system is ever allowed to forget a (user, term) pair.
#
# "How would you test it?"
#     The property, not the fixtures: appending a DUPLICATE event to any stream
#     must not change top_k, for any k. That single line is the dedup requirement
#     restated, and it fails instantly against a Counter-based solution. Add a
#     tiny universe (5 users, 6 tags) so ties happen constantly -- that is what
#     catches the inverted heap comparator -- and sweep k from negative through
#     T + 1 to catch the slicing bugs at both ends.
