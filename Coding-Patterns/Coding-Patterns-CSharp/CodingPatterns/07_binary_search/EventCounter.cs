// Event Stream Count in Time Range
// Difficulty: Medium (design)
// Pattern: one sorted timestamp list per key + two binary searches
//
// Problem: A stream delivers (eventType, timestamp) pairs with non-decreasing
// timestamps. Support:
//   Receive(eventType, timestamp)                 -> void
//   Count(eventType, startTime, endTime)          -> int, inclusive [start, end]
// Many event types share the stream; counts are per-type.
//
// The clarifying answers that pick this design:
//   1. Timestamps are non-decreasing across Receive calls -> the per-type list
//      is ALREADY sorted, so a write is a plain append. No tree required.
//   2. Reads are range COUNTS, not value lookups -> we never need to touch the
//      events in the window, only find its two edges.
//
// Approach: bucket by type first, then binary search inside the bucket.
//
//   _events   eventType -> List<long> of timestamps, ascending, append-only
//
// Count(t, lo, hi) = UpperBound(times, hi) - LowerBound(times, lo)
//                    |                        |
//                    first index past hi      first index at or after lo
//
// The two bounds are the whole problem. LowerBound is bisect_left (>= lo) and
// UpperBound is bisect_right (> hi); pairing left-on-start with right-on-end is
// exactly what makes BOTH endpoints inclusive. Any other pairing silently drops
// or double-counts events that land on a boundary, which is the bug an
// interviewer is looking for -- see the boundary drill in Run().
//
// Both are written out by hand rather than leaning on List.BinarySearch, which
// picks an arbitrary index among equal elements and so cannot answer either
// question on its own. The tests cross-check every window against brute force.
//
// Time:  Receive  O(1) amortized (append to a list that is already in order)
//        Count    O(log n) with n = events of THAT type, independent of the
//                 total stream size and of the number of events in the window
// Space: O(n) total across all types -- 8 bytes per event, nothing per query
//
// This is the data-structure half of "Time Based Key-Value Store" (LC 981):
// same per-key sorted list, same binary search, but reduced to counting the
// window instead of retrieving the value at its right edge.

namespace CodingPatterns.BinarySearch;

public class EventCounter
{
    private readonly Dictionary<string, List<long>> _events = new();

    // ---- binary search primitives (bisect_left / bisect_right, by hand) ----

