/*
RecordCollection Sort Colors (Sort Colors / LeetCode 75, wrapped in an object API)

A RecordCollection holds records each carrying one colour: red=0, green=1,
blue=2. Reorder it in place so all reds come first, then greens, then blues.

The twist over plain LeetCode 75 is the API. You are NOT handed an int[]. You
get an object exposing exactly three things:

    int  Count            how many records there are
    int  GetColor(int i)  the colour of record i
    void Swap(int i, int j)

That is the whole point of the variant. Everything below is expressed in terms
of GetColor/Swap -- no shadow array, no copy-out-sort-copy-back, no counting
sort that writes colours back (which would be a *rewrite*, not a swap, and
would silently destroy whatever else each record carries).

QUESTION TO ASK THE INTERVIEWER FIRST
  "Does the collection expose its size?" If there is no Count/len(), the size
  has to arrive some other way -- passed as an argument, or discovered by
  probing GetColor until it throws. Do not silently assume. Here we assume
  Count exists; SortColors is the only place it is read, exactly once, so
  swapping to a `SortColors(collection, int n)` signature is a one-line change.

APPROACH -- Dutch National Flag, one pass, three pointers

  [0, low)      settled 0s
  [low, mid)    settled 1s
  [mid, high]   unknown, still to classify
  (high, n)     settled 2s

  Read the colour at `mid`:
    0 -> it belongs at the front. Swap(low, mid), low++, mid++.
         Safe to advance mid too: whatever came back from `low` was either a 1
         (already classified, belongs in the middle band) or was mid itself
         when low == mid. It can never be a 2 -- 2s only ever live past `high`.
    1 -> already in the middle band. mid++.
    2 -> Swap(mid, high), high--. Do NOT advance mid: the value swapped in came
         from the *unknown* region and has not been looked at yet. This is the
         classic bug -- advancing mid here strands a 0 in the middle.

  Loop while mid <= high. Using `<` instead of `<=` skips the final element.

WHY THIS IS STILL O(n) EVEN THOUGH SOME INDICES GET RE-READ
  The re-read at `mid` after a 2-swap looks like it could loop forever, but
  measure the right thing: the size of the unknown window, high - mid + 1.
  Every single iteration of the loop either advances mid or retreats high, so
  that window shrinks by exactly one every time -- there is no branch that
  leaves both pointers where they were. The window starts at n and the loop
  ends when it hits 0, so the loop body runs at most n times, and does O(1)
  work (one GetColor, at most one Swap) per run. Re-reading an index does not
  cost you an extra iteration; it *is* the iteration that consumed a step of
  `high`. The test below asserts this empirically: GetColor is called at most
  n times and Swap at most n times.

Complexity
  Time:  O(n) -- at most n loop iterations, O(1) each.
  Space: O(1) -- three ints. The records never leave the collection.

Why not the two-pass counting sort? Count the 0s/1s/2s, then overwrite. It is
also O(n) and is a perfectly good answer for a bare int[] -- but here it fails
the brief twice over: it needs a write API the collection does not have
(SetColor), and reconstructing records from colour counts throws away every
other field on the record. Swapping moves whole records, which is why the
tests check that the multiset of record ids is preserved, not just the colours.
*/

namespace CodingPatterns.ArraysStrings;

/// <summary>One record: an identity plus the colour we sort on.</summary>
public sealed record ColorRecord(int Id, int Color);

/// <summary>
/// The wrapper the problem hands you. GetColor/Swap are "already implemented";
/// the counters exist only so the tests can prove the O(n) claim.
/// </summary>
public sealed class RecordCollection
{
    private readonly ColorRecord[] _records;

    public RecordCollection(params int[] colors)
    {
        _records = colors.Select((c, i) => new ColorRecord(i, c)).ToArray();
    }

    public int Count => _records.Length;

    public int GetColorCalls { get; private set; }
    public int SwapCalls { get; private set; }

    public int GetColor(int i)
    {
        GetColorCalls++;
        return _records[i].Color;
    }

    public void Swap(int i, int j)
    {
        SwapCalls++;
        (_records[i], _records[j]) = (_records[j], _records[i]);
    }

    // Test-only inspection. The solution must not touch these.
    public int[] Colors() => _records.Select(r => r.Color).ToArray();
    public int[] Ids() => _records.Select(r => r.Id).ToArray();
    public void ResetCounters() { GetColorCalls = 0; SwapCalls = 0; }
}

public class RecordCollectionSortColors
{
    /// <summary>
    /// Dutch National Flag partition, in place, using only Count/GetColor/Swap.
    /// </summary>
    public static void SortColors(RecordCollection collection)
    {
        int low = 0;                        // next slot for a 0
        int mid = 0;                        // cursor over the unknown region
        int high = collection.Count - 1;    // next slot for a 2

        // Invariant: [0,low) are 0s, [low,mid) are 1s, (high,n) are 2s.
        // The unknown window [mid,high] shrinks by one on every iteration.
        while (mid <= high)
        {
            switch (collection.GetColor(mid))
            {
                case 0:
                    collection.Swap(low, mid);
                    low++;
                    mid++;
                    break;

                case 1:
                    mid++; // already in the right band
                    break;

                default: // 2
                    collection.Swap(mid, high);
                    high--; // mid stays put: the incoming value is unclassified
                    break;
            }
        }
    }

