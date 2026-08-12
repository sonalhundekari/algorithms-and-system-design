// Cheapest Flights Within K Stops (LeetCode #787)
// Difficulty: Medium (the constraint is what makes it Medium; without it, it is Dijkstra)
// Pattern: shortest path where HOP COUNT is part of the state -- Bellman-Ford by rounds
//
// n cities, a list of one-way flights [from, to, price], and:
//
//     src   the city you start in
//     dst   the city you want to reach
//     k     the most stops you are allowed
//
// Cheapest total price from src to dst using at most k stops, or -1.
//
//     n = 4, flights = [[0,1,100],[1,2,100],[2,0,100],[1,3,600],[2,3,200]]
//     src = 0, dst = 3, k = 1  ->  700   (0 ->1 ->3, one stop)
//     src = 0, dst = 3, k = 2  ->  400   (0 ->1 ->2 ->3, two stops -- cheaper, one more stop)
//
// The two examples are the whole problem in miniature: the cheapest route and
// the shortest route are DIFFERENT routes, and which one is legal depends on k.
// Any algorithm that collapses "cheapest" and "fewest hops" into one number is
// going to answer one of these two wrong.
//
// FIRST, RESTATE k. "At most k stops" means at most k INTERMEDIATE cities, which
// means at most k + 1 FLIGHTS. Say this out loud and write `k + 1` exactly once,
// at the top; every off-by-one in this problem is someone re-deriving it inline.
// It is also worth confirming with the interviewer -- "stops" is genuinely
// ambiguous English, and some phrasings of this problem mean k flights.
//
// WHY PLAIN DIJKSTRA IS WRONG, PRECISELY. Dijkstra rests on one claim: the first
// time a node is popped, its distance is final, so it never needs looking at
// again. That claim dies here, because the answer is not a number per node --
// it is a number per (node, flights-used) pair. Reaching city v for 100 in three
// flights does not dominate reaching it for 500 in one: if the budget runs out
// at v, the expensive route is the only one that can still finish.
//
//     0 --1--> 1 --1--> 2 --1--> 3          k = 1, so at most 2 flights
//     0 -----------5--> 2
//
//     Cheapest way to city 2 is 0->1->2 for 2, using 2 flights -- and then there
//     is no budget left for 2->3. The answer is 0->2->3 for 6, arriving at city
//     2 for more than double the price. A visited-set Dijkstra marks city 2 done
//     at cost 2 and returns -1. It is in the tests below.
//
// SO THE STATE IS (city, flights used). Once that is said, every correct
// solution in this file is the same recurrence, walked in a different order:
//
//     best[r][v] = cheapest cost to reach v using AT MOST r flights
//     best[0][src] = 0, everything else infinite
//     best[r][v] = min( best[r-1][v],  min over edges u->v of best[r-1][u] + w )
//
// and the answer is best[k+1][dst]. Layer r depends only on layer r - 1, so two
// rows of n ints are enough -- which is exactly Bellman-Ford stopped after k + 1
// rounds instead of run to convergence.
//
//   THE TRAP, and it is the only thing an interviewer is really watching for:
//   relaxing IN PLACE instead of against a snapshot of the previous round. With
//   one shared array, a round that happens to scan edge 0->1 before edge 1->2
//   uses the brand-new dist[1] while relaxing 1->2, so a two-flight path lands
//   in the table after one round. The result is not garbage -- it is the cost of
//   a REAL route, just one that uses more flights than the budget allows -- so
//   it never looks wrong, it is just too small. On the example above with k = 1
//   the in-place version returns 400 (the three-flight route) instead of 700,
//   and whether it does depends on the order the flights arrive in. Both are in
//   the tests below.
//
// WHY THE ROUNDS TERMINATE AT ALL, and a fact worth knowing: prices are >= 1, so
// there are no zero- or negative-weight cycles, so an optimal route never
// revisits a city (cutting a cycle out makes it both cheaper AND shorter, so it
// stays legal). That means:
//
//   * k >= n - 1 makes the constraint vacuous and the problem collapses to plain
//     Dijkstra -- worth noticing, since it caps the useful work at n - 1 rounds.
//   * "at most k + 1 flights" and "exactly the best simple path" agree, so the
//     brute force in the tests can enumerate walks and still be a valid oracle.
//   * If prices could be negative, the round-bounded Bellman-Ford here still
//     works -- a bounded walk length is immune to negative cycles -- while every
//     Dijkstra variant in this file breaks. That is the honest reason to reach
//     for this shape even when it is not the fastest.
//
// Time:  O(k * E) -- k + 1 rounds over the flight list, ~500k operations at the
//                    stated limits (n <= 100, E <= 4950, k < 100)
// Space: O(n)     -- two rows; the full table is only needed to rebuild a route

