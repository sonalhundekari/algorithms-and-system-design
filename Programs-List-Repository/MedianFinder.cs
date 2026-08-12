// LeetCode 295 - Find Median from Data Stream
// Difficulty: Hard (the two-heap idea is the whole problem)
// Pattern: TWO HEAPS split at the median -- "keep the boundary, throw away the order"
//
// addNum(num) / findMedian() over an unbounded stream. Sorting on every query is
// O(n log n) per call; keeping a sorted list is O(n) per insert. Both are doing
// far more work than the question asks for.
//
// THE INSIGHT. The median depends on ONE position in the sorted order -- the
// middle -- and on nothing else. It does not care that the 3rd smallest is 7 and
// the 4th is 9; it only cares which values sit either side of the split. So do
// not maintain a total order. Maintain the SPLIT:
//
//     lo = a MAX-heap of the smaller half      hi = a MIN-heap of the larger half
//
//         ... 1  3  4  |  8  9  12 ...
//                   ^     ^
//              lo.Peek()  hi.Peek()      <- the only two values a median needs
//
// A heap is exactly the structure that gives you one end of a set cheaply and
// refuses to tell you anything else. That refusal is the point: it is why insert
// is O(log n) instead of O(n).
//
// THE INVARIANTS, and they are the entire implementation:
//
//   1. every value in lo <= every value in hi            (the split is a real split)
//   2. lo.Count == hi.Count, or lo.Count == hi.Count + 1 (lo holds the extra one)
//
//   median = odd total  -> lo.Peek()
//            even total -> (lo.Peek() + hi.Peek()) / 2
//
// THE TRICK THAT MAKES ADD UNBREAKABLE. The obvious add compares num against
// lo.Peek() to pick a heap, then rebalances -- correct, but it is two decisions
// and the empty-heap case has to be special-cased. Instead, push through the far
// side every time:
//
//     lo.Enqueue(num);  hi.Enqueue(lo.Dequeue());       // num lands in hi's half
//     if (hi.Count > lo.Count) lo.Enqueue(hi.Dequeue()); // restore the size rule
//
// Invariant 1 holds by construction -- whatever comes out of lo is lo's maximum,
// so it is >= everything left in lo, and it goes to the small end of hi. There is
// no comparison to get backwards and no empty-heap branch. Three enqueues worth
// of constant factor buys a version that is hard to write wrong under pressure.
//
//   Time:  AddNum O(log n)   FindMedian O(1)
//   Space: O(n) -- every value must be retained; a median is not summarizable
//
// FOUR IMPLEMENTATIONS, because the follow-ups are the interesting part:
//
//   MedianFinder            two heaps.    O(log n) add, O(1) median -- the answer
//   CountingMedianFinder    Fenwick tree. O(log U) add, O(log U) median, and
//                                         Remove + arbitrary percentiles for free
//   SortedListMedianFinder  sorted list.  O(n) add, O(1) median -- the oracle
//   SlidingWindowMedian     two heaps + lazy deletion (LeetCode 480)
//
// WHAT TO ASK BEFORE WRITING ANYTHING:
//
//   1. Even count: mean of the two middles, or "either middle is fine"? The mean
//      is what makes the return type double instead of int, and it is why the
//      answer to [1,2] is 1.5 and not 1 or 2.
//   2. findMedian on an empty stream -- throw, or NaN? The prompt guarantees it
//      never happens, which means it is unspecified, which means say what you did.
//   3. Is there a removeNum? It changes the answer completely: heaps cannot
//      delete an interior element, so you need lazy deletion or a Fenwick tree.
//   4. What is the value range? "-10^5 to 10^5" is a hint, not decoration -- a
//      bounded domain admits a counting structure that beats the heaps.
//   5. How many numbers, and does it fit in memory? At a billion values the exact
//      answer stops being the goal and t-digest/P^2 take over (see notes below).

using System.Diagnostics;

namespace CodingPatterns.StackQueue;

/// <summary>
/// The surface all three exact implementations share, so the randomized block can
/// drive them through one loop.
/// </summary>
public interface IMedianFinder
{
    void AddNum(int num);

    /// <summary>Median of everything added so far. Throws if nothing has been.</summary>
    double FindMedian();

    int Count { get; }
}

