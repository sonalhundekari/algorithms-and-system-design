// Max Credits with K Classes (weighted interval scheduling, count-capped)
// Difficulty: Hard (LeetCode 1751; Medium once you see it is 1235 + one axis)
// Pattern: sort by end time, binary-search the predecessor, 2D DP over (class, count)
//
// Classes are (start, end, credit). Pick at most K of them, no two overlapping,
// and maximize total credit.
//
// FIRST, THE MISDIRECTION. "Classes" and "K" pull people straight to Course
// Schedule (LC 207 / 210) and they start building an adjacency list. There are
// no prerequisites here and no graph -- the only relation between two classes
// is whether their time ranges collide. This is interval scheduling. Say that
// out loud early; the rest of the round is easy once the wrong frame is dropped.
//
// WHY NOT GREEDY. Both obvious greedies lose:
//
//     by credit/hour, K=1:  (0,1,10) -> density 10, (0,10,50) -> density 5.
//                           Greedy takes 10, the answer is 50.
//     by raw credit, K=2:   (0,10,10) beats each of (0,4,6) and (5,9,6) alone,
//                           but the pair is 12.
//
// Taking a class costs you the *time window*, and the window's worth depends on
// what else could have filled it. That is a DP, not a sort.
//
// THE DP. Sort by end time, so "what is still available after class i" is a
// prefix of the same array. With classes 1..n in that order:
//
//     p(i) = number of classes whose end time is early enough to precede i
//     dp[i][j] = max credit using the first i classes, at most j of them
//              = max( dp[i-1][j],                    skip i
//                     credit[i] + dp[p(i)][j-1] )    take i
//
// The whole trick of the sorted-by-end order is that p(i) is a *prefix length*,
// found with one binary search, so "take i" jumps straight to a solved
// subproblem instead of re-scanning for compatible classes.
//
// THE BOUNDARY RULE -- ASK. Does a class ending at t conflict with one starting
// at t? Two different answers, two different binary searches:
//
//     half-open times (default, LC 1235):  end <= start is fine
//                                          p(i) = UpperBound(ends, start_i)
//     inclusive end DAYS (LC 1751):        need end < start, a shared day is a
//                                          conflict, p(i) = LowerBound(ends, start_i)
//
// UpperBound counts ends <= start; LowerBound counts ends < start. That one call
// is the entire difference between the two problems -- get it wrong and you fail
// exactly the tests where intervals touch.
//
// Time:  O(N log N + N*K)      sort + one binary search per class + the table
// Space: O(N) rolling over the K axis, O(N*K) if you want the schedule back
//
// Edge cases: K = 0 -> 0. K >= N -> the cap is inert, clamp it to N so the DP
// does not do K wasted passes. Everything overlapping -> falls out as the single
// best credit. Negative credits never get taken because "skip" is always an
// option. A zero-length class (start == end) satisfies `end <= start` against
// itself under the half-open rule, so p(i) is clamped to i -- otherwise the DP
// takes such a class twice.
//
// If the interviewer DROPS the K cap, delete the j axis and it is LC 1235
// (JobScheduling below, 1D). If they drop the credits and ask for the maximum
// *number* of events, it stops being a DP entirely -- that is the greedy
// min-heap-by-end-day problem (LC 1353), not this.

namespace CodingPatterns.DynamicProgramming;

public class MaxCreditsKClasses
{
    public record struct Class(int Start, int End, int Credit);