namespace CodingPatterns.Graphs;

public static class CheapestFlightsWithinKStops
{
    /// <summary>No route within the stop budget.</summary>
    public const int Unreachable = -1;

    private const int Infinity = int.MaxValue;

    // ------------------------------------------- the answer to write: Bellman-Ford

    /// <summary>
    /// Cheapest price from <paramref name="src"/> to <paramref name="dst"/> using
    /// at most <paramref name="k"/> stops, or <see cref="Unreachable"/>.
    ///
    /// k + 1 rounds of edge relaxation, each round reading a SNAPSHOT of the last.
    /// The invariant is the whole proof: after round r, `best[v]` is the cheapest
    /// cost to v over all routes of at most r flights. Reading `previous` is what
    /// enforces "at most r" -- an edge relaxed this round can only extend a route
    /// that was already complete at r - 1, so no round can ever add two flights.
    /// </summary>
    public static int FindCheapestPrice(int n, int[][] flights, int src, int dst, int k)
    {
        Validate(n, flights, src, dst, k);

        if (src == dst)
            return 0;                                   // no flight needed, whatever k is

        var best = new int[n];
        Array.Fill(best, Infinity);
        best[src] = 0;

        for (int round = 0; round <= k; round++)        // k + 1 rounds == k + 1 flights
        {
            var previous = (int[])best.Clone();         // the snapshot. NOT optional.

            foreach (var flight in flights)
            {
                int from = flight[0], to = flight[1], price = flight[2];

                if (previous[from] == Infinity)
                    continue;                           // guard, not a micro-optimisation:
                                                        // Infinity + price overflows to negative
                if (previous[from] + price < best[to])
                    best[to] = previous[from] + price;
            }

            // Nothing improved, so nothing can improve again -- later rounds read
            // the same snapshot. Cheap, and it is what makes a generous k free.
            if (best.SequenceEqual(previous))
                break;
        }

        return best[dst] == Infinity ? Unreachable : best[dst];
    }

    /// <summary>
    /// The cheapest price to EVERY city under the same budget, in one pass.
    ///
    /// The single-destination version above already computes this and throws all
    /// of it away; if the interviewer follows up with "now answer many queries
    /// from the same origin", this row IS the answer table and each query becomes
    /// an array lookup. Entries stay <see cref="Unreachable"/> when there is no
    /// route within the budget.
    /// </summary>
    public static int[] CheapestPricesFromSource(int n, int[][] flights, int src, int k)
    {
        Validate(n, flights, src, src, k);

        var best = new int[n];
        Array.Fill(best, Infinity);
        best[src] = 0;

        for (int round = 0; round <= k; round++)
        {
            var previous = (int[])best.Clone();

            foreach (var flight in flights)
            {
                if (previous[flight[0]] == Infinity)
                    continue;
                if (previous[flight[0]] + flight[2] < best[flight[1]])
                    best[flight[1]] = previous[flight[0]] + flight[2];
            }

            if (best.SequenceEqual(previous))
                break;
        }

        for (int v = 0; v < n; v++)
            if (best[v] == Infinity)
                best[v] = Unreachable;

        return best;
    }

    // ------------------------------------------------ the same thing, as BFS levels

