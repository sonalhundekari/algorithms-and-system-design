/*
Grep With Context Lines (grep -C, the interview version)

A scanned document is a list of strings, one per line. Print every line that
contains `target`, plus the `linesAround` lines before and after each match.
Print each line at most once, in original document order.

    lines = [ "good morning",
              "hello there",
              "my name is Alex",
              "my friend is albert",
              "it is nice to meet you Alex" ]
    target = "Alex", linesAround = 1

    -> "hello there", "my name is Alex", "my friend is albert",
       "it is nice to meet you Alex"

"my friend is albert" is trailing context for line 2 AND leading context for
line 4. It appears once.

THE ONE SENTENCE THAT SPECIFIES THE WHOLE PROBLEM

    print line j  <=>  some i with |i - j| <= k matches

Everything below is that predicate computed in a different amount of time, or
with a different amount of the document in memory. Say it out loud before
writing code, because it settles three things the statement leaves open:

  1. DEDUPE IS BY INDEX, NOT BY CONTENT. Two identical lines at different
     indices are two different lines and both print. Anyone who reaches for a
     HashSet<string> of already-printed text has silently changed the problem;
     the natural structure is a set of INDICES, which is a bool[], which is a
     bitmap, which is a list of intervals -- Parts 1 and 3 in one breath.
  2. ORDER IS DOCUMENT ORDER, not match order. So you never sort output, and
     the overlap between two match windows is a merge, not a set union.
  3. THE OUTPUT IS A UNION OF WINDOWS. Union, not concatenation. That is the
     entire "don't print twice" requirement, and it is why every solution here
     ends up computing the same interval set.

THE FOUR PARTS, AND WHAT EACH ONE ACTUALLY CHANGES

    Part 1  whole list in memory      bool[] of kept indices        O(n*k) time
    Part 2  lines arrive one at a time ring buffer of k lines       O(k) memory
    Part 3  k is huge                 merge intervals, never mark   O(n) time
    Part 4  the file is enormous      chunk, map to intervals, merge

PART 1 -- MARK AND SWEEP. For each match, set keep[j] = true across its window;
then walk keep once and emit. The union and the ordering are both free, because
a bool[] indexed by line number cannot represent a duplicate or an ordering.
Cost: O(n*k) writes, and that is the thing Part 3 attacks.

PART 2 -- STREAMING. Two facts drive the design:

    trailing context is easy   at a match, you already know you must print the
                               next k lines -> remember an index, not the lines
    leading context is not     at a match, the k previous lines are GONE unless
                               you kept them -> a ring buffer of the last k

So: a Queue<string> capped at k, plus one integer `emitThrough` = the last index
we have committed to print. The duplicate problem does NOT need a per-line
"printed?" flag: track `nextEmit`, the lowest index not yet emitted, and the
flush loop starts at max(nextEmit, i - k). Monotone counter, no flags, no
rescanning of the buffer.

    memory   O(k) lines -- independent of document length
    latency  O(k) lines -- a line cannot be ruled OUT until k more have arrived,
             which is inherent to the problem, not to this implementation

PART 3 -- INTERVALS. Marking is wasteful because overlapping windows rewrite the
same cells: with every line matching, Part 1 performs n*(2k+1) writes to produce
n lines of output. Instead emit the window [i-k, i+k] as an interval and merge
it into the previous one when they touch. Matches are found in ascending order,
so the intervals arrive already sorted -- THERE IS NO SORT, the merge is a
one-line append. O(n) scan + O(m) merge, and the output loop costs exactly the
size of the output.

    merge when  next.Start <= last.End + 1
                            ^^^^^^^^^^^^^^  the +1 merges ADJACENT windows too.
    Not needed for correctness (adjacent windows print no line twice), but
    without it two windows that exactly abut stay separate forever and the
    interval count stops being minimal.

  A third option worth naming: the DIFFERENCE ARRAY. delta[start]++,
  delta[end+1]--, prefix-sum > 0 means keep. Also O(n), three lines, and it is
  the right answer if you want to keep the "mark then sweep" shape. Intervals
  win when the output is sparse, because the final loop touches only lines that
  are actually printed rather than all n.

PART 4 -- MULTITHREADING. Map-reduce over contiguous chunks:

    map     worker c scans lines [start_c, end_c) and returns the MERGED
            intervals for the matches it found, with windows clamped to the
            WHOLE document -- a window is allowed to overhang the chunk
    reduce  concatenate the per-chunk lists IN CHUNK ORDER and merge

  The overhang is the whole reason workers return intervals instead of lines. A
  match on the first line of chunk 3 needs k lines of context that live in chunk
  2; if workers emitted text, that context would be missing, or duplicated, or
  out of order. Intervals make the boundary a non-event -- the coordinator's
  merge fixes overlaps it never had to know about.

  And the reduce is O(m), not O(m log m): chunks are contiguous and combined in
  id order, so the concatenated interval list is ALREADY sorted by start (each
  start is its match index minus k, and match indices ascend globally). Sorting
  it would be correct and would also be admitting you did not notice.

  Results go into a pre-sized array slot per chunk, not a shared list: distinct
  array elements written by distinct threads need no lock, and reading them back
  in id order is what makes the output deterministic. A ConcurrentBag here would
  cost a lock per match AND lose the ordering that makes the reduce linear.

WHAT TO ASK BEFORE WRITING CODE
  1. Substring match, or word/regex? Substring is assumed here -- note that
     "albert" does NOT contain "Alex", so the example's third line is context,
     not a match. Case sensitivity is a parameter (`comparison`).
  2. Return the lines, or print them? Returning is testable; a real grep streams
     to stdout and must not build the output in memory. Both shapes are here.
  3. Overlapping context: separator lines between blocks? Real grep prints "--"
     between non-adjacent groups, which is why it tracks intervals too.
  4. Is k ever bigger than the document? Yes, and i + k must not overflow int.
     Window() does the clamping arithmetic in long for exactly that reason.

COMPLEXITY
  Part 1  time O(n*k + output), space O(n) for the mark array
  Part 2  time O(n + output),   space O(k) -- the only bounded-memory version
  Part 3  time O(n + output),   space O(m) intervals, m <= number of matches
  Part 4  time O(n/p + m) per worker plus an O(m) reduce, same space as Part 3
  In all four, the scan itself is O(n * L) for line length L: matching is the
  floor, and no amount of interval cleverness gets under it.

Related: Merge Intervals (#56) is Part 3 with the sort put back in; Sliding
Window Maximum is the same "the answer at j depends on a k-window around j"
shape solved with a deque instead of a mark array.
*/

