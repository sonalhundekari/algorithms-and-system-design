// Find Minimum Time to Finish All Jobs -- LeetCode 1723
// Difficulty: Hard
// Pattern: balanced k-way partitioning (makespan minimization, P||Cmax)
//
// Give every job to exactly one of k workers; a worker's time is the SUM of the
// jobs it holds; the answer is the largest worker time, and we want that as
// small as possible. Jobs are NOT splittable and workers are interchangeable.
//
//   jobs = [1,2,4,7,8], k = 2  ->  11     [8,1,2] and [7,4]
//
// The first instinct -- "always hand the next job to the least loaded worker"
// (LPT, longest processing time first) -- is a real algorithm with a real
// guarantee, and it is WRONG here:
//
//   jobs = [3,3,2,2,2], k = 2
//     LPT: 3|3 -> 5|3 -> 5|5 -> 7|5   answer 7
//     opt: [3,3] and [2,2,2]          answer 6
//
// LPT is within 4/3 of optimal (Graham 1969) and that is the best any greedy
// does, because the exact problem is NP-hard. What rescues us is n <= 12: the
// input is small enough to search exhaustively, so the question is really "can
// you prune a 12^12 search down to something instant".
//
// Three ways to do it, all in this file:
//
//   MinimumTime                  binary search the answer + DFS feasibility   <- reach for this
//   MinimumTimeByBacktracking    one DFS, prune against the best so far
//   MinimumTimeBySubsetDp        bitmask DP over subsets, no search at all    O(k * 3^n)
//
// The two prunings that do all the work, and the ones to say out loud:
//
//   1. SYMMETRY. Workers are interchangeable, so two workers holding the same
//      load right now lead to identical subtrees. Try the load once. This alone
//      turns k^n into something that finishes; without it nothing else matters.
//      In particular all the idle workers collapse into a single branch.
//
//   2. BIGGEST JOB FIRST. Sorting descending makes bad branches fail near the
//      root instead of at depth 11. Same answer, enormously smaller tree --
//      placing the 8 first immediately rules out the branches that a run of 1s
//      would have wandered through.
//
// Feasibility is monotone -- if every job fits under a limit of T, it fits under
// T+1 -- which is the whole license for the binary search. The range is
// [max(max job, ceil(total/k)), total]: no worker can beat the biggest single
// job, no schedule can beat a perfectly even split, and one worker doing
// everything is always legal.
//
// The DP is the answer to "what if you cannot afford exponential-in-the-search
// but can afford exponential-in-n" -- it is O(k * 3^n) NO MATTER THE VALUES,
// while the searches are fast in practice but have no such bound.

using System.Diagnostics;
using System.Numerics;

namespace CodingPatterns.DynamicProgramming;

public static class MinimumTimeToFinishAllJobs
{
    // --------------------------------------------------- binary search + DFS