    /// <summary>
    /// Identical recurrence, walked as a level-by-level BFS over an adjacency list.
    ///
    /// Worth knowing because it is what the same idea looks like when the graph is
    /// big and the budget is small: only cities whose cost IMPROVED last round can
    /// improve anything this round, so the frontier collapses instead of rescanning
    /// all E flights k times. Same O(k * E) worst case, far less work in practice,
    /// and it dies out on its own when the frontier empties.
    ///
    /// Note this is a cost-relaxing BFS, not a shortest-hops BFS: a city can enter
    /// the frontier in several rounds, once per improvement. Popping a city does
    /// not finish it -- that is the same fact that broke Dijkstra, wearing a hat.
    /// </summary>
    public static int FindCheapestPriceLayered(int n, int[][] flights, int src, int dst, int k)
    {
        Validate(n, flights, src, dst, k);

        if (src == dst)
            return 0;

        var adjacency = new List<(int To, int Price)>[n];
        for (int v = 0; v < n; v++)
            adjacency[v] = new List<(int, int)>();
        foreach (var flight in flights)
            adjacency[flight[0]].Add((flight[1], flight[2]));

        var best = new int[n];
        Array.Fill(best, Infinity);
        best[src] = 0;

        var frontier = new List<int> { src };

        for (int round = 0; round <= k && frontier.Count > 0; round++)
        {
            var previous = (int[])best.Clone();
            var improved = new HashSet<int>();           // a city improved twice this
                                                        // round must only be expanded once
            foreach (int from in frontier)
                foreach (var (to, price) in adjacency[from])
                    if (previous[from] + price < best[to])
                    {
                        best[to] = previous[from] + price;
                        improved.Add(to);
                    }

            frontier = improved.ToList();
        }

        return best[dst] == Infinity ? Unreachable : best[dst];
    }

    // ------------------------------------------------------ Dijkstra, done correctly

    /// <summary>
    /// Best-first search over (city, flights used), popping by cost.
    ///
    /// The fix for the broken version is dominance, not a bigger visited set. Pop
    /// order is by cost, so when (v, e) comes out, any state for v already popped
    /// was at most as expensive. It dominates the new one only if it ALSO used at
    /// most as many flights -- so the thing to remember per city is the fewest
    /// flights any popped state used, and a state is discardable exactly when it
    /// arrives no earlier in hops than that. Nothing else about v matters.
    ///
    /// The first pop of dst is the answer: costs come out non-decreasing and every
    /// queued state already respects the budget.
    ///
    /// When this beats the Bellman-Ford above: a large sparse graph with a
    /// generous k, where it stops the moment dst surfaces instead of grinding
    /// through k full passes. When it loses: everywhere else -- it is
    /// O(E * k log(E * k)), it is three times the code, and it cannot survive a
    /// negative price. Write the rounds; mention this.
    /// </summary>
    public static int FindCheapestPriceDijkstra(int n, int[][] flights, int src, int dst, int k)
    {
        Validate(n, flights, src, dst, k);

        var adjacency = new List<(int To, int Price)>[n];
        for (int v = 0; v < n; v++)
            adjacency[v] = new List<(int, int)>();
        foreach (var flight in flights)
            adjacency[flight[0]].Add((flight[1], flight[2]));

        var fewestFlights = new int[n];                 // over states already POPPED
        Array.Fill(fewestFlights, int.MaxValue);

        var queue = new PriorityQueue<(int City, int Flights), int>();
        queue.Enqueue((src, 0), 0);

        while (queue.TryDequeue(out var state, out int cost))
        {
            var (city, flights_used) = state;

            if (city == dst)
                return cost;                            // cheapest, and legal by construction

            if (flights_used >= fewestFlights[city])
                continue;                               // dominated: costs more AND hops more
            fewestFlights[city] = flights_used;

            if (flights_used == k + 1)
                continue;                               // out of budget; the city still counts

            foreach (var (to, price) in adjacency[city])
                queue.Enqueue((to, flights_used + 1), cost + price);
        }

        return Unreachable;
    }

    // --------------------------------------------------------------- top-down DP

