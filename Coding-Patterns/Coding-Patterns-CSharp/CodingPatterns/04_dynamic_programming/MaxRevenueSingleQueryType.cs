// Single Query Type, Max Revenue in K Minutes (Snowflake-flavoured)
// Difficulty: Easy once the constraint is read; Hard if you start writing knapsack
// Pattern: closed form per candidate + one linear scan -- the DP that is NOT needed
//
// A virtual warehouse can execute one of n query types. Query type i takes
// duration_i minutes per run and earns revenue_i per run. Pick ONE query type,
// run it back-to-back for k minutes, and report the type and the total revenue.
//
//     k = 10, types = [(6, 10), (5, 6), (1, 1)]      (duration, revenue)
//         type 0:  floor(10/6) = 1 run   ->  10
//         type 1:  floor(10/5) = 2 runs  ->  12   <- answer
//         type 2:  floor(10/1) = 10 runs ->  10
//
// THE WHOLE PROBLEM IS THE WORD "ONE". Runs cost time and pay revenue, the budget
// is k, so every instinct says unbounded knapsack (LC 322's cousin) and people
// start allocating a dp[k + 1] array. Read the constraint again: only a single
// type may be selected. That removes the only real decision a knapsack makes --
// *which* item to spend the next minute on. With the mix forbidden, the count is
// not a choice either: revenue is non-negative, so you run the chosen type as
// many times as physically fit. Both axes collapse and each candidate has a
// closed form:
//
//     revenue(i) = floor(k / duration_i) * revenue_i
//
// n candidates, O(1) each. One scan, O(n) time, O(1) space, and it is exactly
// optimal -- there is nothing left for a DP to search. Say that sentence out loud
// before writing code; the round is graded on noticing, not on typing.
//
// It also matters at the scale this problem is dressed in. Knapsack over minutes
// is O(n * k) time and O(k) memory -- pseudo-polynomial, driven by the VALUE of
// k, not its size. A day is 1440 minutes and that is fine; a quarter in seconds
// is not. The scan does not care what k is.
//
// EVERY GREEDY SHORTCUT IS ALSO WRONG -- floor() is not monotone in any single
// column, which the example above is built to show:
//
//     highest revenue per run  -> type 0 (10 per run)    gives 10, answer is 12
//     highest revenue density  -> type 0 (10/6 = 1.67)   gives 10, answer is 12
//     shortest duration        -> type 2 (1 minute)      gives 10, answer is 12
//
// Density is the seductive one, and it is only right when the leftover time can
// be used -- i.e. after the floor disappears (see BestFractional below). The
// wasted tail k mod duration_i is what breaks it, and the tail depends on the
// candidate. So: evaluate all n, do not rank by a proxy.
//
// CLARIFY BEFORE CODING (these are the questions the round is actually probing):
//
//   duration_i > k        Zero runs, zero revenue. Not an error -- just a losing
//                         candidate. If EVERY type is too long the answer is 0,
//                         and "which type" is arbitrary; agree on a convention.
//   duration_i == 0       Degenerate: infinitely many runs, unbounded revenue.
//                         There is no correct answer, so reject the input rather
//                         than silently dividing. Ask; do not assume.
//   revenue_i < 0         Only possible if "revenue" is really net of cost. Then
//                         running the max number of times is no longer optimal --
//                         the best count is 0. Handled by idleAllowed below.
//   overflow              floor(k / 1) * revenue is ~k * max-revenue. k in minutes
//                         over a year with a per-run revenue in cents overflows a
//                         32-bit int easily. long everywhere.
//   ties                  Stated as arbitrary; return the lowest index anyway so
//                         the function is deterministic and testable.
//
// FOLLOW-UPS, in the order interviewers ask them:
//
//   "Now allow any mix of types."     -> BestMix: the unbounded knapsack you were
//                                        about to write, O(n * k) / O(k), with the
//                                        counts recovered. On the example above it
//                                        returns 14 (one type-0 run + four type-2),
//                                        strictly better than the single-type 12 --
//                                        the single-type answer is a lower bound.
//   "Runs can be interrupted."        -> BestFractional: the floor vanishes, so
//                                        the density greedy becomes correct and the
//                                        answer is k * max(revenue_i / duration_i)
//                                        with no scan of counts at all.
//   "Each type has a run limit."      -> bounded knapsack: binary-split each type
//                                        into 1, 2, 4, ... copies and run 0/1
//                                        knapsack, O(n * k * log limit).
//   "Many k values, same types."      -> the scan is already O(n) per query; if k
//                                        is huge and n small, note that the answer
//                                        as a function of k is piecewise constant
//                                        and only changes at multiples of the
//                                        durations.
//
// Time:  O(n)      one pass, one division each
// Space: O(1)