// ============================================================================
//  1. Two heaps -- the answer to give
// ============================================================================

/// <summary>
/// Median of a stream via a max-heap of the lower half and a min-heap of the
/// upper half, kept within one element of each other in size.
///
///   AddNum      O(log n)
///   FindMedian  O(1) -- both middles are already at a root
///   memory      O(n)
/// </summary>
public sealed class MedianFinder : IMedianFinder
{
    /// <summary>.NET's PriorityQueue is a min-heap; reversing the comparer makes a max-heap.</summary>
    private static readonly IComparer<int> Descending = Comparer<int>.Create((a, b) => b.CompareTo(a));

    private readonly PriorityQueue<int, int> _lo = new(Descending);   // smaller half, largest on top
    private readonly PriorityQueue<int, int> _hi = new();             // larger half, smallest on top

    public int Count => _lo.Count + _hi.Count;

    /// <summary>
    /// Every value takes the same path: into lo, straight out of lo into hi, and
    /// then one element back if hi has grown too big. No comparison against a
    /// heap top, so no empty-heap special case and nothing to invert.
    /// </summary>
    public void AddNum(int num)
    {
        _lo.Enqueue(num, num);

        int spill = _lo.Dequeue();      // lo's maximum -- <= nothing left in lo, so hi stays sorted above lo
        _hi.Enqueue(spill, spill);

        if (_hi.Count > _lo.Count)      // the size rule: lo holds the odd one out
        {
            int back = _hi.Dequeue();
            _lo.Enqueue(back, back);
        }
    }

    /// <summary>
    /// O(1): the two candidate middles are the two roots. The cast keeps the sum
    /// out of int arithmetic -- irrelevant at |num| &lt;= 10^5, but this class does
    /// not know that, and int.MaxValue + int.MaxValue is a silent wrap.
    /// </summary>
    public double FindMedian()
    {
        if (_lo.Count == 0)
            throw new InvalidOperationException("median of an empty stream is undefined");

        return _lo.Count > _hi.Count
            ? _lo.Peek()
            : ((double)_lo.Peek() + _hi.Peek()) / 2.0;
    }

    /// <summary>The two values the median is computed from. Useful for asserting invariant 1.</summary>
    public (int Lower, int Upper) Middles()
    {
        if (_lo.Count == 0)
            throw new InvalidOperationException("empty stream");
        return (_lo.Peek(), _hi.Count > 0 ? _hi.Peek() : _lo.Peek());
    }

    /// <summary>Invariant check: sizes within one, and every lo element &lt;= every hi element.</summary>
    public bool InvariantsHold()
    {
        if (_lo.Count != _hi.Count && _lo.Count != _hi.Count + 1)
            return false;
        if (_lo.Count == 0)
            return true;

        int loMax = _lo.Peek();
        return _hi.Count == 0 || loMax <= _hi.Peek();
    }

    public static void Main() => MedianFinderTests.Run();
}

// ============================================================================
//  2. Fenwick tree over the value domain -- the answer to "the range is bounded"
// ============================================================================

/// <summary>
/// The constraint "-10^5 &lt;= num &lt;= 10^5" is doing work: only 200,001 distinct
/// values exist, so instead of storing the numbers, store HOW MANY of each. A
/// Fenwick (binary indexed) tree over that array gives prefix counts in O(log U),
/// and the median is a k-th-smallest query, which Fenwick answers by binary
/// lifting -- descending the implicit tree instead of binary-searching prefixes,
/// so it is one O(log U) pass rather than O(log^2 U).
///
///   AddNum      O(log U)      U = size of the value domain, NOT the stream length
///   FindMedian  O(log U)
///   Remove      O(log U)      <- the heaps cannot do this at all
///   KthSmallest / Percentile / CountLessThan  O(log U)  <- nor these
///   memory      O(U), independent of n
///
/// So it is worse than the heaps on paper (O(1) median lost) and better in every
/// other way that matters here: log2(200001) ~ 18 comparisons of int arithmetic
/// with no allocation beats a heap sift in practice, memory is a flat 800 KB no
/// matter how many billions of values stream through, and deletions and
/// percentiles fall out for free. Reach for it when the domain is small and
/// known; the heaps remain the answer when it is not.
/// </summary>
public sealed class CountingMedianFinder : IMedianFinder
{
    private readonly int _min;
    private readonly int _max;
    private readonly int[] _tree;       // 1-indexed Fenwick: _tree[i] covers a block ending at i
    private readonly int _highBit;      // largest power of two <= domain size, for the lifting descent
    private int _count;