    /// <summary>
    /// The recurrence read backwards: cheapest from a city to dst within a flight
    /// budget, memoised on (city, budget).
    ///
    /// Same O(n * k) states and O(E * k) work as the rounds; it is here because it
    /// is the form that falls out if you start from "just DFS it" and add a cache,
    /// and because the memo key makes the two-dimensional state impossible to miss.
    /// The recursion depth is bounded by the budget (<= n), so no stack worries.
    /// </summary>
    public static int FindCheapestPriceMemo(int n, int[][] flights, int src, int dst, int k)
    {
        Validate(n, flights, src, dst, k);

        var adjacency = new List<(int To, int Price)>[n];
        for (int v = 0; v < n; v++)
            adjacency[v] = new List<(int, int)>();
        foreach (var flight in flights)
            adjacency[flight[0]].Add((flight[1], flight[2]));

        // memo[city, budget] = cheapest city -> dst using <= budget flights.
        var memo = new int?[n, k + 2];

        int Cheapest(int city, int budget)
        {
            if (city == dst)
                return 0;
            if (budget == 0)
                return Infinity;
            if (memo[city, budget] is int cached)
                return cached;

            int best = Infinity;
            foreach (var (to, price) in adjacency[city])
            {
                int rest = Cheapest(to, budget - 1);
                if (rest != Infinity && rest + price < best)
                    best = rest + price;
            }

            memo[city, budget] = best;
            return best;
        }

        int answer = Cheapest(src, k + 1);
        return answer == Infinity ? Unreachable : answer;
    }

    // ------------------------------------------------------------- the itinerary

    /// <summary>
    /// The cheapest route itself, src..dst inclusive, or null when there is none.
    ///
    /// Reconstruction is the one place the two-row optimisation has to be given
    /// up: the layer that produced a city's final cost is exactly the information
    /// two rows throw away. Keeping the whole (k + 2) x n table costs 10,000 ints
    /// at the stated limits, which is nothing, and each cell records either the
    /// city it was reached from at this layer, or <c>Carried</c> for "this layer
    /// did not improve on the last".
    ///
    /// The walk back alternates: a carried cell steps down a layer, an edge cell
    /// steps down a layer AND back a city. It always lands on src, because src
    /// starts at 0 and every price is >= 1, so nothing ever improves it.
    /// </summary>
    public static int[] CheapestItinerary(int n, int[][] flights, int src, int dst, int k)
    {
        const int Carried = -1;

        Validate(n, flights, src, dst, k);

        var best = new int[k + 2][];
        var from = new int[k + 2][];

        best[0] = new int[n];
        Array.Fill(best[0], Infinity);
        best[0][src] = 0;
        from[0] = new int[n];
        Array.Fill(from[0], Carried);

        for (int round = 1; round <= k + 1; round++)
        {
            best[round] = (int[])best[round - 1].Clone();
            from[round] = new int[n];
            Array.Fill(from[round], Carried);

            foreach (var flight in flights)
            {
                int a = flight[0], b = flight[1], price = flight[2];

                if (best[round - 1][a] == Infinity)
                    continue;
                if (best[round - 1][a] + price < best[round][b])
                {
                    best[round][b] = best[round - 1][a] + price;
                    from[round][b] = a;
                }
            }
        }

        if (best[k + 1][dst] == Infinity)
            return null;

        var route = new List<int> { dst };
        for (int round = k + 1, city = dst; round > 0; round--)
        {
            int predecessor = from[round][city];
            if (predecessor == Carried)
                continue;                               // this layer changed nothing here

            route.Add(predecessor);
            city = predecessor;
        }

        route.Reverse();
        return route.ToArray();
    }

    // ------------------------------------------------------------------ validation

    private static void Validate(int n, int[][] flights, int src, int dst, int k)
    {
        ArgumentNullException.ThrowIfNull(flights);

        if (n < 1)
            throw new ArgumentOutOfRangeException(nameof(n), "need at least one city");
        if (k < 0)
            throw new ArgumentOutOfRangeException(nameof(k), "a negative stop budget is meaningless");
        if ((uint)src >= (uint)n)
            throw new ArgumentOutOfRangeException(nameof(src));
        if ((uint)dst >= (uint)n)
            throw new ArgumentOutOfRangeException(nameof(dst));

        foreach (var flight in flights)
        {
            if (flight is not { Length: 3 })
                throw new ArgumentException("every flight is [from, to, price]", nameof(flights));
            if ((uint)flight[0] >= (uint)n || (uint)flight[1] >= (uint)n)
                throw new ArgumentException($"flight [{flight[0]}, {flight[1]}] leaves the map", nameof(flights));
            if (flight[2] < 0)
                throw new ArgumentException("negative prices break every Dijkstra in this file", nameof(flights));
        }
    }