    /// <summary>
    /// Max total credit from at most k non-overlapping classes.
    /// Rolls the DP over the count axis: row j only ever reads row j-1, so two
    /// length-(n+1) arrays are enough. O(N log N + N*K) time, O(N) space.
    /// </summary>
    public long Solve(IReadOnlyList<Class> classes, int k, bool inclusiveEnd = false)
    {
        int n = classes.Count;
        k = Math.Min(k, n);                       // K >= N makes the cap inert
        if (n == 0 || k <= 0) return 0;

        // Sort by end time and precompute p(i) for every class: the number of
        // classes that finish early enough to be followed by ordered[i] -- i.e. the
        // length of the still-usable prefix, which is the row index the DP jumps to.
        var ordered = classes.OrderBy(c => c.End).ThenBy(c => c.Start).ToArray();
        var ends = ordered.Select(c => c.End).ToArray();

        var prev = new int[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            // ends <= start when a shared boundary is legal, ends < start when not.
            // LowerBound counts entries strictly less than Start; UpperBound counts
            // those less than or equal.
            int lo = 0, hi = ends.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                bool before = inclusiveEnd ? ends[mid] < ordered[i].Start : ends[mid] <= ordered[i].Start;
                if (before) lo = mid + 1;
                else hi = mid;
            }

            // Math.Min keeps a class from being its own predecessor: a zero-length
            // class (Start == End) satisfies `end <= start` against itself, and
            // without the clamp the DP would happily take it twice.
            prev[i] = Math.Min(lo, i);
        }