using System.Diagnostics;

namespace CodingPatterns.ArraysStrings;

/// <summary>A closed range of line indices, <c>[Start, End]</c>, both inclusive.</summary>
public readonly record struct LineRange(int Start, int End)
{
    public int Count => End - Start + 1;

    public override string ToString() => Start == End ? $"[{Start}]" : $"[{Start}..{End}]";
}

// =============================================================================
// Part 1 -- the base problem: mark a bool[], then sweep it
// =============================================================================

public static partial class GrepWithContextLines
{
    /// <summary>
    /// The match predicate. IndexOf with an explicit <see cref="StringComparison"/>
    /// rather than <c>line.Contains(target)</c>: the two-argument overload of
    /// IndexOf(string) is CULTURE-SENSITIVE in .NET, which is both slower and
    /// capable of surprising matches, and the culture-sensitive path is precisely
    /// the one you do not want inside an O(n) scan.
    /// </summary>
    public static bool Matches(string line, string target,
                               StringComparison comparison = StringComparison.Ordinal)
        => line is not null && line.IndexOf(target, comparison) >= 0;

    /// <summary>
    /// The context window around a match, clamped to the document. Computed in
    /// long because <paramref name="linesAround"/> may legitimately be huge --
    /// <c>index + linesAround</c> in int wraps negative and the clamp then keeps
    /// the wrong end.
    /// </summary>
    public static LineRange Window(int index, int linesAround, int count)
    {
        long lo = (long)index - linesAround;
        long hi = (long)index + linesAround;
        return new LineRange((int)Math.Max(0, lo), (int)Math.Min(count - 1, hi));
    }

    /// <summary>
    /// Part 1: paint every match's window into a bool[], then emit in index order.
    /// The array is the dedupe (a line index cannot be marked twice) and the
    /// ordering (the sweep is left to right), which is why nothing else is needed.
    /// O(n*k) writes -- see <see cref="GrepFast"/> for the version that does not
    /// pay per context line.
    /// </summary>
    public static List<string> Grep(IReadOnlyList<string> lines, string target, int linesAround,
                                    StringComparison comparison = StringComparison.Ordinal)
    {
        Validate(lines, target, linesAround);

        int n = lines.Count;
        var keep = new bool[n];

        for (int i = 0; i < n; i++)
        {
            if (!Matches(lines[i], target, comparison))
                continue;

            var window = Window(i, linesAround, n);
            for (int j = window.Start; j <= window.End; j++)
                keep[j] = true;
        }

        var output = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (keep[i])
                output.Add(lines[i]);
        }