    // ----------------------------------------------------- the two wrong versions
    //
    // Both are kept runnable. They are not strawmen -- each one is what the
    // natural first draft looks like, each returns a plausible number, and the
    // tests below pin down exactly which input separates it from the truth.

    /// <summary>
    /// DELIBERATELY WRONG: Bellman-Ford relaxing in place, with no snapshot.
    ///
    /// Every round reads costs the same round has already written, so a single
    /// round can chain arbitrarily many flights -- how many depends entirely on
    /// the order the flight list happens to be in. It never returns the cost of a
    /// route that does not exist; it returns the cost of a route that uses too
    /// many flights, which is why it under-reports and why the example in the
    /// problem statement (400 where 700 is correct) catches it.
    /// </summary>
    public static int FindCheapestPriceInPlace(int n, int[][] flights, int src, int dst, int k)
    {
        var best = new int[n];
        Array.Fill(best, Infinity);
        best[src] = 0;

        for (int round = 0; round <= k; round++)
            foreach (var flight in flights)
            {
                if (best[flight[0]] == Infinity)
                    continue;
                best[flight[1]] = Math.Min(best[flight[1]], best[flight[0]] + flight[2]);
            }                                           // <- reads what it just wrote

        return best[dst] == Infinity ? Unreachable : best[dst];
    }

    /// <summary>
    /// DELIBERATELY WRONG: textbook Dijkstra with a visited set on the CITY.
    ///
    /// "First pop is final" is a theorem about a graph whose only state is the
    /// node. Here the state is (city, flights used), so settling a city at its
    /// cheapest cost silently discards the pricier-but-shorter arrival that was
    /// the only one with budget left to finish. It fails by returning -1 on a
    /// graph where a route plainly exists, which is the most confusing possible
    /// symptom to debug from.
    /// </summary>
    public static int FindCheapestPriceDijkstraNaive(int n, int[][] flights, int src, int dst, int k)
    {
        var adjacency = new List<(int To, int Price)>[n];
        for (int v = 0; v < n; v++)
            adjacency[v] = new List<(int, int)>();
        foreach (var flight in flights)
            adjacency[flight[0]].Add((flight[1], flight[2]));

        var settled = new bool[n];
        var queue = new PriorityQueue<(int City, int Flights), int>();
        queue.Enqueue((src, 0), 0);

        while (queue.TryDequeue(out var state, out int cost))
        {
            if (state.City == dst)
                return cost;
            if (settled[state.City])                    // <- the city is NOT the state
                continue;
            settled[state.City] = true;

            if (state.Flights == k + 1)
                continue;

            foreach (var (to, price) in adjacency[state.City])
                queue.Enqueue((to, state.Flights + 1), cost + price);
        }

        return Unreachable;
    }

    // ---------------------------------------------------------------------- oracle

    /// <summary>
    /// Every walk of at most k + 1 flights, enumerated. Exponential and only
    /// usable on toy graphs -- which is the point: it assumes nothing the four
    /// real implementations assume, so it is a genuine oracle rather than a
    /// fifth copy of the same idea. Walks, not paths, on purpose: the fact that
    /// an optimal route is simple is a CONCLUSION, not something to bake into
    /// the thing checking the conclusion.
    /// </summary>
    private static int BruteForce(int n, int[][] flights, int src, int dst, int k)
    {
        var adjacency = new List<(int To, int Price)>[n];
        for (int v = 0; v < n; v++)
            adjacency[v] = new List<(int, int)>();
        foreach (var flight in flights)
            adjacency[flight[0]].Add((flight[1], flight[2]));

        int best = Infinity;

        void Walk(int city, int used, int cost)
        {
            if (city == dst)
                best = Math.Min(best, cost);
            if (used == k + 1)
                return;

            foreach (var (to, price) in adjacency[city])
                Walk(to, used + 1, cost + price);
        }

        Walk(src, 0, 0);
        return best == Infinity ? Unreachable : best;
    }