        var row = new long[n + 1];                // j = 0: nothing may be taken
        var cur = new long[n + 1];
        for (int j = 1; j <= k; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                long take = ordered[i - 1].Credit + row[prev[i - 1]];
                cur[i] = Math.Max(cur[i - 1], take);
            }
            (row, cur) = (cur, row);
        }
        return row[n];
    }

    /// <summary>
    /// (best credit, the classes that achieve it) -- full table, then walk back.
    /// Same recurrence, but keeps all N*K cells so the choices can be recovered.
    /// Reach for this when asked "which classes?" and not just "how much?".
    /// </summary>
    public (long Credit, List<Class> Schedule) SolveWithSchedule(
        IReadOnlyList<Class> classes, int k, bool inclusiveEnd = false)
    {
        int n = classes.Count;
        k = Math.Min(k, n);
        if (n == 0 || k <= 0) return (0, new List<Class>());

        // Sort by end time and precompute p(i) for every class: the number of
        // classes that finish early enough to be followed by ordered[i] -- i.e. the
        // length of the still-usable prefix, which is the row index the DP jumps to.
        var ordered = classes.OrderBy(c => c.End).ThenBy(c => c.Start).ToArray();
        var ends = ordered.Select(c => c.End).ToArray();

        var prev = new int[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            // ends <= start when a shared boundary is legal, ends < start when not.
            // LowerBound counts entries strictly less than Start; UpperBound counts
            // those less than or equal.
            int lo = 0, hi = ends.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                bool before = inclusiveEnd ? ends[mid] < ordered[i].Start : ends[mid] <= ordered[i].Start;
                if (before) lo = mid + 1;
                else hi = mid;
            }

            // Math.Min keeps a class from being its own predecessor: a zero-length
            // class (Start == End) satisfies `end <= start` against itself, and
            // without the clamp the DP would happily take it twice.
            prev[i] = Math.Min(lo, i);
        }


        var dp = new long[n + 1, k + 1];
        for (int i = 1; i <= n; i++)
        {
            int credit = ordered[i - 1].Credit, p = prev[i - 1];
            for (int j = 1; j <= k; j++)
                dp[i, j] = Math.Max(dp[i - 1, j], credit + dp[p, j - 1]);
        }

        // Backtrack: if the cell matches "skip", it was a skip; otherwise it was
        // a take, so drop to the predecessor row and spend one of the j slots.
        var chosen = new List<Class>();
        for (int i = n, j = k; i > 0 && j > 0;)
        {
            if (dp[i, j] == dp[i - 1, j])
            {
                i--;
            }
            else
            {
                chosen.Add(ordered[i - 1]);
                (i, j) = (prev[i - 1], j - 1);
            }
        }
        chosen.Reverse();
        return (dp[n, k], chosen);
    }

    /// <summary>
    /// LeetCode 1751 -- events[i] = [startDay, endDay, value], at most k attended.
    /// End days are INCLUSIVE, so [1,2] and [2,3] collide over day 2.
    /// </summary>
    public int MaxValue(int[][] events, int k) =>
        (int)Solve(events.Select(e => new Class(e[0], e[1], e[2])).ToArray(), k, inclusiveEnd: true);

    /// <summary>
    /// LeetCode 1235 -- the same problem with no cap, so the j axis disappears.
    /// Have this ready: interviewers like to remove K mid-round and watch whether
    /// you rebuild from scratch or just delete a loop.
    /// </summary>
    public static int JobScheduling(int[] startTime, int[] endTime, int[] profit)
    {
        int n = startTime.Length;
        var jobs = Enumerable.Range(0, n)
            .Select(i => (End: endTime[i], Start: startTime[i], Profit: profit[i]))
            .OrderBy(j => j.End).ThenBy(j => j.Start)
            .ToArray();
        var ends = jobs.Select(j => j.End).ToArray();

        var dp = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            // Count of ends <= this job's start: the index past the last job that
            // can still be followed by this one.
            int lo = 0, hi = ends.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (ends[mid] <= jobs[i - 1].Start) lo = mid + 1;
                else hi = mid;
            }

            dp[i] = Math.Max(dp[i - 1], jobs[i - 1].Profit + dp[lo]);
        }
        return dp[n];
    }

    public static void Run()
    {
        var sol = new MaxCreditsKClasses();
        var classes = new[]
        {
            new Class(1, 3, 50), new Class(2, 5, 20), new Class(4, 6, 70), new Class(6, 9, 30),
        };

        // K edge cases.
        Console.WriteLine(sol.Solve(classes, 0));      // 0    cap of zero
        Console.WriteLine(sol.Solve(classes, 1));      // 70   best single
        Console.WriteLine(sol.Solve(classes, 2));      // 120  (1,3,50) + (4,6,70)
        Console.WriteLine(sol.Solve(classes, 3));      // 150  ...+ (6,9,30)
        Console.WriteLine(sol.Solve(classes, 99));     // 150  cap inert past N

        // The greedies from the header, both beaten by the DP.
        Console.WriteLine(sol.Solve(new[] { new Class(0, 1, 10), new Class(0, 10, 50) }, 1));   // 50, not 10
        var creditTrap = new[] { new Class(0, 10, 10), new Class(0, 4, 6), new Class(5, 9, 6) };
        Console.WriteLine(sol.Solve(creditTrap, 2));                                            // 12, not 10

        // Everything overlaps -> the single best credit, whatever K says.
        var stacked = new[] { new Class(0, 10, 5), new Class(1, 9, 40), new Class(2, 8, 12) };
        Console.WriteLine(string.Join(" ", new[] { 1, 2, 3, 10 }.Select(k => sol.Solve(stacked, k))));  // 40 40 40 40

        // Touching endpoints: the one clarifying question, and it changes the answer.
        var touching = new[] { new Class(0, 5, 10), new Class(5, 10, 10) };
        Console.WriteLine(sol.Solve(touching, 2));                          // 20  half-open: both
        Console.WriteLine(sol.Solve(touching, 2, inclusiveEnd: true));      // 10  inclusive: conflict

        // LeetCode 1751 -- inclusive end days.
        Console.WriteLine(sol.MaxValue(new[] { new[] { 1, 2, 4 }, new[] { 3, 4, 3 }, new[] { 2, 3, 1 } }, 2));   // 7
        Console.WriteLine(sol.MaxValue(new[] { new[] { 1, 2, 4 }, new[] { 3, 4, 3 }, new[] { 2, 3, 10 } }, 2));  // 10

        // LeetCode 1235 -- the uncapped 1D fallback.
        Console.WriteLine(JobScheduling(new[] { 1, 2, 3, 3 }, new[] { 3, 4, 5, 6 }, new[] { 50, 10, 40, 70 }));  // 120

        // Which classes, not just how much.
        for (int k = 1; k <= 3; k++)
        {
            var (credit, schedule) = sol.SolveWithSchedule(classes, k);
            var picked = string.Join(", ", schedule.Select(c => $"({c.Start},{c.End},{c.Credit})"));
            Console.WriteLine($"K={k}  credit={credit,3}  take [{picked}]");
        }
    }
}