        return output;
    }

    /// <summary>
    /// Part 1, done in O(n): the difference array. Bump +1 where a window opens
    /// and -1 one past where it closes; a prefix sum above zero means "covered by
    /// at least one window". Keeps the mark-and-sweep shape while dropping the k
    /// factor, and it is the answer to give if the interviewer wants Part 3's
    /// speed without Part 3's data structure.
    /// </summary>
    public static List<string> GrepDifferenceArray(IReadOnlyList<string> lines, string target, int linesAround,
                                                   StringComparison comparison = StringComparison.Ordinal)
    {
        Validate(lines, target, linesAround);

        int n = lines.Count;
        var delta = new int[n + 1];

        for (int i = 0; i < n; i++)
        {
            if (!Matches(lines[i], target, comparison))
                continue;

            var window = Window(i, linesAround, n);
            delta[window.Start]++;
            delta[window.End + 1]--;
        }

        var output = new List<string>();
        int covering = 0;
        for (int i = 0; i < n; i++)
        {
            covering += delta[i];
            if (covering > 0)
                output.Add(lines[i]);
        }

        return output;
    }

    private static void Validate(IReadOnlyList<string> lines, string target, int linesAround)
    {
        if (lines is null)
            throw new ArgumentNullException(nameof(lines));
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (linesAround < 0)
            throw new ArgumentOutOfRangeException(nameof(linesAround), "Context size cannot be negative.");
    }
}

// =============================================================================
// Part 2 -- streaming input: O(k) memory, one line at a time
// =============================================================================

/// <summary>
/// Grep over a stream. Lines are pushed in with <see cref="Accept"/>; matching
/// lines and their context are pushed out through the sink as soon as they are
/// known to belong in the output.
///
/// State is three things and no flags:
///   _recent      ring buffer of the last k lines, for context BEFORE a match
///   _emitThrough the last index we have already committed to printing, for
///                context AFTER a match
///   _nextEmit    lowest index not yet emitted -- the entire dedupe mechanism
/// </summary>
public sealed class StreamingGrep
{
    private readonly string _target;
    private readonly int _linesAround;
    private readonly StringComparison _comparison;
    private readonly Action<int, string> _sink;
    private readonly Queue<string> _recent = new();
    private readonly List<string> _output = new();

    private int _index;                 // index the next arriving line will get
    private int _nextEmit;              // lowest index not yet emitted
    private int _emitThrough = -1;      // every index <= this must be printed

    public StreamingGrep(string target, int linesAround,
                         StringComparison comparison = StringComparison.Ordinal,
                         Action<int, string> sink = null)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (linesAround < 0)
            throw new ArgumentOutOfRangeException(nameof(linesAround));

        _target = target;
        _linesAround = linesAround;
        _comparison = comparison;
        _sink = sink;
    }

    /// <summary>Everything emitted so far, in order.</summary>
    public IReadOnlyList<string> Output => _output;

    /// <summary>Lines currently held for context-before. Never exceeds k.</summary>
    public int Buffered => _recent.Count;

    /// <summary>Feeds one line. Emits zero or more lines before returning.</summary>
    public void Accept(string line)
    {
        int i = _index++;
        bool hit = GrepWithContextLines.Matches(line, _target, _comparison);

        if (hit)
        {
            // Commit to the next k lines up front: trailing context needs no
            // buffer, only this integer.
            _emitThrough = (int)Math.Min(int.MaxValue, (long)i + _linesAround);

            // Leading context: replay the ring, skipping what was already sent.
            // max(_nextEmit, i - k) is why no line can be emitted twice, and why
            // no per-line "printed" flag is needed.
            int first = (int)Math.Max(_nextEmit, (long)i - _linesAround);
            int position = i - _recent.Count;            // index of the ring's front
            foreach (string buffered in _recent)
            {
                if (position >= first)
                    Emit(position, buffered);
                position++;
            }

            Emit(i, line);
        }
        else if (i <= _emitThrough)
        {
            Emit(i, line);                              // trailing context
        }

        if (_linesAround > 0)
        {
            _recent.Enqueue(line);
            if (_recent.Count > _linesAround)
                _recent.Dequeue();
        }
    }

    /// <summary>
    /// End of stream. Nothing is pending -- every line is decided by the time the
    /// k-th line after it arrives, and the stream ending only means fewer lines of
    /// trailing context exist. Present so callers have a symmetric API and so the
    /// buffer can be released.
    /// </summary>
    public IReadOnlyList<string> Complete()
    {
        _recent.Clear();
        return _output;
    }

    private void Emit(int index, string line)
    {
        _output.Add(line);
        _nextEmit = index + 1;
        _sink?.Invoke(index, line);
    }

    /// <summary>Convenience: run a whole sequence through the streaming engine.</summary>
    public static List<string> Run(IEnumerable<string> lines, string target, int linesAround,
                                   StringComparison comparison = StringComparison.Ordinal)
    {
        var grep = new StreamingGrep(target, linesAround, comparison);
        foreach (string line in lines)
            grep.Accept(line);
        return grep.Complete().ToList();
    }
}