    // ----------------------------------------------------------------------- tests

    private static int _checks;
    private static int _failures;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (condition)
            return;

        _failures++;
        Console.WriteLine($"  FAIL: {what}");
    }

    private delegate int Solver(int n, int[][] flights, int src, int dst, int k);

    private static readonly (string Name, Solver Solve)[] Solvers =
    {
        ("Bellman-Ford", FindCheapestPrice),
        ("layered BFS", FindCheapestPriceLayered),
        ("Dijkstra", FindCheapestPriceDijkstra),
        ("top-down DP", FindCheapestPriceMemo),
    };

    public static void Run()
    {
        _checks = _failures = 0;

        Console.WriteLine("== the problem's own examples ==");

        int[][] example =
        {
            new[] { 0, 1, 100 },
            new[] { 1, 2, 100 },
            new[] { 2, 0, 100 },
            new[] { 1, 3, 600 },
            new[] { 2, 3, 200 },
        };

        foreach (var (name, solve) in Solvers)
        {
            Check(solve(4, example, 0, 3, 1) == 700, $"{name}: k = 1 -> 700");
            Check(solve(4, example, 0, 3, 2) == 400, $"{name}: k = 2 -> 400");
        }

        Console.WriteLine($"  k = 1 -> {FindCheapestPrice(4, example, 0, 3, 1)} (expect 700, route 0->1->3)");
        Console.WriteLine($"  k = 2 -> {FindCheapestPrice(4, example, 0, 3, 2)} (expect 400, route 0->1->2->3)");
        Console.WriteLine($"  itinerary at k = 1: [{string.Join(" -> ", CheapestItinerary(4, example, 0, 3, 1))}]");
        Console.WriteLine($"  itinerary at k = 2: [{string.Join(" -> ", CheapestItinerary(4, example, 0, 3, 2))}]");

        Check(CheapestItinerary(4, example, 0, 3, 1).SequenceEqual(new[] { 0, 1, 3 }), "the k = 1 route is 0 -> 1 -> 3");
        Check(CheapestItinerary(4, example, 0, 3, 2).SequenceEqual(new[] { 0, 1, 2, 3 }), "the k = 2 route is 0 -> 1 -> 2 -> 3");
        Check(CheapestItinerary(4, example, 0, 3, 0) is null, "k = 0 has no route, so no itinerary");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        foreach (var (name, solve) in Solvers)
        {
            Check(solve(4, example, 0, 3, 0) == Unreachable, $"{name}: k = 0, no direct flight -> -1");
            Check(solve(4, example, 0, 0, 0) == 0, $"{name}: src == dst -> 0 even with no budget");
            Check(solve(4, example, 3, 0, 5) == Unreachable, $"{name}: flights are ONE-WAY, 3 -> 0 is -1");
            Check(solve(4, example, 0, 1, 0) == 100, $"{name}: k = 0 still allows one flight");
            Check(solve(1, Array.Empty<int[]>(), 0, 0, 0) == 0, $"{name}: one city, no flights, src == dst");
            Check(solve(3, Array.Empty<int[]>(), 0, 2, 5) == Unreachable, $"{name}: no flights at all -> -1");
            Check(solve(4, example, 0, 3, 50) == 400, $"{name}: a budget past n - 1 is just Dijkstra");
        }

        Console.WriteLine($"  k = 0            -> {FindCheapestPrice(4, example, 0, 3, 0)}");
        Console.WriteLine($"  src == dst       -> {FindCheapestPrice(4, example, 0, 0, 0)}");
        Console.WriteLine($"  wrong direction  -> {FindCheapestPrice(4, example, 3, 0, 5)}");
        Console.WriteLine($"  huge budget      -> {FindCheapestPrice(4, example, 0, 3, 50)}");

        // A cycle in the graph must not turn into a cheaper answer or a hang: the
        // example's 0 -> 1 -> 2 -> 0 loop costs 300 and is never worth flying.
        Check(FindCheapestPrice(4, example, 0, 0, 3) == 0, "flying a positive-cost cycle never beats staying put");

        // Parallel edges: the constraints forbid them, real schedules are full of
        // them, and every version here takes the cheaper without a special case.
        int[][] parallel =
        {
            new[] { 0, 1, 500 },
            new[] { 0, 1, 100 },
            new[] { 1, 2, 100 },
        };
        foreach (var (name, solve) in Solvers)
            Check(solve(3, parallel, 0, 2, 1) == 200, $"{name}: parallel flights, cheaper one wins");

        // A self-loop is a legal [from, to, price] and is always a waste of a hop.
        int[][] selfLoop = { new[] { 0, 0, 1 }, new[] { 0, 1, 50 } };
        foreach (var (name, solve) in Solvers)
            Check(solve(2, selfLoop, 0, 1, 3) == 50, $"{name}: a self-loop is never worth taking");

        Console.WriteLine("  cycles, parallel flights and self-loops: all handled without a special case");

        Console.WriteLine();
        Console.WriteLine("== the two wrong versions, on the inputs that expose them ==");

        int inPlace = FindCheapestPriceInPlace(4, example, 0, 3, 1);
        Console.WriteLine($"  in-place relaxation, k = 1: {inPlace} (correct answer 700)");
        Console.WriteLine("  It found 0->1->2->3 -- a real route, three flights, one round. The");
        Console.WriteLine("  snapshot is the only thing standing between a round and a whole path.");
        Check(inPlace == 400, "in-place relaxation under-reports by using more flights than the budget");
        Check(FindCheapestPrice(4, example, 0, 3, 1) == 700, "the snapshot version does not");

        // Dijkstra with a visited set: cheap-but-long to city 2 hides
        // expensive-but-short, and the expensive one was the only one that fits.
        int[][] trap =
        {
            new[] { 0, 1, 1 },
            new[] { 1, 2, 1 },
            new[] { 2, 3, 1 },
            new[] { 0, 2, 5 },
        };

        int naive = FindCheapestPriceDijkstraNaive(4, trap, 0, 3, 1);
        Console.WriteLine($"  visited-set Dijkstra, k = 1: {naive} (correct answer 6)");
        Console.WriteLine("  City 2 was settled at cost 2 via two flights, so the cost-5 arrival");
        Console.WriteLine("  with a flight left in hand was discarded -- and it was the only one");
        Console.WriteLine("  that could reach city 3. Not 'a worse answer': NO answer.");
        Check(naive == Unreachable, "visited-set Dijkstra loses the route entirely");
        foreach (var (name, solve) in Solvers)
            Check(solve(4, trap, 0, 3, 1) == 6, $"{name}: the dominated-arrival trap -> 6");
        Check(CheapestItinerary(4, trap, 0, 3, 1).SequenceEqual(new[] { 0, 2, 3 }), "and the route is 0 -> 2 -> 3");

        Console.WriteLine();
        Console.WriteLine("== the answer table (many queries, one origin) ==");

        var row = CheapestPricesFromSource(4, example, 0, 2);
        Console.WriteLine($"  from city 0 within 2 stops: [{string.Join(", ", row)}]");
        for (int dst = 0; dst < 4; dst++)
            Check(row[dst] == FindCheapestPrice(4, example, 0, dst, 2), $"the table agrees at city {dst}");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks against brute force ==");

        var rng = new Random(787);
        bool agreed = true;
        int cases = 0;

        for (int trial = 0; trial < 200; trial++)
        {
            int n = rng.Next(1, 7);
            int k = rng.Next(0, 4);

            var edges = new List<int[]>();
            for (int from = 0; from < n; from++)
                for (int to = 0; to < n; to++)
                    if (from != to && rng.Next(100) < 45)
                        edges.Add(new[] { from, to, rng.Next(1, 21) });

            var flights = edges.ToArray();

            for (int src = 0; src < n; src++)
            {
                var table = CheapestPricesFromSource(n, flights, src, k);

                for (int dst = 0; dst < n; dst++)
                {
                    int want = BruteForce(n, flights, src, dst, k);
                    cases++;

                    foreach (var (_, solve) in Solvers)
                        agreed &= solve(n, flights, src, dst, k) == want;

                    agreed &= table[dst] == want;

                    // The itinerary must be a real sequence of flights, must fit
                    // the budget, and must cost exactly what was quoted -- three
                    // separate things, and "it returned a list" is none of them.
                    var route = CheapestItinerary(n, flights, src, dst, k);
                    agreed &= (route is null) == (want == Unreachable);

                    if (route is null)
                        continue;

                    agreed &= route[0] == src && route[^1] == dst;
                    agreed &= route.Length - 2 <= k;             // cities - 2 == stops

                    int total = 0;
                    for (int i = 0; i + 1 < route.Length; i++)
                    {
                        var leg = flights
                            .Where(f => f[0] == route[i] && f[1] == route[i + 1])
                            .Select(f => f[2])
                            .DefaultIfEmpty(Infinity)
                            .Min();

                        agreed &= leg != Infinity;               // an invented flight
                        total += leg;
                    }

                    agreed &= total == want;
                }
            }
        }

        Check(agreed, "200 random graphs, every src/dst pair: all four solvers and the itinerary match brute force");
        Console.WriteLine($"  {cases} src/dst/k combinations checked against enumerated walks: agree = {agreed}");

        Console.WriteLine();
        Console.WriteLine("== input validation ==");

        try
        {
            FindCheapestPrice(3, new[] { new[] { 0, 9, 100 } }, 0, 2, 1);
            Check(false, "a flight to a city that does not exist should throw");
        }
        catch (ArgumentException)
        {
            Check(true, "a flight off the map is rejected, not silently indexed");
        }

        try
        {
            FindCheapestPrice(3, Array.Empty<int[]>(), 0, 2, -1);
            Check(false, "a negative budget should throw");
        }
        catch (ArgumentOutOfRangeException)
        {
            Check(true, "a negative stop budget is rejected");
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"All {_checks} checks passed."
            : $"{_failures} of {_checks} checks FAILED.");
    }
}