    public static void Run()
    {
        Console.WriteLine("RecordCollection Sort Colors (LeetCode 75 variant)");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        var cases = new (string Name, int[] Input)[]
        {
            ("empty collection",        Array.Empty<int>()),
            ("single record",           new[] { 1 }),
            ("all red",                 new[] { 0, 0, 0, 0 }),
            ("all green",               new[] { 1, 1, 1 }),
            ("all blue",                new[] { 2, 2, 2, 2, 2 }),
            ("already sorted",          new[] { 0, 0, 1, 1, 2, 2 }),
            ("reverse sorted",          new[] { 2, 2, 1, 1, 0, 0 }),
            ("repeated middle colour",  new[] { 2, 1, 1, 1, 1, 0 }),
            ("no greens at all",        new[] { 2, 0, 2, 0, 2, 0 }),
            ("greens on the outside",   new[] { 1, 2, 0, 1 }),
            ("single 2 at the front",   new[] { 2, 0, 0, 0 }),
            ("single 0 at the back",    new[] { 2, 2, 2, 0 }),
            ("classic LeetCode sample", new[] { 2, 0, 2, 1, 1, 0 }),
        };

        int failures = 0;

        foreach (var (name, input) in cases)
            failures += CheckCase(name, input);

        // Exhaustive: every colour string of length 0..7 over {0,1,2}.
        // 3^0 + ... + 3^7 = 3280 collections, so the pointer logic has nowhere
        // left to hide an off-by-one.
        int exhaustive = 0, exhaustiveFailures = 0;
        for (int n = 0; n <= 7; n++)
        {
            foreach (var combo in Combinations(n))
            {
                exhaustive++;
                exhaustiveFailures += CheckCase(null, combo);
            }
        }
        Console.WriteLine();
        Console.WriteLine(exhaustiveFailures == 0
            ? $"  [OK]   exhaustive: all {exhaustive} colour strings of length 0..7"
            : $"  [FAIL] exhaustive: {exhaustiveFailures}/{exhaustive} colour strings wrong");
        failures += exhaustiveFailures;

        Console.WriteLine();
        if (failures > 0)
            throw new Exception($"{failures} case(s) failed.");
        Console.WriteLine("All tests passed.");
    }

    /// <summary>
    /// Runs one case and verifies four things: colours end up sorted, the
    /// records are a permutation of the originals (nothing overwritten or
    /// invented), and neither GetColor nor Swap is called more than n times.
    /// Pass name = null to stay quiet unless it fails.
    /// </summary>
    private static int CheckCase(string? name, int[] input)
    {
        var collection = new RecordCollection(input);
        int n = input.Length;

        SortColors(collection);

        int[] actual = collection.Colors();
        int[] expected = input.OrderBy(c => c).ToArray();

        bool sorted = actual.SequenceEqual(expected);
        bool permutation = collection.Ids().OrderBy(i => i).SequenceEqual(Enumerable.Range(0, n));
        bool linearReads = collection.GetColorCalls <= n;
        bool linearSwaps = collection.SwapCalls <= n;
        bool ok = sorted && permutation && linearReads && linearSwaps;

        if (ok && name is null)
            return 0;

        string label = name ?? $"[{string.Join(",", input)}]";
        Console.WriteLine($"  [{(ok ? "OK" : "FAIL")}] {label,-24} " +
                          $"[{string.Join(",", input)}] -> [{string.Join(",", actual)}]  " +
                          $"reads={collection.GetColorCalls} swaps={collection.SwapCalls} (n={n})");

        if (!ok)
        {
            if (!sorted)
                Console.WriteLine($"         expected [{string.Join(",", expected)}]");
            if (!permutation)
                Console.WriteLine($"         records are not a permutation: ids [{string.Join(",", collection.Ids())}]");
            if (!linearReads)
                Console.WriteLine($"         GetColor called {collection.GetColorCalls} times, expected <= {n}");
            if (!linearSwaps)
                Console.WriteLine($"         Swap called {collection.SwapCalls} times, expected <= {n}");
        }

        return ok ? 0 : 1;
    }

    /// <summary>Every length-n string over {0,1,2}, counted in base 3.</summary>
    private static IEnumerable<int[]> Combinations(int n)
    {
        int total = (int)Math.Pow(3, n);
        for (int code = 0; code < total; code++)
        {
            var combo = new int[n];
            int rest = code;
            for (int i = 0; i < n; i++)
            {
                combo[i] = rest % 3;
                rest /= 3;
            }
            yield return combo;
        }
    }
}