// =============================================================================
// Part 3 -- intervals: stop paying for k
// =============================================================================

public static partial class GrepWithContextLines
{
    /// <summary>
    /// The merged context windows of every match, in ascending order, non-
    /// overlapping and non-adjacent. This is the real answer to the problem --
    /// the printed lines are just a loop over it.
    /// </summary>
    public static List<LineRange> ContextRanges(IReadOnlyList<string> lines, string target, int linesAround,
                                                StringComparison comparison = StringComparison.Ordinal)
    {
        Validate(lines, target, linesAround);
        return ScanRanges(lines, target, linesAround, 0, lines.Count, comparison);
    }

    /// <summary>
    /// Scans <c>[from, to)</c> and returns the merged windows of the matches found
    /// there. Windows are clamped to the WHOLE document, so a window may overhang
    /// the scanned slice -- that is what makes this usable as a parallel worker.
    /// </summary>
    private static List<LineRange> ScanRanges(IReadOnlyList<string> lines, string target, int linesAround,
                                              int from, int to, StringComparison comparison)
    {
        var ranges = new List<LineRange>();

        for (int i = from; i < to; i++)
        {
            if (Matches(lines[i], target, comparison))
                Append(ranges, Window(i, linesAround, lines.Count));
        }

        return ranges;
    }

    /// <summary>
    /// Merges <paramref name="range"/> into the tail of an already-sorted list.
    /// Callers must supply ranges in ascending Start order, which every caller
    /// here does for free -- match indices ascend, and Start is index - k.
    /// </summary>
    private static void Append(List<LineRange> ranges, LineRange range)
    {
        if (ranges.Count > 0 && range.Start <= ranges[^1].End + 1)
        {
            // Max() is belt and braces: ends ascend with starts here, so the new
            // end always wins. It costs nothing and makes the helper safe for a
            // caller whose ranges are sorted but not nested-free.
            ranges[^1] = new LineRange(ranges[^1].Start, Math.Max(ranges[^1].End, range.End));
        }
        else
        {
            ranges.Add(range);
        }
    }

    /// <summary>Part 3: same output as <see cref="Grep"/>, without the O(n*k) marking.</summary>
    public static List<string> GrepFast(IReadOnlyList<string> lines, string target, int linesAround,
                                        StringComparison comparison = StringComparison.Ordinal)
        => Materialize(lines, ContextRanges(lines, target, linesAround, comparison));

    private static List<string> Materialize(IReadOnlyList<string> lines, List<LineRange> ranges)
    {
        var output = new List<string>(ranges.Sum(r => r.Count));
        foreach (var range in ranges)
        {
            for (int i = range.Start; i <= range.End; i++)
                output.Add(lines[i]);
        }
        return output;
    }
}

// =============================================================================
// Part 4 -- multithreading: map to intervals, reduce by merging
// =============================================================================

public static partial class GrepWithContextLines
{
    /// <summary>
    /// Part 4: split the document into <paramref name="workers"/> contiguous
    /// chunks, scan them in parallel, and merge the per-chunk interval lists in
    /// chunk order. Byte-for-byte identical output to the sequential version, by
    /// construction rather than by luck.
    /// </summary>
    public static List<LineRange> ContextRangesParallel(
        IReadOnlyList<string> lines, string target, int linesAround,
        int workers = 0, StringComparison comparison = StringComparison.Ordinal)
    {
        Validate(lines, target, linesAround);

        int n = lines.Count;
        if (n == 0)
            return new List<LineRange>();

        if (workers <= 0)
            workers = Environment.ProcessorCount;
        workers = Math.Clamp(workers, 1, n);

        // One slot per chunk. Distinct threads write distinct elements, so this
        // needs no lock -- and reading it back in id order is what keeps both the
        // merge linear and the output deterministic.
        var perChunk = new List<LineRange>[workers];
        int size = n / workers;
        int remainder = n % workers;

        Parallel.For(0, workers, c =>
        {
            int start = c * size + Math.Min(c, remainder);
            int end = start + size + (c < remainder ? 1 : 0);
            perChunk[c] = ScanRanges(lines, target, linesAround, start, end, comparison);
        });

        var merged = new List<LineRange>();
        foreach (var chunk in perChunk)
        {
            foreach (var range in chunk)
                Append(merged, range);          // no sort: chunk order IS start order
        }

        return merged;
    }