    // First index i with times[i] >= target. Equivalent to Python's bisect_left.
    //
    // Invariant: the answer always lives in [lo, hi]. `hi` starts at Count
    // rather than Count - 1 because "no such index" is a legal answer -- every
    // timestamp being smaller than target must return Count, not -1.
    private static int LowerBound(List<long> times, long target)
    {
        int lo = 0, hi = times.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;   // written this way so lo + hi cannot overflow
            if (times[mid] < target)
                lo = mid + 1;               // mid is too early, it cannot be the answer
            else
                hi = mid;                   // mid qualifies, but something left of it might too
        }
        return lo;
    }

    // First index i with times[i] > target. Equivalent to Python's bisect_right.
    // Identical to LowerBound except for `<=`, which is what walks past a run of
    // duplicates instead of stopping at its front.
    private static int UpperBound(List<long> times, long target)
    {
        int lo = 0, hi = times.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (times[mid] <= target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    // ---- API ----

    /// <summary>Record one event. O(1) amortized while timestamps stay non-decreasing.</summary>
    public void Receive(string eventType, long timestamp)
    {
        if (eventType is null)
            throw new ArgumentNullException(nameof(eventType));

        if (!_events.TryGetValue(eventType, out var times))
            _events[eventType] = times = new List<long>();

        // The fast path the problem's guarantee buys us: the new timestamp is
        // >= every timestamp already stored, so appending keeps the list sorted.
        if (times.Count == 0 || timestamp >= times[^1])
        {
            times.Add(timestamp);
        }
        else
        {
            // The guarantee was violated. Splice it into place so Count stays
            // correct rather than silently returning garbage. This costs O(n)
            // per out-of-order arrival -- fine for the occasional straggler, not
            // a substitute for the real fix (see the follow-up notes at the end).
            times.Insert(LowerBound(times, timestamp), timestamp);
        }
    }

    /// <summary>Events of <paramref name="eventType"/> with startTime &lt;= timestamp &lt;= endTime. O(log n).</summary>
    public int Count(string eventType, long startTime, long endTime)
    {
        if (startTime > endTime)
            return 0;                                   // empty window, not an error

        if (eventType is null || !_events.TryGetValue(eventType, out var times))
            return 0;                                   // type never seen

        int first = LowerBound(times, startTime);       // first event at or after the start
        int past = UpperBound(times, endTime);          // first event strictly after the end
        return past - first;
    }

    // ---- small conveniences, same structure ----

    /// <summary>All-time count for a type. O(1) -- no need to binary search for it.</summary>
    public int Total(string eventType) =>
        eventType is not null && _events.TryGetValue(eventType, out var times) ? times.Count : 0;

    public List<string> EventTypes()
    {
        var types = new List<string>(_events.Keys);
        types.Sort(StringComparer.Ordinal);
        return types;
    }

    public int TotalEvents() => _events.Values.Sum(times => times.Count);

    // Exposed for the boundary drill in Run(); not part of the interview API.
    internal List<long> TimestampsFor(string eventType) =>
        _events.TryGetValue(eventType, out var times) ? times : new List<long>();

    public static void Run()
    {
        // Reference implementation: walk everything and compare.
        static int BruteCount(List<long> times, long lo, long hi) =>
            times.Count(t => lo <= t && t <= hi);

        // -- the worked example --
        var ec = new EventCounter();
        ec.Receive("login", 100);
        ec.Receive("click", 110);
        ec.Receive("login", 120);
        ec.Receive("login", 200);

        Console.WriteLine($"login in [100,150]: {ec.Count("login", 100, 150)}  (expect 2)");
        Console.WriteLine($"login in [100,250]: {ec.Count("login", 100, 250)}  (expect 3)");
        Console.WriteLine($"click in [0,1000]:  {ec.Count("click", 0, 1000)}  (expect 1)");

        bool agreed = ec.Count("login", 100, 150) == 2
                   && ec.Count("login", 100, 250) == 3
                   && ec.Count("click", 0, 1000) == 1;

        // -- edge cases --
        agreed &= new EventCounter().Count("anything", 0, 10) == 0;   // empty stream
        agreed &= ec.Count("logout", 0, 10_000) == 0;                 // type never seen
        agreed &= ec.Count("login", 250, 100) == 0;                   // start > end
        agreed &= ec.Count("login", 0, 99) == 0;                      // window entirely before
        agreed &= ec.Count("login", 201, 10_000) == 0;                // window entirely after
        agreed &= ec.Count("login", 150, 150) == 0;                   // empty point window
        agreed &= ec.Count("login", 120, 120) == 1;                   // point window on an event
        agreed &= ec.Total("login") == 3 && ec.TotalEvents() == 4;
        agreed &= string.Join(",", ec.EventTypes()) == "click,login";
        Console.WriteLine($"edge cases (empty stream, unseen type, start>end, point windows): {agreed}");

        // -- boundary drill: inclusive on BOTH ends is the thing to get right --
        // Duplicated timestamps make the left/right distinction visible: a window
        // touching 120 must take the whole run of 120s, from either side.
        var dup = new EventCounter();
        foreach (long t in new long[] { 100, 120, 120, 120, 140 })
            dup.Receive("e", t);

        bool boundaries = dup.Count("e", 120, 120) == 3     // the run alone
                       && dup.Count("e", 100, 120) == 4     // left edge takes 100, right takes all 120s
                       && dup.Count("e", 120, 140) == 4     // right edge takes 140
                       && dup.Count("e", 101, 119) == 0     // strictly between two runs
                       && dup.Count("e", 100, 140) == 5;    // everything

        var times = dup.TimestampsFor("e");
        for (long lo = 95; lo <= 145; lo++)
            for (long hi = 95; hi <= 145; hi++)
                boundaries &= dup.Count("e", lo, hi) == BruteCount(times, lo, hi);

        // A left/right mix-up is silent on distinct timestamps and wrong on ties:
        // LowerBound on both ends reports 0 for [120,120] instead of 3.
        boundaries &= LowerBound(times, 120) - LowerBound(times, 120) == 0;
        Console.WriteLine($"2,601 windows over duplicated timestamps match brute force: {boundaries}");

        // -- randomized cross-check against brute force --
        var rng = new Random(7);
        ec = new EventCounter();
        var truth = new Dictionary<string, List<long>>();
        long clock = 0;
        for (int i = 0; i < 5_000; i++)
        {
            clock += rng.Next(0, 3);                        // non-decreasing, with ties
            string kind = $"type{rng.Next(6)}";
            ec.Receive(kind, clock);
            if (!truth.TryGetValue(kind, out var list))
                truth[kind] = list = new List<long>();
            list.Add(clock);
        }

        bool randomAgreed = true;
        for (int i = 0; i < 2_000; i++)
        {
            string kind = $"type{rng.Next(8)}";             // includes types never sent
            long lo = rng.NextInt64(-5, clock + 5), hi = rng.NextInt64(-5, clock + 5);
            int expected = truth.TryGetValue(kind, out var list) ? BruteCount(list, lo, hi) : 0;
            randomAgreed &= ec.Count(kind, lo, hi) == expected;
        }
        Console.WriteLine($"2,000 randomized queries over {ec.TotalEvents():N0} events agree with brute force: {randomAgreed}");

        // -- out-of-order arrivals still answer correctly --
        var messy = new EventCounter();
        foreach (long t in new long[] { 50, 10, 30, 30, 20, 90, 5 })
            messy.Receive("e", t);
        bool repaired = string.Join(",", messy.TimestampsFor("e")) == "5,10,20,30,30,50,90"
                     && messy.Count("e", 20, 50) == 4
                     && messy.Count("e", 0, 4) == 0;
        Console.WriteLine($"out-of-order arrivals spliced back into order: {repaired}");

        // -- the point of the design: reads do not track the window size --
        var big = new EventCounter();
        for (int i = 0; i < 1_000_000; i++)
            big.Receive(i % 2 == 1 ? "hit" : "miss", i);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        long counted = 0;
        for (int i = 0; i < 500_000; i += 500)
            counted += big.Count("hit", i, i + 400_000);
        watch.Stop();

        Console.WriteLine($"1,000 range queries over 1,000,000 events: {watch.Elapsed.TotalMilliseconds:F1} ms " +
                          $"({counted:N0} events counted without visiting one of them)");
    }
}

// ---- Notes for the follow-up questions ----
//
// "What if timestamps were NOT monotonic?"
//     The Insert fallback above keeps answers correct but degrades writes to
//     O(n). The real fix depends on how unordered the stream is:
//       - Bounded lateness (events arrive at most D behind): keep a small
//         reorder buffer, sort it, and flush to the append-only list once the
//         watermark passes. Writes stay amortized O(1).
//       - Genuinely random order: replace the list with an order-statistic
//         structure keyed by timestamp -- a balanced BST / skip list carrying
//         subtree counts, or a Fenwick tree over compressed timestamps. Both
//         give O(log n) insert AND O(log n) range count. Note that .NET's
//         SortedSet/SortedDictionary are NOT enough on their own: they can find
//         the window's edges but cannot report the number of nodes between
//         them without walking, so the subtree counts have to be maintained.
//
// "The stream never ends -- this grows without bound."
//     Counting queries almost always target a recent window, so age events out.
//     Because the list is sorted and append-only, expiry is a prefix drop:
//     times.RemoveRange(0, LowerBound(times, now - retention)), or a Queue.
//     If even the retained window is too large to store, switch from exact
//     counts to buckets: keep per-type counts in fixed time buckets (say 1s)
//     and answer a range as a prefix-sum difference over buckets -- O(1) reads,
//     O(1) writes, memory proportional to TIME rather than events, at the cost
//     of approximation at the two partial edge buckets.
//
// "Millions of distinct event types."
//     Nothing changes structurally -- the dictionary shards cleanly by type,
//     since no query ever spans two types. That is also what makes this
//     trivially horizontal: hash the type to a node and reads stay single-node.
//
// "Now give me the count of events across ALL types in a range."
//     A second list holding every timestamp (also append-only) answers it with
//     the same two binary searches, doubling the memory. Cheaper if types are
//     few: sum Count() per type, O(T log n).
//
// "Make it a rate limiter -- how many in the last 60 seconds?"
//     Count(type, now - 60, now). This structure is the exact backing store for
//     a sliding-window-log limiter, and the retention drop above is what keeps
//     it from leaking.
//
// "Is this thread-safe?"
//     No. One writer and many readers is the common shape: guard with a
//     ReaderWriterLockSlim, or shard the lock by event type since types never
//     interact. A lock-free version is possible because writes only ever append
//     -- readers can snapshot the list length and binary search below it.