// ---- Notes for the follow-up questions ----
//
// "Now each flight also has a duration, and the whole trip must fit in T hours."
//     A second budget is a third state dimension: best[r][v][t]. It stays a DP
//     only while t is small and discrete; with continuous time the honest answer
//     is that this becomes a resource-constrained shortest path, which is NP-hard
//     in general, and you fall back to Lagrangian relaxation or labelling with
//     Pareto-dominance (keep every (cost, time) pair where neither dominates).
//     The dominance rule in the Dijkstra above is the one-dimensional case of
//     exactly that, which is a good way to say it.
//
// "Many queries, same source." -> CheapestPricesFromSource: one O(k * E) pass,
//     then O(1) per query. "Many queries, same DESTINATION" is the same trick on
//     the reversed graph. "All pairs, no stop limit" is Floyd-Warshall at
//     O(n^3) = 10^6 here; with a stop limit it is repeated squaring of the
//     min-plus adjacency matrix -- O(n^3 log k) -- which is the answer that
//     actually impresses, since best[2r] = best[r] (x) best[r] under (min, +).
//
// "What if k is huge -- say 10^9?" The constraint is vacuous past n - 1 flights
//     (no optimal route revisits a city, since prices are positive), so clamp k
//     to n - 1 and run plain Dijkstra. Notice this before writing the loop, not
//     after it times out.
//
// "What if some prices are negative -- refunds, subsidies?" The round-bounded
//     Bellman-Ford above is the ONLY version in this file that survives: a
//     bounded number of flights means a negative cycle cannot be milked forever.
//     Every Dijkstra here breaks, because a later, cheaper arrival can no longer
//     be ruled out by pop order. Say which of your solutions the change kills.
//
// "The graph is enormous and dst is nearby." Bidirectional or A* -- but the stop
//     budget has to be split across the two halves, which is fiddly enough that
//     it is worth pricing before promising. A better first move on a huge sparse
//     graph is the layered BFS above: it touches only cities that improved.
//
// "Why not just BFS by hops and take the cheapest at depth <= k + 1?" That is
//     precisely the layered version -- as long as "BFS" means relaxing costs per
//     level, not visiting each city once. A visited-once BFS answers "fewest
//     flights", which the first example already shows is a different question.