namespace CodingPatterns.DynamicProgramming;

public class MaxRevenueSingleQueryType
{
    /// <summary>A query type: minutes per run, revenue per run.</summary>
    public readonly record struct QueryType(long DurationMinutes, long RevenuePerRun);

    /// <summary>Chosen type index (-1 = stay idle), how many times it runs, total revenue.</summary>
    public readonly record struct Choice(int TypeIndex, long Runs, long Revenue);

    /// <summary>
    /// THE ANSWER. Pick the single query type whose back-to-back runs earn the most
    /// within k minutes. One division per type, no DP, no sort.
    ///
    /// idleAllowed only matters when revenues can be negative: it permits the
    /// warehouse to run nothing at all (index -1, revenue 0) instead of being
    /// forced to lose money. With non-negative revenues it changes nothing.
    /// </summary>
    public static Choice Best(IReadOnlyList<QueryType> types, long k, bool idleAllowed = false)
    {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "Time budget cannot be negative.");

        // Idle is the floor when losses are possible; otherwise there is no
        // baseline and even an all-too-slow candidate (0 runs, 0 revenue) wins it.
        var best = idleAllowed ? new Choice(-1, 0, 0) : new Choice(-1, 0, long.MinValue);

        for (int i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (t.DurationMinutes <= 0)
                throw new ArgumentException(
                    $"Query type {i} has duration {t.DurationMinutes}: a run must take time, " +
                    "otherwise revenue is unbounded.", nameof(types));

            long runs = k / t.DurationMinutes;              // duration > k -> 0 runs, 0 revenue
            long revenue = runs * t.RevenuePerRun;

            // A losing type is worth running zero times when idling is on the table.
            if (idleAllowed && revenue < 0) (runs, revenue) = (0, 0);

            // Strictly greater keeps the lowest index on a tie -- deterministic tests.
            if (revenue > best.Revenue) best = new Choice(i, runs, revenue);
        }