    /// <summary>Part 4, materialized. The reduce stays single-threaded: it is O(m).</summary>
    public static List<string> GrepParallel(IReadOnlyList<string> lines, string target, int linesAround,
                                            int workers = 0,
                                            StringComparison comparison = StringComparison.Ordinal)
        => Materialize(lines, ContextRangesParallel(lines, target, linesAround, workers, comparison));
}

// =============================================================================
// Part 5 -- tests
// =============================================================================

public static partial class GrepWithContextLines
{
    private static readonly string[] Sample =
    {
        "good morning",
        "hello there",
        "my name is Alex",
        "my friend is albert",
        "it is nice to meet you Alex",
    };

    /// <summary>
    /// The definition, computed the other way round: line j is kept iff SOME line
    /// in [j-k, j+k] matches. Pull instead of push -- it shares no code with any
    /// implementation above, which is what makes agreement meaningful.
    /// </summary>
    private static List<string> Reference(IReadOnlyList<string> lines, string target, int linesAround,
                                          StringComparison comparison = StringComparison.Ordinal)
    {
        var output = new List<string>();

        for (int j = 0; j < lines.Count; j++)
        {
            var window = Window(j, linesAround, lines.Count);
            for (int i = window.Start; i <= window.End; i++)
            {
                if (Matches(lines[i], target, comparison))
                {
                    output.Add(lines[j]);
                    break;
                }
            }
        }

        return output;
    }

    private static string Show(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        return list.Count == 0 ? "(nothing)" : string.Join(" | ", list);
    }