    public CountingMedianFinder(int min = -100_000, int max = 100_000)
    {
        if (min > max)
            throw new ArgumentException("min must not exceed max", nameof(min));

        _min = min;
        _max = max;
        _tree = new int[max - min + 2];  // slot 0 unused

        int size = max - min + 1;
        _highBit = 1;
        while (_highBit * 2 <= size)
            _highBit *= 2;
    }

    public int Count => _count;

    public void AddNum(int num)
    {
        Update(IndexOf(num), +1);
        _count++;
    }

    /// <summary>Removes one occurrence. False if the value was not present.</summary>
    public bool Remove(int num)
    {
        if (CountOf(num) == 0)
            return false;

        Update(IndexOf(num), -1);
        _count--;
        return true;
    }

    public double FindMedian()
    {
        if (_count == 0)
            throw new InvalidOperationException("median of an empty stream is undefined");

        // Odd: the single middle. Even: the two straddling it, averaged.
        return _count % 2 == 1
            ? KthSmallest(_count / 2 + 1)
            : ((double)KthSmallest(_count / 2) + KthSmallest(_count / 2 + 1)) / 2.0;
    }

    /// <summary>k-th smallest, 1-indexed. The median is just two of these.</summary>
    public int KthSmallest(int k)
    {
        if (k < 1 || k > _count)
            throw new ArgumentOutOfRangeException(nameof(k), $"k must be in [1, {_count}]");

        // Binary lifting: walk down the Fenwick blocks largest-first, taking a
        // step whenever the block still leaves fewer than k elements behind. Ends
        // on the last index whose prefix is < k, so the answer is the next one.
        int position = 0;
        int remaining = k;

        for (int step = _highBit; step > 0; step >>= 1)
        {
            int next = position + step;
            if (next < _tree.Length && _tree[next] < remaining)
            {
                position = next;
                remaining -= _tree[next];
            }
        }

        return position + _min;          // index position+1 <-> value _min + position
    }

    /// <summary>
    /// The p-th percentile, p in [0, 100], nearest-rank. Free once k-th-smallest
    /// exists -- and the reason a monitoring system stores counts, not values:
    /// p50 and p99 come off the same structure.
    /// </summary>
    public int Percentile(double p)
    {
        if (_count == 0)
            throw new InvalidOperationException("percentile of an empty stream is undefined");
        if (p < 0 || p > 100)
            throw new ArgumentOutOfRangeException(nameof(p), "percentile must be in [0, 100]");

        int rank = (int)Math.Ceiling(p / 100.0 * _count);
        return KthSmallest(Math.Max(1, rank));
    }

    public int CountLessOrEqual(int num) =>
        num < _min ? 0 : Prefix(IndexOf(Math.Min(num, _max)));

    public int CountOf(int num) => CountLessOrEqual(num) - CountLessOrEqual(num - 1);

    private int IndexOf(int num)
    {
        if (num < _min || num > _max)
            throw new ArgumentOutOfRangeException(
                nameof(num), $"{num} is outside the declared domain [{_min}, {_max}]");
        return num - _min + 1;
    }

    private void Update(int index, int delta)
    {
        for (; index < _tree.Length; index += index & -index)
            _tree[index] += delta;
    }

    private int Prefix(int index)
    {
        int sum = 0;
        for (; index > 0; index -= index & -index)
            sum += _tree[index];
        return sum;
    }
}

// ============================================================================
//  3. The oracle -- too slow to ship, too simple to be wrong
// ============================================================================

/// <summary>
/// Keeps the whole stream sorted by binary-searching the insertion point. The
/// search is O(log n) and then the shift is O(n), which is the cost the heaps
/// exist to avoid -- but it states the definition of "median" with nothing in the
/// way, so the randomized block can hold the other two against it.
///
/// Worth saying out loud in an interview: at small n this WINS. The shift is a
/// memmove of contiguous ints, so the crossover against a heap sits somewhere in
/// the low thousands. "O(n) beats O(log n)" is not a paradox, it is constants.
/// </summary>
public sealed class SortedListMedianFinder : IMedianFinder
{
    private readonly List<int> _sorted = new();