        // Only reachable with an empty catalogue: nothing to pick, nothing earned.
        return best.TypeIndex < 0 ? new Choice(-1, 0, 0) : best;
    }

    /// <summary>
    /// FOLLOW-UP: drop the single-type rule and any mix is legal. NOW it is
    /// unbounded knapsack -- dp[t] = best revenue in exactly-at-most t minutes --
    /// and the inner loop runs forward so a type can be reused.
    ///
    /// O(n * k) time, O(k) space: pseudo-polynomial, so this is only viable while
    /// k stays small. That cost difference is the point of the comparison.
    /// </summary>
    public static (long Revenue, long[] Counts) BestMix(IReadOnlyList<QueryType> types, int k)
    {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k));

        var dp = new long[k + 1];
        var lastUsed = new int[k + 1];                     // for reconstructing the counts
        Array.Fill(lastUsed, -1);

        for (int t = 1; t <= k; t++)
        {
            dp[t] = dp[t - 1];                             // leave the minute idle
            lastUsed[t] = -1;                              // ...so no run ended here

            for (int i = 0; i < types.Count; i++)
            {
                var q = types[i];
                if (q.DurationMinutes <= 0 || q.DurationMinutes > t) continue;

                long candidate = dp[t - q.DurationMinutes] + q.RevenuePerRun;
                if (candidate > dp[t]) (dp[t], lastUsed[t]) = (candidate, i);
            }
        }

        // Walk back: at minute t either a run of lastUsed[t] finished, or nothing did.
        var counts = new long[types.Count];
        for (int t = k; t > 0;)
        {
            int pick = lastUsed[t];
            if (pick < 0) { t--; continue; }
            counts[pick]++;
            t -= (int)types[pick].DurationMinutes;
        }
        return (dp[k], counts);
    }

    /// <summary>
    /// FOLLOW-UP: runs may be cut off mid-flight and still pay pro rata. The floor
    /// disappears, so no time is ever wasted and the density greedy that loses
    /// above becomes exactly right: pour the whole budget into the best ratio.
    /// O(n), and note it is an upper bound on both answers above.
    /// </summary>
    public static (int TypeIndex, double Revenue) BestFractional(IReadOnlyList<QueryType> types, long k)
    {
        int bestIndex = -1;
        double bestDensity = 0;                            // idling beats a negative rate

        for (int i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (t.DurationMinutes <= 0) throw new ArgumentException("Duration must be positive.", nameof(types));

            double density = (double)t.RevenuePerRun / t.DurationMinutes;
            if (density > bestDensity) (bestIndex, bestDensity) = (i, density);
        }
        return (bestIndex, bestDensity * k);
    }

    public static void Run()
    {
        // The worked example: every one-column greedy picks a different loser.
        var types = new[]
        {
            new QueryType(6, 10),      // best per run, best density  -> 10
            new QueryType(5, 6),       // best actual total           -> 12
            new QueryType(1, 1),       // shortest duration           -> 10
        };

        var best = Best(types, 10);
        Console.WriteLine($"single type: #{best.TypeIndex} x{best.Runs} = {best.Revenue}");   // #1 x2 = 12

        var (mixRevenue, counts) = BestMix(types, 10);
        Console.WriteLine($"any mix:     {mixRevenue} via [{string.Join(", ", counts)}]");    // 14 via [1, 0, 4]

        var (fracIndex, fracRevenue) = BestFractional(types, 10);
        Console.WriteLine($"fractional:  #{fracIndex} -> {fracRevenue:0.##}");                // #0 -> 16.67

        // Every duration exceeds the budget: zero runs, zero revenue, index arbitrary.
        Console.WriteLine(Best(new[] { new QueryType(50, 900), new QueryType(99, 5) }, 10));  // 0 runs, 0 revenue

        // k = 0 is the same story with no candidates able to run at all.
        Console.WriteLine(Best(types, 0).Revenue);                                           // 0

        // Ties resolve to the lowest index.
        Console.WriteLine(Best(new[] { new QueryType(2, 3), new QueryType(4, 6) }, 8).TypeIndex);  // 0 (both 12)

        // Losses: forced to pick, you take the least-bad; allowed to idle, you idle.
        var lossy = new[] { new QueryType(3, -1), new QueryType(2, -5) };
        Console.WriteLine(Best(lossy, 10));                              // #0 x3 = -3
        Console.WriteLine(Best(lossy, 10, idleAllowed: true));           // #-1 x0 = 0

        // Overflow check: minutes in a (leap) year, revenue per run in cents.
        // 527040 * 1000 = 527,040,000 fits, but 527040 * 100000 does not fit an int.
        Console.WriteLine(Best(new[] { new QueryType(1, 100_000) }, 527_040).Revenue);        // 52,704,000,000

        // Duration 0 is rejected rather than answered.
        try { Best(new[] { new QueryType(0, 5) }, 10); }
        catch (ArgumentException e) { Console.WriteLine($"rejected: {e.Message.Split('.')[0]}"); }

        // The single-type answer is always a lower bound on the mixed answer.
        var random = new Random(7);
        for (int trial = 0; trial < 500; trial++)
        {
            var sample = Enumerable.Range(0, random.Next(1, 6))
                .Select(_ => new QueryType(random.Next(1, 12), random.Next(0, 40)))
                .ToArray();
            int budget = random.Next(0, 60);
            if (Best(sample, budget).Revenue > BestMix(sample, budget).Revenue)
                throw new Exception("single-type beat the mix -- impossible");
        }
        Console.WriteLine("500 random trials: single-type <= mixed, as expected.");
    }
}