    public static void Main()
    {
        Console.WriteLine("== the published example ==");
        for (int i = 0; i < Sample.Length; i++)
            Console.WriteLine($"  {i}: {Sample[i]}");

        var result = Grep(Sample, "Alex", 1);
        Console.WriteLine($"  target \"Alex\", k=1 -> {result.Count} lines:");
        foreach (string line in result)
            Console.WriteLine($"      {line}");
        var sampleRanges = ContextRanges(Sample, "Alex", 1);
        Console.WriteLine($"  merged windows: {string.Join(" ", sampleRanges)}");
        Console.WriteLine("  Lines 2 and 4 match; their windows are [1..3] and [3..4] (clamped to the last");
        Console.WriteLine("  line). They overlap at line 3, so the merge gives one block [1..4] and");
        Console.WriteLine("  \"my friend is albert\" prints once. Note it is CONTEXT, not a match --");
        Console.WriteLine("  \"albert\" does not contain \"Alex\".");

        Console.WriteLine();
        Console.WriteLine("== all four implementations, same input, same answer ==");
        Console.WriteLine($"  {"Part 1  mark a bool[]",-34}{Show(Grep(Sample, "Alex", 1))}");
        Console.WriteLine($"  {"Part 1b difference array",-34}{Show(GrepDifferenceArray(Sample, "Alex", 1))}");
        Console.WriteLine($"  {"Part 2  streaming, O(k) memory",-34}{Show(StreamingGrep.Run(Sample, "Alex", 1))}");
        Console.WriteLine($"  {"Part 3  merged intervals",-34}{Show(GrepFast(Sample, "Alex", 1))}");
        Console.WriteLine($"  {"Part 4  parallel, 4 workers",-34}{Show(GrepParallel(Sample, "Alex", 1, workers: 4))}");

        Console.WriteLine();
        Console.WriteLine("== the streaming trace: when each line is actually emitted ==");

        var traced = new List<(int Arrived, int Emitted, string Line)>();
        int arrived = 0;
        var streaming = new StreamingGrep("Alex", 1, sink: (index, line) => traced.Add((arrived, index, line)));
        for (; arrived < Sample.Length; arrived++)
        {
            int before = traced.Count;
            streaming.Accept(Sample[arrived]);
            var fresh = traced.Skip(before).Select(t => $"#{t.Emitted} \"{t.Line}\"").ToList();
            Console.WriteLine($"  line {arrived} arrives -> emits: {(fresh.Count == 0 ? "(held)" : string.Join(", ", fresh))}"
                              + $"   buffered={streaming.Buffered}");
        }
        Console.WriteLine("  Line 1 could not be emitted when it arrived -- nothing had matched yet. It");
        Console.WriteLine("  is released by the match on line 2, k lines later. That lag is the problem,");
        Console.WriteLine("  not the implementation: no streaming solution can beat O(k) latency.");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var repeated = new[] { "x", "hit", "x", "x", "hit", "x" };
        Console.WriteLine($"  {"k = 0 (matches only):",-42}{Show(GrepFast(Sample, "Alex", 0))}");
        Console.WriteLine($"  {"k >= n (whole document):",-42}{Show(GrepFast(Sample, "Alex", 99))}");
        Console.WriteLine($"  {"no match at all:",-42}{Show(GrepFast(Sample, "zebra", 2))}");
        Console.WriteLine($"  {"match on the first line:",-42}{Show(GrepFast(Sample, "good", 1))}");
        Console.WriteLine($"  {"match on the last line:",-42}{Show(GrepFast(Sample, "meet", 1))}");
        Console.WriteLine($"  {"empty document:",-42}{Show(GrepFast(Array.Empty<string>(), "Alex", 3))}");
        Console.WriteLine($"  {"empty target (matches everything):",-42}{Show(GrepFast(Sample, "", 0))}");
        Console.WriteLine($"  {"identical lines at different indices:",-42}{Show(GrepFast(repeated, "hit", 1))}");
        Console.WriteLine($"  {"case-insensitive:",-42}{Show(GrepFast(Sample, "ALEX", 0, StringComparison.OrdinalIgnoreCase))}");
        Console.WriteLine($"  {"windows for the repeated case:",-42}{string.Join(" ", ContextRanges(repeated, "hit", 1))}");
        Console.WriteLine("  The repeated case is the dedupe test with teeth: six lines, four of them the");
        Console.WriteLine("  string \"x\", and the answer keeps three of those four. Dedupe by content and");
        Console.WriteLine("  you print one. Its two windows [0..2] and [3..5] never overlap -- they abut,");
        Console.WriteLine("  and the `+ 1` in Append is what collapses them into the single block [0..5].");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-check against the definition ==");
        Console.WriteLine("  keep(j) == some i in [j-k, j+k] matches -- computed by scanning OUT from");
        Console.WriteLine("  each line, which is the definition read backwards and shares no code.");

        var rng = new Random(20260810);
        bool markAgrees = true, deltaAgrees = true, streamAgrees = true;
        bool fastAgrees = true, parallelAgrees = true;
        bool rangesSorted = true, rangesDisjoint = true, rangesMatchOutput = true;
        int trials = 0, linesScanned = 0;

        for (int trial = 0; trial < 3000; trial++)
        {
            // A tiny alphabet on purpose: dense matches and heavily overlapping
            // windows are where the merge and the dedupe break.
            int n = rng.Next(0, 25);
            var document = new string[n];
            for (int i = 0; i < n; i++)
                document[i] = $"{(char)('a' + rng.Next(3))}{(char)('a' + rng.Next(3))}";

            string target = rng.Next(4) == 0 ? "" : $"{(char)('a' + rng.Next(3))}";
            int k = rng.Next(0, 6);

            var expected = Reference(document, target, k);

            markAgrees &= Grep(document, target, k).SequenceEqual(expected);
            deltaAgrees &= GrepDifferenceArray(document, target, k).SequenceEqual(expected);
            streamAgrees &= StreamingGrep.Run(document, target, k).SequenceEqual(expected);
            fastAgrees &= GrepFast(document, target, k).SequenceEqual(expected);
            parallelAgrees &= GrepParallel(document, target, k, workers: 1 + rng.Next(8)).SequenceEqual(expected);

            var ranges = ContextRanges(document, target, k);
            for (int i = 0; i < ranges.Count; i++)
            {
                rangesSorted &= ranges[i].Start <= ranges[i].End;
                if (i > 0)
                {
                    // Merged means the next window starts at least two past the
                    // previous end: overlapping AND adjacent both got absorbed.
                    rangesDisjoint &= ranges[i].Start > ranges[i - 1].End + 1;
                    rangesSorted &= ranges[i].Start > ranges[i - 1].Start;
                }
            }
            rangesMatchOutput &= ranges.Sum(r => r.Count) == expected.Count;

            trials++;
            linesScanned += n;
        }

        Console.WriteLine($"  {$"{trials:n0} random documents, {linesScanned:n0} lines",-56}");
        Console.WriteLine($"  {"Part 1  bool[] marking == definition:",-56}{markAgrees}");
        Console.WriteLine($"  {"Part 1b difference array == definition:",-56}{deltaAgrees}");
        Console.WriteLine($"  {"Part 2  streaming == definition:",-56}{streamAgrees}");
        Console.WriteLine($"  {"Part 3  merged intervals == definition:",-56}{fastAgrees}");
        Console.WriteLine($"  {"Part 4  parallel (1-8 workers) == definition:",-56}{parallelAgrees}");
        Console.WriteLine($"  {"intervals ascend and never nest:",-56}{rangesSorted}");
        Console.WriteLine($"  {"intervals are disjoint AND non-adjacent:",-56}{rangesDisjoint}");
        Console.WriteLine($"  {"total interval length == lines printed:",-56}{rangesMatchOutput}");

        Console.WriteLine();
        Console.WriteLine("== streaming holds O(k) lines, whatever the document size ==");

        var long_ = Enumerable.Range(0, 100_000).Select(i => i % 7 == 0 ? "needle here" : "filler").ToList();
        var bounded = new StreamingGrep("needle", 3);
        int peak = 0;
        foreach (string line in long_)
        {
            bounded.Accept(line);
            peak = Math.Max(peak, bounded.Buffered);
        }
        Console.WriteLine($"  {long_.Count:n0} lines streamed, k=3 -> peak buffered lines: {peak}");
        Console.WriteLine($"  output size {bounded.Output.Count:n0}, and the buffer never grew past k.");

        Console.WriteLine();
        Console.WriteLine("== Part 3: why marking hurts when k is large ==");

        int denseN = 20_000, denseK = 2_000;
        var dense = Enumerable.Range(0, denseN).Select(i => $"line {i} needle").ToList();
        long markWrites = dense.Select((_, i) => (long)Window(i, denseK, denseN).Count).Sum();

        Grep(dense, "needle", 4);                      // warm the JIT
        GrepFast(dense, "needle", 4);

        var clock = Stopwatch.StartNew();
        var marked = Grep(dense, "needle", denseK);
        double markMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        var intervalled = GrepFast(dense, "needle", denseK);
        double fastMs = clock.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  {denseN:n0} lines, EVERY line matches, k={denseK:n0}");
        Console.WriteLine($"  {"bool[] marking:",-26}{markMs,8:F1} ms   ({markWrites:n0} writes to produce {marked.Count:n0} lines)");
        Console.WriteLine($"  {"merged intervals:",-26}{fastMs,8:F1} ms   (1 interval, {intervalled.Count:n0} lines)");
        Console.WriteLine($"  {"same output:",-26}{marked.SequenceEqual(intervalled),8}");
        Console.WriteLine("  Every window here covers every other window, so the marking loop rewrites");
        Console.WriteLine("  the same cells ~2k times over. The interval version merges them into one.");

        Console.WriteLine();
        Console.WriteLine("== Part 4: chunk, scan in parallel, merge ==");

        int bigN = 300_000;
        var big = new List<string>(bigN);
        var filler = new Random(7);
        for (int i = 0; i < bigN; i++)
        {
            // Long-ish lines so the scan, not the allocation, is what we time.
            string body = new string((char)('a' + filler.Next(26)), 60);
            big.Add(i % 5_000 == 0 ? $"{body} NEEDLE {i}" : $"{body} {i}");
        }

        ContextRangesParallel(big, "NEEDLE", 2, workers: 2);        // warm up
        ContextRanges(big, "NEEDLE", 2);

        clock.Restart();
        var sequential = ContextRanges(big, "NEEDLE", 2);
        double sequentialMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        var parallel = ContextRangesParallel(big, "NEEDLE", 2);
        double parallelMs = clock.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  {bigN:n0} lines, {sequential.Count} matches, k=2, {Environment.ProcessorCount} cores");
        Console.WriteLine($"  {"sequential scan:",-26}{sequentialMs,8:F1} ms");
        Console.WriteLine($"  {"parallel scan:",-26}{parallelMs,8:F1} ms   ({sequentialMs / Math.Max(parallelMs, 0.001):F1}x)");
        Console.WriteLine($"  {"identical intervals:",-26}{parallel.SequenceEqual(sequential),8}");

        bool deterministic = true;
        for (int workers = 1; workers <= 16 && deterministic; workers++)
            deterministic &= ContextRangesParallel(big, "NEEDLE", 2, workers).SequenceEqual(sequential);

        Console.WriteLine($"  {"same for every worker count 1..16:",-26}{deterministic,8}");
        Console.WriteLine("  Determinism is structural, not lucky: chunks are contiguous, each worker");
        Console.WriteLine("  writes its own array slot, and the reduce reads those slots in chunk order.");

        Console.WriteLine();
        Console.WriteLine("== the boundary case that makes workers return intervals, not lines ==");

        var boundary = new[] { "a", "b", "c", "MATCH", "e", "f", "g", "h" };
        Console.WriteLine($"  document: {string.Join(" ", boundary)}   target MATCH, k=2");
        for (int workers = 1; workers <= 4; workers++)
        {
            var ranges = ContextRangesParallel(boundary, "MATCH", 2, workers);
            Console.WriteLine($"  {workers} worker(s): {string.Join(" ", ranges),-12} -> {Show(Materialize(boundary, ranges))}");
        }
        Console.WriteLine("  With 4 workers the chunks are 2 lines each, so the match ends chunk 1 while");
        Console.WriteLine("  its context spills into chunks 0 and 2. Because the worker reports [1..5]");
        Console.WriteLine("  rather than text, the coordinator prints lines that worker never looked at --");
        Console.WriteLine("  and prints them exactly once.");
    }
}