    public int Count => _sorted.Count;

    public void AddNum(int num)
    {
        int index = _sorted.BinarySearch(num);
        if (index < 0)
            index = ~index;              // BinarySearch returns the bitwise complement of the insertion point
        _sorted.Insert(index, num);
    }

    public double FindMedian()
    {
        int n = _sorted.Count;
        if (n == 0)
            throw new InvalidOperationException("median of an empty stream is undefined");

        return n % 2 == 1
            ? _sorted[n / 2]
            : ((double)_sorted[n / 2 - 1] + _sorted[n / 2]) / 2.0;
    }
}

// ============================================================================
//  4. Sliding window median -- the follow-up where the heaps need help
// ============================================================================

/// <summary>
/// LeetCode 480. The window moves, so every insert is paired with a REMOVE of the
/// value that fell off the back -- and a binary heap cannot delete an interior
/// element, only its root. Same wall as the priority-queue problems, same escape:
/// do not delete. Record the value as owed in a `delayed` map, adjust the LOGICAL
/// sizes, and discard the corpse when it surfaces at a root.
///
/// The bookkeeping that makes it work: `_loSize` / `_hiSize` count LIVE elements,
/// the heaps hold live plus tombstones, and every operation ends with both roots
/// pruned so a peek always sees a real value. Amortized O(log k) per step.
/// </summary>
public sealed class SlidingWindowMedian
{
    private static readonly IComparer<int> Descending = Comparer<int>.Create((a, b) => b.CompareTo(a));

    private readonly PriorityQueue<int, int> _lo = new(Descending);
    private readonly PriorityQueue<int, int> _hi = new();
    private readonly Dictionary<int, int> _delayed = new();   // value -> removals still owed
    private int _loSize, _hiSize;                             // LIVE counts, not _lo.Count / _hi.Count

    public void Insert(int num)
    {
        if (_loSize == 0 || num <= _lo.Peek())
        {
            _lo.Enqueue(num, num);
            _loSize++;
        }
        else
        {
            _hi.Enqueue(num, num);
            _hiSize++;
        }

        Rebalance();
    }

    /// <summary>
    /// Marks one occurrence dead. The element is NOT removed from its heap unless
    /// it happens to be sitting at the root, where it is finally addressable.
    /// </summary>
    public void Erase(int num)
    {
        _delayed[num] = _delayed.GetValueOrDefault(num) + 1;

        if (num <= _lo.Peek())
        {
            _loSize--;
            if (num == _lo.Peek())
                Prune(_lo);
        }
        else
        {
            _hiSize--;
            if (num == _hi.Peek())
                Prune(_hi);
        }

        Rebalance();
    }

    public double Median() =>
        (_loSize + _hiSize) % 2 == 1
            ? _lo.Peek()
            : ((double)_lo.Peek() + _hi.Peek()) / 2.0;

    /// <summary>Median of every length-k window of <paramref name="nums"/>, left to right.</summary>
    public static double[] Compute(int[] nums, int k)
    {
        if (nums is null)
            throw new ArgumentNullException(nameof(nums));
        if (k < 1 || k > nums.Length)
            throw new ArgumentOutOfRangeException(nameof(k), "window must fit inside the array");

        var window = new SlidingWindowMedian();
        var medians = new double[nums.Length - k + 1];

        for (int i = 0; i < nums.Length; i++)
        {
            window.Insert(nums[i]);
            if (i >= k)
                window.Erase(nums[i - k]);       // the value that just fell off the back
            if (i >= k - 1)
                medians[i - k + 1] = window.Median();
        }

        return medians;
    }

    /// <summary>Throws away tombstones sitting at the root, so the next peek is live.</summary>
    private static void Prune(PriorityQueue<int, int> heap, Dictionary<int, int> delayed)
    {
        while (heap.Count > 0 && delayed.TryGetValue(heap.Peek(), out int owed) && owed > 0)
        {
            int dead = heap.Dequeue();
            if (owed == 1)
                delayed.Remove(dead);
            else
                delayed[dead] = owed - 1;
        }
    }

    private void Prune(PriorityQueue<int, int> heap) => Prune(heap, _delayed);