    /// <summary>
    /// The minimum possible working time of the busiest worker.
    ///
    /// Binary searches the answer and asks a DFS "can every job be placed under
    /// this limit?" -- a decision problem that fails fast, where the optimization
    /// problem has to explore everything.
    /// </summary>
    public static int MinimumTime(int[] jobs, int k)
    {
        // Places desc[idx..] onto `loads` without any worker
        // exceeding `limit`, recording each choice in
        // `slot` (indexed by sorted position, not input position).
        //
        // The duplicate-load check is the symmetry pruning: workers are unlabeled,
        // so two workers at the same load are the same worker as far as the search
        // is concerned. It subsumes the usual "break on the first empty worker"
        // special case -- every idle worker has load 0, so only one of them is ever
        // tried.
        static bool Place(int[] desc, int idx, int[] loads, int limit, int[] slot)
        {
            if (idx == desc.Length)
                return true;

            int job = desc[idx];

            for (int i = 0; i < loads.Length; i++)
            {
                if (loads[i] + job > limit)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (loads[j] == loads[i]) { duplicate = true; break; }
                if (duplicate)
                    continue;

                loads[i] += job;
                slot[idx] = i;
                if (Place(desc, idx + 1, loads, limit, slot))
                    return true;
                loads[i] -= job;
            }

            return false;
        }

        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));
        if (jobs.Length == 0)
            return 0;

        var desc = (int[])jobs.Clone();
        Array.Sort(desc);
        Array.Reverse(desc);
        int total = desc.Sum();

        // Two independent lower bounds; neither is reachable on its own.
        int lo = Math.Max(desc[0], (total + k - 1) / k);
        int hi = total;                                   // one worker takes it all

        var slot = new int[desc.Length];
        int workers = Math.Min(k, desc.Length);            // workers beyond n are dead weight

        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Place(desc, 0, new int[workers], mid, slot))
                hi = mid;
            else
                lo = mid + 1;
        }

        return lo;
    }

    /// <summary>
    /// Whether <paramref name="jobs"/> can be finished within
    /// <paramref name="limit"/> using k workers -- the decision problem on its
    /// own, which is what an interviewer usually asks for next.
    /// </summary>
    public static bool CanFinishWithin(int[] jobs, int k, int limit)
    {
        // Places desc[idx..] onto `loads` without any worker
        // exceeding `limit`, recording each choice in
        // `slot` (indexed by sorted position, not input position).
        //
        // The duplicate-load check is the symmetry pruning: workers are unlabeled,
        // so two workers at the same load are the same worker as far as the search
        // is concerned. It subsumes the usual "break on the first empty worker"
        // special case -- every idle worker has load 0, so only one of them is ever
        // tried.
        static bool Place(int[] desc, int idx, int[] loads, int limit, int[] slot)
        {
            if (idx == desc.Length)
                return true;

            int job = desc[idx];

            for (int i = 0; i < loads.Length; i++)
            {
                if (loads[i] + job > limit)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (loads[j] == loads[i]) { duplicate = true; break; }
                if (duplicate)
                    continue;

                loads[i] += job;
                slot[idx] = i;
                if (Place(desc, idx + 1, loads, limit, slot))
                    return true;
                loads[i] -= job;
            }

            return false;
        }

        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));
        if (jobs.Length == 0)
            return limit >= 0;

        var desc = (int[])jobs.Clone();
        Array.Sort(desc);
        Array.Reverse(desc);
        return Place(desc, 0, new int[Math.Min(k, desc.Length)], limit, new int[desc.Length]);
    }

    /// <summary>
    /// The optimal split itself, as one list of ORIGINAL job indices per worker
    /// (some may be empty when k &gt; n). Solve for the optimum first, then run a
    /// single feasibility DFS at that limit -- it cannot fail, and the placements
    /// it makes on the way are the witness.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Assign(int[] jobs, int k)
    {
        // Places desc[idx..] onto `loads` without any worker
        // exceeding `limit`, recording each choice in
        // `slot` (indexed by sorted position, not input position).
        //
        // The duplicate-load check is the symmetry pruning: workers are unlabeled,
        // so two workers at the same load are the same worker as far as the search
        // is concerned. It subsumes the usual "break on the first empty worker"
        // special case -- every idle worker has load 0, so only one of them is ever
        // tried.
        static bool Place(int[] desc, int idx, int[] loads, int limit, int[] slot)
        {
            if (idx == desc.Length)
                return true;

            int job = desc[idx];

            for (int i = 0; i < loads.Length; i++)
            {
                if (loads[i] + job > limit)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (loads[j] == loads[i]) { duplicate = true; break; }
                if (duplicate)
                    continue;

                loads[i] += job;
                slot[idx] = i;
                if (Place(desc, idx + 1, loads, limit, slot))
                    return true;
                loads[i] -= job;
            }

            return false;
        }

        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));

        var buckets = Enumerable.Range(0, k).Select(_ => new List<int>()).ToList();
        if (jobs.Length == 0)
            return buckets;

        var order = Enumerable.Range(0, jobs.Length)
            .OrderByDescending(i => jobs[i])
            .ToArray();
        var desc = order.Select(i => jobs[i]).ToArray();

        var slot = new int[desc.Length];
        int optimum = MinimumTime(jobs, k);

        if (!Place(desc, 0, new int[Math.Min(k, desc.Length)], optimum, slot))
            throw new InvalidOperationException($"no assignment at the computed optimum {optimum}");

        for (int idx = 0; idx < desc.Length; idx++)
            buckets[slot[idx]].Add(order[idx]);

        foreach (var bucket in buckets)
            bucket.Sort();

        return buckets;
    }

    // ------------------------------------------------------- plain backtracking

    /// <summary>
    /// One DFS over "which worker gets job idx", pruned against the best
    /// complete assignment found so far. Seeded with the LPT greedy so the
    /// pruning has real teeth from the very first node instead of starting at
    /// "the sum of everything".
    ///
    /// Same asymptotics as the binary-search version (both exponential in the
    /// worst case); this one is the easier sell in an interview, the other one is
    /// usually faster because a failed decision problem gives up sooner than an
    /// optimization sweep does.
    /// </summary>
    public static int MinimumTimeByBacktracking(int[] jobs, int k)
    {
        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));
        if (jobs.Length == 0)
            return 0;

        var desc = (int[])jobs.Clone();
        Array.Sort(desc);
        Array.Reverse(desc);
        static void Search(int[] desc, int idx, int[] loads, ref int best)
        {
            if (idx == desc.Length)
            {
                best = Math.Min(best, loads.Max());
                return;
            }

            int job = desc[idx];

            for (int i = 0; i < loads.Length; i++)
            {
                // Loads only grow, so a worker already at or past `best` can never
                // lead to an improvement -- cut the branch, do not just score it.
                if (loads[i] + job >= best)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (loads[j] == loads[i]) { duplicate = true; break; }
                if (duplicate)
                    continue;

                loads[i] += job;
                Search(desc, idx + 1, loads, ref best);
                loads[i] -= job;
            }
        }

        int best = GreedyLongestFirst(jobs, k);            // a legal answer, hence a legal bound
        Search(desc, 0, new int[Math.Min(k, desc.Length)], ref best);
        return best;
    }

    // ------------------------------------------------------------- subset DP
    //
    // dp[w][mask] = best makespan when the jobs in `mask` are shared among w
    // workers. Worker w either idles, or takes some nonempty subset `sub`:
    //
    //     dp[w][mask] = min( dp[w-1][mask],
    //                        min over sub of mask: max(sum[sub], dp[w-1][mask^sub]) )
    //
    // The max() is the point -- adding a worker cannot lower the load of the
    // others, so the makespan of a split is the worse of its two halves.
    //
    // Enumerating every submask of every mask is sum over masks of 2^|mask| =
    // 3^n, and only the previous worker's row is ever read, so two rows of
    // 2^n ints is the whole table. At n=12 that is ~531k inner steps per worker:
    // microseconds, and -- unlike the searches -- a hard bound.

    /// <summary>
    /// Exact O(k * 3^n) subset DP. No pruning, no search, no dependence on the
    /// job VALUES -- the runtime is the same for every input of a given size.
    /// </summary>
    public static int MinimumTimeBySubsetDp(int[] jobs, int k)
    {
        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));

        int n = jobs.Length;
        if (n == 0)
            return 0;
        if (n > 22)
            throw new ArgumentException($"subset DP needs 3^n work; n={n} is out of reach", nameof(jobs));

        int workers = Math.Min(k, n);
        int full = (1 << n) - 1;

        // sum[mask], built by peeling off the lowest set bit.
        var sum = new int[full + 1];
        for (int mask = 1; mask <= full; mask++)
        {
            int low = mask & -mask;
            sum[mask] = sum[mask ^ low] + jobs[BitOperations.TrailingZeroCount(low)];
        }

        var dp = (int[])sum.Clone();                       // one worker: it does everything
        var next = new int[full + 1];

        for (int worker = 2; worker <= workers; worker++)
        {
            for (int mask = 0; mask <= full; mask++)
            {
                int best = dp[mask];                       // this worker takes nothing
                for (int sub = mask; sub > 0; sub = (sub - 1) & mask)
                {
                    int candidate = Math.Max(sum[sub], dp[mask ^ sub]);
                    if (candidate < best)
                        best = candidate;
                }
                next[mask] = best;
            }
            (dp, next) = (next, dp);
        }

        return dp[full];
    }

    // ---------------------------------------------------------- the greedy

    /// <summary>
    /// LPT: biggest job first, always onto the least loaded worker. Not optimal
    /// -- but never worse than (4/3 - 1/3k) times optimal, O(n log n), and it
    /// seeds the backtracking bound. This is the answer when n is large enough
    /// that exact is off the table.
    /// </summary>
    public static int GreedyLongestFirst(int[] jobs, int k)
    {
        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));
        if (k < 1)
            throw new ArgumentOutOfRangeException(nameof(k), k, "there must be at least one worker");

        long totalTime = 0;
        foreach (int job in jobs)
        {
            if (job < 0)
                throw new ArgumentException($"job time {job} is negative", nameof(jobs));
            totalTime += job;
        }

        // Every bound here is a sum of job times; the constraints (n <= 12,
        // job <= 1e7) keep that inside an int, but nothing stops a caller.
        if (totalTime > int.MaxValue)
            throw new ArgumentException("total job time overflows int", nameof(jobs));
        if (jobs.Length == 0)
            return 0;

        var desc = (int[])jobs.Clone();
        Array.Sort(desc);
        Array.Reverse(desc);

        var loads = new int[Math.Min(k, jobs.Length)];
        foreach (int job in desc)
        {
            int least = 0;
            for (int i = 1; i < loads.Length; i++)
                if (loads[i] < loads[least])
                    least = i;
            loads[least] += job;
        }

        return loads.Max();
    }

    // ---------------------------------------------------------------- tests

    public static void Run()
    {
        Console.WriteLine("== the LeetCode examples ==");

        foreach (var (jobs, k, expected) in new (int[], int, int)[]
                 {
                     (new[] { 3, 2, 3 }, 3, 3),
                     (new[] { 1, 2, 4, 7, 8 }, 2, 11),
                 })
        {
            int answer = MinimumTime(jobs, k);
            Console.WriteLine($"  jobs [{string.Join(",", jobs)}], k={k} -> {answer} (expect {expected}) {(answer == expected ? "ok" : "WRONG")}");
            Console.WriteLine($"    split: {Describe(jobs, Assign(jobs, k))}");
            Console.WriteLine($"    backtracking {MinimumTimeByBacktracking(jobs, k)}, subset DP {MinimumTimeBySubsetDp(jobs, k)}, LPT greedy {GreedyLongestFirst(jobs, k)}");
        }

        Console.WriteLine();
        Console.WriteLine("== why the greedy is not the answer ==");

        var trap = new[] { 3, 3, 2, 2, 2 };
        Console.WriteLine($"  jobs [{string.Join(",", trap)}], k=2");
        Console.WriteLine($"    LPT greedy: {GreedyLongestFirst(trap, 2)}   (3|3 -> 5|3 -> 5|5 -> 7|5)");
        Console.WriteLine($"    optimal:    {MinimumTime(trap, 2)}   {Describe(trap, Assign(trap, 2))}");
        Console.WriteLine("  Greedy commits the two 3s to different workers and can never take it back.");
        Console.WriteLine("  It is off by one here and provably never off by more than 4/3 -- which is");
        Console.WriteLine("  exactly why the exact solution has to search.");

        Console.WriteLine();
        Console.WriteLine("== the bounds the binary search runs between ==");

        var bounded = new[] { 5, 5, 4, 4, 3, 3, 2 };
        int totalWork = bounded.Sum();
        Console.WriteLine($"  jobs [{string.Join(",", bounded)}], k=3: total {totalWork}, biggest {bounded.Max()}");
        Console.WriteLine($"    lower bound max(biggest, ceil(total/k)) = max({bounded.Max()}, {(totalWork + 2) / 3}) = {Math.Max(bounded.Max(), (totalWork + 2) / 3)}");
        Console.WriteLine($"    answer {MinimumTime(bounded, 3)}  {Describe(bounded, Assign(bounded, 3))}");
        Console.WriteLine($"    feasible at 9? {CanFinishWithin(bounded, 3, 9)}   at 8? {CanFinishWithin(bounded, 3, 8)}   monotone, so binary search is legal");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        Console.WriteLine($"  k >= n, every worker takes one job: {MinimumTime(new[] { 4, 9, 2 }, 5)} (expect 9, the biggest job)");
        Console.WriteLine($"  k = 1, one worker takes everything: {MinimumTime(new[] { 4, 9, 2 }, 1)} (expect 15)");
        Console.WriteLine($"  no jobs at all:                     {MinimumTime(Array.Empty<int>(), 3)} (expect 0)");
        Console.WriteLine($"  every job identical:                {MinimumTime(new[] { 5, 5, 5, 5 }, 2)} (expect 10)");
        Console.WriteLine($"  a zero-length job:                  {MinimumTime(new[] { 0, 0, 7 }, 2)} (expect 7)");
        Console.WriteLine($"  one huge job dominates:             {MinimumTime(new[] { 100, 1, 1, 1 }, 2)} (expect 100)");
        Console.WriteLine($"  k > n leaves workers idle:           {Describe(new[] { 4, 9 }, Assign(new[] { 4, 9 }, 4))}");

        foreach (var (label, bad) in new (string, Func<int>)[]
                 {
                     ("k = 0", () => MinimumTime(new[] { 1 }, 0)),
                     ("negative job", () => MinimumTime(new[] { 1, -3 }, 2)),
                     ("null jobs", () => MinimumTime(null, 2)),
                 })
        {
            try
            {
                bad();
                Console.WriteLine($"  {label}: NOT rejected -- bug");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  {label} rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(1723);
        bool searchMatchesDp = true, backtrackMatchesDp = true, bruteForceMatches = true;
        bool assignmentValid = true, boundsHold = true, greedyWithinRatio = true;

        for (int trial = 0; trial < 2000; trial++)
        {
            int n = rng.Next(0, 9);
            int k = rng.Next(1, 5);
            var jobs = Enumerable.Range(0, n).Select(_ => rng.Next(0, 25)).ToArray();

            int answer = MinimumTime(jobs, k);
            int dp = MinimumTimeBySubsetDp(jobs, k);
            int backtracked = MinimumTimeByBacktracking(jobs, k);
            int brute = BruteForce(jobs, k);

            searchMatchesDp &= answer == dp;
            backtrackMatchesDp &= backtracked == dp;
            bruteForceMatches &= brute == dp;

            assignmentValid &= IsValidAssignment(jobs, k, Assign(jobs, k), answer);

            int total = jobs.Sum();
            int floorBound = n == 0 ? 0 : Math.Max(jobs.Max(), (total + k - 1) / k);
            boundsHold &= answer >= floorBound && answer <= total;

            // Graham's bound: LPT is never worse than (4/3 - 1/3k) * OPT.
            int greedy = GreedyLongestFirst(jobs, k);
            greedyWithinRatio &= greedy >= answer && greedy <= (4.0 / 3.0 - 1.0 / (3.0 * k)) * answer + 1e-9;
        }

        Console.WriteLine($"  2,000 random inputs -- binary search == subset DP:      {searchMatchesDp}");
        Console.WriteLine($"                         backtracking  == subset DP:      {backtrackMatchesDp}");
        Console.WriteLine($"                         brute force   == subset DP:      {bruteForceMatches}");
        Console.WriteLine($"  Assign() partitions every job exactly once, makespan optimal:  {assignmentValid}");
        Console.WriteLine($"  max(biggest job, ceil(total/k)) <= answer <= total:            {boundsHold}");
        Console.WriteLine($"  LPT greedy within Graham's (4/3 - 1/3k) bound:                 {greedyWithinRatio}");

        Console.WriteLine();
        Console.WriteLine("== the full constraint, n = 12 ==");

        // Near-equal jobs are the adversarial case: nothing fails early, so the
        // search has to work for the answer.
        var worst = Enumerable.Range(0, 12).Select(_ => 100 + rng.Next(0, 6)).ToArray();
        Console.WriteLine($"  jobs [{string.Join(",", worst)}], k=5");

        var sw = Stopwatch.StartNew();
        int searched = MinimumTime(worst, 5);
        var searchTime = sw.Elapsed;

        sw.Restart();
        int backtrack = MinimumTimeByBacktracking(worst, 5);
        var backtrackTime = sw.Elapsed;

        sw.Restart();
        int table = MinimumTimeBySubsetDp(worst, 5);
        var dpTime = sw.Elapsed;

        Console.WriteLine($"    binary search + DFS  {searched}  in {searchTime.TotalMilliseconds:0.###} ms");
        Console.WriteLine($"    backtracking         {backtrack}  in {backtrackTime.TotalMilliseconds:0.###} ms");
        Console.WriteLine($"    subset DP            {table}  in {dpTime.TotalMilliseconds:0.###} ms  (3^12 = 531,441 steps per worker)");
        Console.WriteLine($"    all three agree: {searched == backtrack && backtrack == table}");
        Console.WriteLine("  The searches win on typical inputs and have no worst-case bound; the DP has");
        Console.WriteLine("  one and does not care what the numbers are. That tradeoff is the answer to");
        Console.WriteLine("  \"which would you ship?\"");
    }

    /// <summary>Every job to every worker, k^n of them. The definition, used to check the clever versions.</summary>
    private static int BruteForce(int[] jobs, int k)
    {
        var loads = new int[k];
        return Recurse(0);

        int Recurse(int idx)
        {
            if (idx == jobs.Length)
                return loads.Max();

            int best = int.MaxValue;
            for (int i = 0; i < k; i++)
            {
                loads[i] += jobs[idx];
                best = Math.Min(best, Recurse(idx + 1));
                loads[i] -= jobs[idx];
            }
            return best;
        }
    }

    /// <summary>
    /// An assignment is valid when it uses k buckets, every job appears exactly once,
    /// and the busiest bucket matches the claimed optimum.
    /// </summary>
    private static bool IsValidAssignment(
        int[] jobs, int k, IReadOnlyList<IReadOnlyList<int>> buckets, int optimum)
    {
        if (buckets.Count != k)
            return false;

        var seen = new HashSet<int>();
        int worst = 0;

        foreach (var bucket in buckets)
        {
            int load = 0;
            foreach (int index in bucket)
            {
                if (index < 0 || index >= jobs.Length || !seen.Add(index))
                    return false;
                load += jobs[index];
            }
            worst = Math.Max(worst, load);
        }

        return seen.Count == jobs.Length && worst == optimum;
    }

    private static string Describe(int[] jobs, IReadOnlyList<IReadOnlyList<int>> buckets)
        => string.Join("  ", buckets.Select(b => $"[{string.Join(",", b.Select(i => jobs[i]))}]={b.Sum(i => jobs[i])}"));
}

// ---- Notes for the follow-up questions ----
//
// "Why can't you just sort and deal them out round robin?"
//     Because the objective is a max, not a sum, and greedy choices are not
//     locally correctable: [3,3,2,2,2] with k=2 separates the two 3s and is stuck
//     at 7 when 6 exists. Any exchange argument you try will fail, which is the
//     tell that the problem is NP-hard (it contains PARTITION: k=2 and "is the
//     answer total/2?" is exactly the partition question).
//
// "n is 1,000 now."
//     Exact is gone. LPT is the answer -- O(n log n), within 4/3, and better than
//     4/3 in practice. If you can afford more time, run LPT then hill-climb by
//     moving or swapping jobs between the busiest and the least loaded worker
//     while that lowers the makespan. There is also a PTAS (Hochbaum-Shmoys):
//     bin-pack the large jobs exactly, sprinkle the small ones.
//
// "Minimize the SUM of the worker times instead."
//     Trivial -- it is the total, the same for every assignment. Ask which
//     objective they mean before optimizing anything: makespan (this problem),
//     sum of completion times (SJF-style, easy), or the number of workers needed
//     to stay under a limit (LC 2305 / bin packing, the same DFS with the loop
//     inverted).
//
// "Fewest workers that finish within T?"
//     Same search, different question: binary search on the WORKER COUNT instead
//     of the time, calling CanFinishWithin(jobs, k, T). Monotone in k for the
//     same reason it is monotone in T.
//
// "Workers have different speeds."
//     Load becomes sum(job) / speed(worker) -- the DFS is unchanged (the limit
//     check just divides), but the symmetry pruning DIES, because workers are no
//     longer interchangeable. Expect a much bigger tree; that is R||Cmax, and
//     the DP over subsets survives since it never relied on symmetry.
//
// "Jobs have precedence constraints / release times."
//     Different problem entirely -- partitioning is no longer enough because a
//     worker's finish time stops being the sum of its jobs. That is list
//     scheduling over a DAG, and the makespan needs a topological pass, not a
//     subset sum.