// ---- Notes for the follow-up questions ----
//
// "Print it, don't return it -- the file is 100 GB."
//     Then the interval list is the wrong final step, because Materialize()
//     builds the whole output in memory. Two clean answers: (a) the streaming
//     engine of Part 2, which is already O(k) memory and writes as it goes; or
//     (b) keep the interval scan but have it yield ranges lazily (IEnumerable of
//     LineRange) and let the caller seek and print. Part 2 is the honest answer
//     for a pipe, Part 3 for a seekable file where you also want to skip cheaply.
//
// "Real grep prints a `--` separator between non-adjacent blocks."
//     Free once you have Part 3: one interval is one block, so print the
//     separator between consecutive intervals. That is exactly why merging
//     ADJACENT windows (the `+ 1` in Append) matters -- without it, two touching
//     windows would print a separator in the middle of a contiguous run.
//
// "Different amounts of before/after context (grep -A / -B)."
//     Nothing structural changes: the window becomes [i - before, i + after].
//     In the streaming version the ring buffer sizes to `before` and emitThrough
//     uses `after`; they were never the same variable, only the same value.
//
// "The match is a regex, not a substring."
//     Only the predicate changes. Worth saying that a compiled Regex with
//     RegexOptions.Compiled amortizes across n lines, and that for a large
//     literal alternation Aho-Corasick beats k separate scans. The context logic
//     never learns what a match is -- keep it behind Func<string, bool> and the
//     four parts stay untouched.
//
// "How would you parallelize the STREAM?"
//     You mostly cannot, and that is the right answer: the output order is the
//     input order, so any parallelism has to be inside a window of lines that are
//     already buffered. What does parallelize is the MATCHING -- hand blocks of
//     lines to a worker pool, get back per-block match indices, and have a single
//     ordering stage do the window/merge/emit. Match testing is the expensive
//     part (O(n*L)); the interval bookkeeping is O(n) integer work and belongs on
//     one thread.
//
// "Chunk by bytes, not by lines -- we are reading a file, not a List."
//     The real map-reduce version. Split the file at byte offsets, then have each
//     worker skip forward to the first newline and read past its end offset to
//     finish the last line, so every line is owned by exactly one worker. Line
//     NUMBERS then need a second pass (or a per-chunk line count reduced by
//     prefix sum) since a worker cannot know its starting line number -- which is
//     the one thing the List-of-lines version hands you for free.
//
// "Is Parallel.For over chunks really the right shape?"
//     For a CPU-bound scan over an in-memory list, yes: contiguous chunks give
//     sequential memory access, and one result slot per chunk means no
//     contention. The failure mode to name is uneven work -- if matches cluster,
//     one worker's interval list is huge while others return empty. Fix by
//     over-partitioning (say 4x the core count) and letting the scheduler
//     load-balance; the reduce is still linear because chunk ids still ascend.
//
// "How would you test it?"
//     The cross-check in Run() is the answer, and its shape is the point: every
//     implementation is checked against the DEFINITION ("keep j iff a match is
//     within k of j") evaluated by scanning outward from each line, not against
//     another implementation. Random documents over a 3-letter alphabet with
//     k up to 6 make windows overlap constantly, which is where the merge and
//     the streaming dedupe fail. Add the two structural invariants -- intervals
//     strictly ascending and non-adjacent, total interval length == lines
//     printed -- and a duplicate emit cannot survive.