    /// <summary>
    /// Restores "lo holds the odd one out" using LIVE sizes. Moving a root across
    /// exposes whatever was underneath, which may be a corpse -- hence the prune.
    /// </summary>
    private void Rebalance()
    {
        if (_loSize > _hiSize + 1)
        {
            int spill = _lo.Dequeue();
            _hi.Enqueue(spill, spill);
            _loSize--;
            _hiSize++;
            Prune(_lo);
        }
        else if (_loSize < _hiSize)
        {
            int back = _hi.Dequeue();
            _lo.Enqueue(back, back);
            _loSize++;
            _hiSize--;
            Prune(_hi);
        }
    }
}

// ============================================================================
//  Demo + tests
// ============================================================================

internal static class MedianFinderTests
{
    public static void Run()
    {
        WorkedExample();
        OddEvenAndDuplicates();
        Streaming();
        BoundedDomainExtras();
        SlidingWindow();
        EdgeCases();
        Randomized();
        Timing();
    }

    private static void WorkedExample()
    {
        Console.WriteLine("== the worked example ==");

        var mf = new MedianFinder();
        mf.AddNum(1);
        mf.AddNum(2);
        Console.WriteLine($"  add 1, add 2 -> findMedian {mf.FindMedian()}   (expect 1.5)");
        mf.AddNum(3);
        Console.WriteLine($"  add 3        -> findMedian {mf.FindMedian()}   (expect 2)");
        Console.WriteLine($"  the two middles right now: {mf.Middles()} -- median never looks past these");
    }

    private static void OddEvenAndDuplicates()
    {
        Console.WriteLine();
        Console.WriteLine("== odd, even, duplicates, negatives ==");

        Console.WriteLine($"  [1,2,3]            -> {Median(1, 2, 3)}   (odd: the middle itself)");
        Console.WriteLine($"  [1,2]              -> {Median(1, 2)} (even: the MEAN, which is why it returns double)");
        Console.WriteLine($"  [5,5,5,5]          -> {Median(5, 5, 5, 5)}   (duplicates are ordinary values, not a special case)");
        Console.WriteLine($"  [-3,-2,-1]         -> {Median(-3, -2, -1)}  (nothing assumes positivity)");
        Console.WriteLine($"  [-5,5]             -> {Median(-5, 5)}   (the mean of the middles can be 0)");
        Console.WriteLine($"  [1,2,3,4]          -> {Median(1, 2, 3, 4)} (2.5 -- not 2, and not an integer at all)");
        Console.WriteLine($"  [-100000, 100000]  -> {Median(-100_000, 100_000)}   (domain extremes)");

        static double Median(params int[] nums)
        {
            var mf = new MedianFinder();
            foreach (int n in nums)
                mf.AddNum(n);
            return mf.FindMedian();
        }
    }

    private static void Streaming()
    {
        Console.WriteLine();
        Console.WriteLine("== the median after every single add, and the invariant behind it ==");

        var mf = new MedianFinder();
        var oracle = new SortedListMedianFinder();
        int[] stream = { 6, 10, 2, 6, 5, 0, 6, 3, 1, 0, 0 };
        bool matches = true, invariants = true;

        foreach (int num in stream)
        {
            mf.AddNum(num);
            oracle.AddNum(num);
            matches &= mf.FindMedian() == oracle.FindMedian();
            invariants &= mf.InvariantsHold();
            Console.WriteLine($"  add {num,3} -> n = {mf.Count,2}, median {mf.FindMedian(),5}, middles {mf.Middles()}");
        }

        Console.WriteLine($"  agrees with the sorted oracle at every step: {matches}");
        Console.WriteLine($"  sizes within one AND lo.Peek() <= hi.Peek() throughout: {invariants}");
        Console.WriteLine("  ^ the second half is invariant 1, and the push-through-the-far-side add");
        Console.WriteLine("    is what makes it impossible to break: what leaves lo IS lo's maximum.");
    }

    private static void BoundedDomainExtras()
    {
        Console.WriteLine();
        Console.WriteLine("== what the bounded value range buys (Fenwick over [-10^5, 10^5]) ==");

        var counting = new CountingMedianFinder();
        foreach (int num in new[] { 41, 35, 62, 5, 97, 35, 12, 78, 35, 50 })
            counting.AddNum(num);

        Console.WriteLine($"  10 values, median {counting.FindMedian()}, p90 {counting.Percentile(90)}, " +
                          $"p10 {counting.Percentile(10)}");
        Console.WriteLine($"  3rd smallest {counting.KthSmallest(3)}, how many <= 40: {counting.CountLessOrEqual(40)}, " +
                          $"occurrences of 35: {counting.CountOf(35)}");
        Console.WriteLine($"  Remove(35) -> {counting.Remove(35)}, Remove(999) -> {counting.Remove(999)}, " +
                          $"median now {counting.FindMedian()}");
        Console.WriteLine("  Removal and percentiles are the two things the heaps simply cannot do, and");
        Console.WriteLine("  they cost nothing here -- the median was already a k-th-smallest query.");

        try
        {
            counting.AddNum(200_000);
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.WriteLine("  a value outside the declared domain is rejected, not silently clamped:");
            Console.WriteLine("  the memory win is bought with an assumption, so the assumption is enforced.");
        }
    }

    private static void SlidingWindow()
    {
        Console.WriteLine();
        Console.WriteLine("== follow-up: median of every sliding window (LeetCode 480) ==");

        int[] nums = { 1, 3, -1, -3, 5, 3, 6, 7 };
        var got = SlidingWindowMedian.Compute(nums, 3);
        Console.WriteLine($"  nums {string.Join(",", nums)}, k = 3");
        Console.WriteLine($"  medians -> {string.Join(", ", got)}");
        Console.WriteLine($"  matches brute force: {got.SequenceEqual(BruteWindows(nums, 3))}");

        var evenWindow = SlidingWindowMedian.Compute(nums, 4);
        Console.WriteLine($"  k = 4 (even windows) -> {string.Join(", ", evenWindow)}");
        Console.WriteLine($"  matches brute force: {evenWindow.SequenceEqual(BruteWindows(nums, 4))}");
        Console.WriteLine("  A window means REMOVAL, and a heap cannot remove an interior element -- so");
        Console.WriteLine("  the same lazy-deletion trick as the priority queue: mark it, drop it at the root.");
    }

    private static void EdgeCases()
    {
        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var single = new MedianFinder();
        single.AddNum(7);
        Console.WriteLine($"  one element -> {single.FindMedian()} (the median of a singleton is itself)");

        foreach (var (label, empty) in new (string, IMedianFinder)[]
                 {
                     ("two heaps", new MedianFinder()),
                     ("Fenwick", new CountingMedianFinder()),
                     ("sorted list", new SortedListMedianFinder()),
                 })
        {
            try
            {
                empty.FindMedian();
                Console.WriteLine($"  {label}: empty stream NOT rejected -- bug");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"  {label,-11} on an empty stream: {ex.Message}");
            }
        }
        Console.WriteLine("  ^ the prompt guarantees this never happens, which means it is UNSPECIFIED.");
        Console.WriteLine("    Throwing is a choice; returning NaN is another. Say which one you picked.");

        var ascending = new MedianFinder();
        var descending = new MedianFinder();
        for (int i = 1; i <= 1000; i++)
        {
            ascending.AddNum(i);
            descending.AddNum(1001 - i);
        }
        Console.WriteLine($"  1..1000 ascending -> {ascending.FindMedian()}, descending -> {descending.FindMedian()}");
        Console.WriteLine("  ^ sorted input is the worst case for a naive sorted-array insert and nothing");
        Console.WriteLine("    at all for the heaps: every add is one sift, whichever end it arrives at.");

        var extremes = new MedianFinder();
        extremes.AddNum(int.MaxValue);
        extremes.AddNum(int.MaxValue);
        Console.WriteLine($"  two int.MaxValue -> {extremes.FindMedian()} " +
                          "(the double cast; int arithmetic would wrap to -1)");
    }

    private static void Randomized()
    {
        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20250811);
        bool heapsMatchOracle = true, fenwickMatchesOracle = true, invariantsHold = true;
        bool kthMatchesOracle = true, windowsMatchBrute = true;

        for (int trial = 0; trial < 3000; trial++)
        {
            var heaps = new MedianFinder();
            var fenwick = new CountingMedianFinder(-50, 50);
            var oracle = new SortedListMedianFinder();
            var seen = new List<int>();

            int adds = rng.Next(1, 40);
            for (int i = 0; i < adds; i++)
            {
                // A deliberately tiny value range: duplicates and ties are where
                // an off-by-one in the size rule shows up, so make them common.
                int num = rng.Next(-6, 7);
                heaps.AddNum(num);
                fenwick.AddNum(num);
                oracle.AddNum(num);
                seen.Add(num);

                heapsMatchOracle &= heaps.FindMedian() == oracle.FindMedian();
                fenwickMatchesOracle &= fenwick.FindMedian() == oracle.FindMedian();
                invariantsHold &= heaps.InvariantsHold();
            }

            // The Fenwick's k-th-smallest is the general form of the median, so
            // check the whole rank range, not just the middle.
            seen.Sort();
            for (int k = 1; k <= seen.Count; k++)
                kthMatchesOracle &= fenwick.KthSmallest(k) == seen[k - 1];
        }

        for (int trial = 0; trial < 1500; trial++)
        {
            var nums = new int[rng.Next(1, 25)];
            for (int i = 0; i < nums.Length; i++)
                nums[i] = rng.Next(-8, 9);

            int k = rng.Next(1, nums.Length + 1);
            windowsMatchBrute &= SlidingWindowMedian.Compute(nums, k).SequenceEqual(BruteWindows(nums, k));
        }

        Console.WriteLine($"  3,000 streams, two heaps == sorted oracle after every add: {heapsMatchOracle}");
        Console.WriteLine($"  Fenwick == sorted oracle after every add:                  {fenwickMatchesOracle}");
        Console.WriteLine($"  Fenwick k-th smallest == the sorted array, all k:          {kthMatchesOracle}");
        Console.WriteLine($"  heap invariants held at every intermediate state:          {invariantsHold}");
        Console.WriteLine($"  1,500 sliding windows == brute force (all k):              {windowsMatchBrute}");
    }

    private static void Timing()
    {
        Console.WriteLine();
        Console.WriteLine("== the cost, measured ==");

        var rng = new Random(7);

        // Small n, all three: the sorted list is still competitive here, which is
        // the honest version of "O(n) insert is bad".
        const int small = 25_000;
        var sample = new int[small];
        for (int i = 0; i < small; i++)
            sample[i] = rng.Next(-100_000, 100_001);

        Console.WriteLine($"  n = {small:N0}, median queried after every add:");
        Console.WriteLine($"    two heaps  {Measure(new MedianFinder(), sample),7:N0} ms");
        Console.WriteLine($"    Fenwick    {Measure(new CountingMedianFinder(), sample),7:N0} ms");
        Console.WriteLine($"    sorted list{Measure(new SortedListMedianFinder(), sample),7:N0} ms");

        Console.WriteLine("  ^ note the sorted list is not the loser here: 25k memmoves of contiguous");
        Console.WriteLine("    ints beat 25k heap sifts, because O(n) with a memmove constant beats");
        Console.WriteLine("    O(log n) with a pointer-chasing one until n gets big. Then it stops.");

        // Large n, only the two that scale. The sorted list at this size is
        // ~10^11 bytes of memmove, so it is not run rather than reported slow.
        const int large = 500_000;
        var big = new int[large];
        for (int i = 0; i < large; i++)
            big[i] = rng.Next(-100_000, 100_001);

        Console.WriteLine($"  n = {large:N0} (20x), sorted list omitted -- it is quadratic and would not finish:");
        Console.WriteLine($"    two heaps  {Measure(new MedianFinder(), big),7:N0} ms");
        Console.WriteLine($"    Fenwick    {Measure(new CountingMedianFinder(), big),7:N0} ms");
        Console.WriteLine("  ^ the heaps grow ~20x (linear in n, the log factor invisible); the sorted");
        Console.WriteLine("    list would grow ~400x -- that is the crossover, and it is the whole reason");
        Console.WriteLine("    to reach for heaps. Fenwick's per-op cost does not move with n at all:");
        Console.WriteLine("    it is O(log U) in the DOMAIN, so 500k values cost the same each as 25k.");

        static long Measure(IMedianFinder finder, int[] data)
        {
            var sw = Stopwatch.StartNew();
            double sink = 0;
            foreach (int num in data)
            {
                finder.AddNum(num);
                sink += finder.FindMedian();     // consumed so the call cannot be optimized away
            }
            sw.Stop();
            return sink == double.PositiveInfinity ? -1 : sw.ElapsedMilliseconds;
        }
    }

    /// <summary>Sort each window from scratch. O(n k log k) and obviously correct.</summary>
    private static double[] BruteWindows(int[] nums, int k)
    {
        var medians = new double[nums.Length - k + 1];
        for (int i = 0; i + k <= nums.Length; i++)
        {
            var window = nums.Skip(i).Take(k).OrderBy(x => x).ToArray();
            medians[i] = k % 2 == 1
                ? window[k / 2]
                : ((double)window[k / 2 - 1] + window[k / 2]) / 2.0;
        }
        return medians;
    }
}

// ---- Notes for the follow-up questions ----
//
// "All numbers are in [0, 100]."
//     The classic follow-up, and the answer is to stop using heaps: a int[101] of
//     counts makes addNum O(1), and the median is a scan of at most 101 buckets,
//     i.e. O(1) with a large constant. CountingMedianFinder above is the general
//     version (Fenwick, so the scan becomes O(log U) and percentiles come too).
//     Note the stated limits here -- -10^5..10^5 -- already permit exactly this.
//
// "99% of numbers are in [0, 100], the rest are arbitrary."
//     Hybrid: counts for the hot range, plus two small heaps for the outliers
//     below and above it. The median lives in the dense region with overwhelming
//     probability, so you locate it by rank -- "there are L values below the
//     range and H above" -- and only touch the heaps when the rank falls outside.
//     The point being tested is whether you notice the median is a RANK query,
//     and rank composes across disjoint ranges.
//
// "Support removeNum(num)."
//     Two heaps stop working directly: a binary heap can only address its root.
//     Two ways out. (a) Lazy deletion -- a `delayed` count per value, logical
//     sizes kept separately, corpses discarded when they reach a root; that is
//     SlidingWindowMedian above, amortized O(log n). (b) A Fenwick tree, if the
//     domain is bounded, where removal is a -1 update and nothing special at all.
//     For an unbounded domain with heavy deletion, an order-statistic tree
//     (SortedList + rank, or a balanced BST with subtree counts) is the real
//     structure; C# has no built-in one, which is why (a) is the interview answer.
//
// "Median of the last k elements only."
//     Sliding window, above. The window is what forces the removal, so the answer
//     is the removal answer plus a queue of what to evict.
//
// "Return the 90th percentile instead."
//     The two-heap split is specifically the 50/50 split; a p90 needs the heaps
//     sized 9:1, and every insert must then walk the boundary back into ratio,
//     which is O(1) amortized but fiddly, and it only supports ONE percentile
//     fixed at construction. If more than one percentile is wanted, the Fenwick
//     (or a t-digest) is the structure -- see Percentile() above.
//
// "The stream is a billion numbers / it must run in bounded memory."
//     Exact median in one pass needs O(n) memory -- it is not a summarizable
//     statistic like a mean or a max, because any value can still turn out to be
//     the middle one. So either bound the DOMAIN (counting/Fenwick: memory O(U),
//     independent of n -- the trick that makes this practical) or give up
//     exactness: t-digest and P^2 hold a few hundred bytes and answer any
//     quantile within a percent or so, which is what every metrics backend
//     actually ships. Reservoir sampling is the third option and the weakest:
//     it bounds memory but its error bars are far worse per byte.
//
// "Multiple threads call addNum."
//     A lock around add+median is correct and probably enough -- both are O(log n)
//     of pure arithmetic, so the critical section is tiny. Finer-grained locking
//     across two heaps is a trap: the balance step touches both, so a per-heap
//     lock has to take them in a fixed order and the invariant is briefly false
//     in between, meaning a concurrent findMedian can read a genuinely wrong
//     answer rather than a merely stale one. If reads dominate, keep the writer
//     single-threaded and publish an immutable (loTop, hiTop, parity) snapshot
//     after each add -- readers then never touch the heaps at all.
//
// "Merge two MedianFinders."
//     Heaps do not merge cheaply (O(n) rebuild); a Fenwick does, in O(U), by
//     adding the count arrays element-wise. Another reason the bounded-domain
//     version is the one that survives contact with a distributed system: the
//     shards ship their count arrays and the coordinator sums them.
