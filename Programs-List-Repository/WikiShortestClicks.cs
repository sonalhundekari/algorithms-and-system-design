// Wiki Page Shortest-Click Path (+ crawl-all-reachable, + threaded crawl)
// Difficulty: Easy-Medium base case, Medium-Hard follow-up
// Pattern: BFS over an IMPLICIT graph that is only readable through an API
//
// You are handed one helper and nothing else:
//
//     GetLinkedPages(uri) -> IReadOnlyList<string>   // every page `uri` links out to
//
// Task 1: fewest clicks from startUri to targetUri.
// Task 2 (the other half of the loop): every page reachable from a start page.
// Task 3 (follow-up): do Task 2 on several threads without crawling a page twice.
//
//     GetLinkedPages("A") -> ["B", "C"]
//     GetLinkedPages("B") -> ["D"]
//     GetLinkedPages("C") -> ["D", "E"]
//     GetLinkedPages("D") -> []
//     ShortestClicks("A", "D") == 2
//
// WHAT TO PIN DOWN BEFORE WRITING ANYTHING. The API hides the entire graph, so
// every one of these is a real question and none of them is pedantry:
//
//   1. Are links directed? On a wiki, yes -- A linking to B says nothing about B
//      linking to A. That kills "just walk both ways", and it is why the
//      bidirectional variant at the bottom needs a SECOND API.
//   2. Is the URI the identity? "/wiki/Cat", "/wiki/cat#Anatomy" and
//      "https://.../wiki/Cat" are one page and three strings. If the API does not
//      canonicalize, YOU must -- otherwise the visited set stops deduping, and on
//      a cyclic graph the traversal simply never ends.
//   3. What comes back for a page that does not exist (a red link)? An empty
//      list, or a throw? Here: empty list -- see PageGraph.
//   4. Is the reachable set finite, and does it fit in memory? "All of Wikipedia"
//      is ~7M nodes; the visited set is the memory bound, so the honest answer is
//      a BOUNDED crawl (maxClicks / page budget), not "BFS the internet".
//   5. Unreachable target: -1, null, or throw? Pick one and say it out loud.
//      Here: -1, matching the rest of this tree.
//   6. Is the API slow or rate-limited? That is the whole reason Tasks 2 and 3
//      exist, and it changes the cost model below.
//
// THE COST MODEL IS API CALLS, NOT COMPARISONS. Edges are only readable by
// spending a network round trip, so the number that matters is how many times
// GetLinkedPages is called. BFS that marks visited AT ENQUEUE TIME calls it
// exactly once per reachable page: O(V) calls, O(V + E) local work, O(V) memory.
//
//   THE TRAP: marking visited on DEQUEUE instead. Be precise about what that
//   actually costs, because the sloppy version is not as wrong as it is usually
//   described and an interviewer will push on an overstatement. Single-threaded,
//   it still fetches each page exactly once -- the dequeue-time check catches
//   the duplicates before they are expanded. What it loses:
//
//     * The QUEUE grows to O(E), not O(V): every parent that links to a page
//       pushes its own copy. One hub page with a million backlinks is a million
//       queue entries, all of them destined to be thrown away.
//     * The check stops working the moment Task 3 arrives. Two workers can pop
//       the same URI at the same instant, both find it unvisited, and both fetch
//       it. Marking at enqueue is what makes "one call per page" a property of
//       the ALGORITHM rather than of the scheduling.
//
//   Both are measured in the tests below, on a graph small enough to verify by
//   hand: 6 pushes vs 9 for the same six fetches.
//
// WHY BFS AND NOT DFS FOR TASK 1. BFS pops in non-decreasing distance order, so
// the first arrival at a page is already along a shortest path and can never be
// improved. DFS commits to one branch to its end, so every distance it records
// stays provisional -- you would have to revisit pages whenever a cheaper route
// appears, which is exhaustive search rather than traversal. "First touch is
// final" is exactly what BFS buys, and it holds only because every click costs
// the same 1. (Weight the clicks and you are in Dijkstra.)
//
// Time:  O(V + E) local work, O(V) API calls
// Space: O(V) for the visited set and the frontier

using System.Collections.Concurrent;

namespace CodingPatterns.Graphs;

public static class WikiShortestClicks
{
    /// <summary>Target not reachable (or not reachable within the budget).</summary>
    public const int Unreachable = -1;

    /// <summary>The provided helper: every page this URI links out to.</summary>
    public delegate IReadOnlyList<string> LinkApi(string uri);

    // ------------------------------------------------------------ Task 1: BFS

    /// <summary>
    /// Fewest clicks from <paramref name="startUri"/> to <paramref name="targetUri"/>,
    /// or <see cref="Unreachable"/>.
    ///
    /// <paramref name="maxClicks"/> bounds the search for the real-world case
    /// where the reachable set is too large to exhaust; null means "run until the
    /// frontier dies". Note that a budget miss and a genuinely unreachable target
    /// both come back as -1 -- if the caller must tell them apart, that is a
    /// third return value, not a cleverer loop.
    /// </summary>
    public static int ShortestClicks(string startUri, string targetUri, LinkApi getLinkedPages, int? maxClicks = null)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);

        if (startUri == targetUri)
            return 0;                                   // the case everyone forgets

        var frontier = new Queue<(string Uri, int Clicks)>();
        var visited = new HashSet<string> { startUri }; // marked on ENQUEUE: one call per page

        frontier.Enqueue((startUri, 0));

        while (frontier.Count > 0)
        {
            var (uri, clicks) = frontier.Dequeue();
            if (maxClicks is int budget && clicks >= budget)
                continue;                               // cannot afford to expand this one

            foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
            {
                if (!visited.Add(link))                 // Add returns false if already there
                    continue;                           // -- the check and the mark in one step
                if (link == targetUri)
                    return clicks + 1;                  // first touch is already shortest

                frontier.Enqueue((link, clicks + 1));
            }
        }

        return Unreachable;
    }

    /// <summary>
    /// The clicks themselves, start..target inclusive, or null when unreachable.
    ///
    /// Same BFS; the only addition is a parent pointer written the moment a page
    /// is first enqueued -- which is the moment its distance is settled, so the
    /// chain those pointers form is a genuine shortest path. Count - 1 always
    /// equals <see cref="ShortestClicks"/>.
    /// </summary>
    public static List<string> ShortestPath(string startUri, string targetUri, LinkApi getLinkedPages)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);

        if (startUri == targetUri)
            return new List<string> { startUri };

        var parent = new Dictionary<string, string> { [startUri] = null };
        var frontier = new Queue<string>();
        frontier.Enqueue(startUri);

        while (frontier.Count > 0)
        {
            string uri = frontier.Dequeue();

            foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
            {
                if (!parent.TryAdd(link, uri))          // parent doubles as the visited set
                    continue;
                if (link == targetUri)
                    return Rebuild(parent, link);

                frontier.Enqueue(link);
            }
        }

        return null;
    }

    private static List<string> Rebuild(Dictionary<string, string> parent, string end)
    {
        var path = new List<string>();
        for (string at = end; at is not null; at = parent[at])
            path.Add(at);

        path.Reverse();                                 // walked target -> start
        return path;
    }

    // ---------------------------------------------- Task 2: crawl everything

    /// <summary>
    /// Every page reachable from <paramref name="startUri"/>, itself included.
    ///
    /// Order does not affect the ANSWER here -- the reachable set is the
    /// reachable set -- so the DFS/BFS argument stops being about correctness.
    /// See <see cref="CrawlAllDepthFirst"/> for why BFS is still the one to write.
    /// </summary>
    public static HashSet<string> CrawlAll(string startUri, LinkApi getLinkedPages)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);

        var visited = new HashSet<string> { startUri };
        var frontier = new Queue<string>();
        frontier.Enqueue(startUri);

        while (frontier.Count > 0)
            foreach (var link in getLinkedPages(frontier.Dequeue()) ?? Array.Empty<string>())
                if (visited.Add(link))
                    frontier.Enqueue(link);

        return visited;
    }

    /// <summary>
    /// The same set, depth-first. Kept so the comparison can be made concrete --
    /// both are O(V) calls, and BFS is still the better crawl:
    ///
    ///   * Stack depth. The natural DFS is recursive and link chains are long; a
    ///     deep enough chain is a StackOverflowException, which in .NET cannot
    ///     even be caught. This version is explicitly stack-based for exactly
    ///     that reason -- already an admission that the elegant form does not
    ///     survive contact with a real graph.
    ///   * A partial result is useless. Stop a DFS early and you hold one thin
    ///     thread dangling into the far side of the graph. Stop a BFS early and
    ///     you hold "everything within k clicks" -- complete, meaningful, and the
    ///     reason depth limits, budgets and timeouts are expressible at all.
    ///   * Locality and politeness. BFS finishes a host's neighbourhood before
    ///     wandering off; DFS hops hosts every single step, which is precisely
    ///     what per-host rate limiters punish.
    ///   * Parallelism. A BFS frontier is a batch of independent, equal-cost
    ///     calls -- the threaded crawl below is this same loop with a worker
    ///     pool. DFS is inherently sequential: the next call depends on the
    ///     result of the last one.
    ///   * Distances come free. BFS already has them; DFS does not, so the moment
    ///     the interviewer asks "and how far is each page?" the DFS is rewritten.
    /// </summary>
    public static HashSet<string> CrawlAllDepthFirst(string startUri, LinkApi getLinkedPages)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);

        var visited = new HashSet<string> { startUri };
        var stack = new Stack<string>();
        stack.Push(startUri);

        while (stack.Count > 0)
            foreach (var link in getLinkedPages(stack.Pop()) ?? Array.Empty<string>())
                if (visited.Add(link))
                    stack.Push(link);

        return visited;
    }

    /// <summary>
    /// Reachable pages mapped to their click distance. The sequential answer that
    /// both threaded crawls below are checked against.
    /// </summary>
    public static Dictionary<string, int> CrawlDistances(string startUri, LinkApi getLinkedPages)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);

        var dist = new Dictionary<string, int> { [startUri] = 0 };
        var frontier = new Queue<string>();
        frontier.Enqueue(startUri);

        while (frontier.Count > 0)
        {
            string uri = frontier.Dequeue();
            foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
                if (dist.TryAdd(link, dist[uri] + 1))
                    frontier.Enqueue(link);
        }

        return dist;
    }

    // ------------------------------------- Task 3: the same crawl, threaded
    //
    // Two things are hard, and neither of them is the traversal:
    //
    //   DEDUPE. "Check visited, then add, then enqueue" has to be ONE atomic
    //   step. If two workers interleave between the check and the add, both
    //   enqueue the same page and it is fetched twice -- rare, silent, and it
    //   recurs on every diamond in the graph. A lock around the pair works;
    //   ConcurrentDictionary.TryAdd fuses them into a single call, which is why
    //   it is the visited set here. Note that a plain ConcurrentDictionary
    //   ContainsKey-then-TryAdd is NOT a fix: the atomicity has to cover the
    //   decision, not just each half of it.
    //
    //   TERMINATION. "Queue is empty" does NOT mean "done" -- a worker may be
    //   mid-call and about to discover fifty more pages. The condition is
    //   queued + in-flight == 0, so the counter is incremented BEFORE a page is
    //   added and decremented only AFTER its expansion has finished enqueueing
    //   its children. Get that order backwards and the crawl exits early on some
    //   runs and not others, which is the worst kind of bug to be asked about.

    /// <summary>
    /// Every reachable page, crawled by <paramref name="workers"/> threads, with
    /// each page fetched exactly once.
    ///
    /// Free-running work queue: no barriers, so a fast branch keeps everyone
    /// busy. The price is that arrival order is no longer level order, so this
    /// returns the SET only -- for distances use
    /// <see cref="CrawlDistancesParallel"/>, and see the note there for why
    /// bolting depths onto this loop quietly produces wrong numbers.
    ///
    /// Threads are right for this despite the work looking CPU-bound: it is
    /// network I/O, and a real implementation would be async all the way down
    /// (SemaphoreSlim for the concurrency cap, Task.WhenAll per level) rather
    /// than blocking a pool thread per page.
    /// </summary>
    public static HashSet<string> CrawlAllParallel(string startUri, LinkApi getLinkedPages, int workers = 4)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);
        if (workers < 1)
            throw new ArgumentOutOfRangeException(nameof(workers), "need at least one worker");

        var visited = new ConcurrentDictionary<string, byte>();
        var queue = new BlockingCollection<string>();
        var failures = new ConcurrentQueue<Exception>();
        int pending = 0;                                // queued + in-flight

        visited.TryAdd(startUri, 0);
        Interlocked.Increment(ref pending);             // count it BEFORE it is visible
        queue.Add(startUri);

        void Work()
        {
            // Blocks until an item arrives; the loop ends when CompleteAdding is
            // called AND the queue has drained.
            foreach (string uri in queue.GetConsumingEnumerable())
            {
                try
                {
                    foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
                    {
                        if (!visited.TryAdd(link, 0))   // the atomic check-and-add
                            continue;

                        Interlocked.Increment(ref pending);
                        queue.Add(link);                // child counted before the parent drops
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);               // a dead worker must not hang the join
                }
                finally
                {
                    // Last one out turns off the lights. Only an in-flight
                    // expander can add, so nothing can arrive after this hits 0.
                    if (Interlocked.Decrement(ref pending) == 0)
                        queue.CompleteAdding();
                }
            }
        }

        var threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
        {
            threads[i] = new Thread(Work) { IsBackground = true, Name = $"crawler-{i}" };
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        queue.Dispose();

        if (!failures.IsEmpty)
            throw new AggregateException("one or more pages failed to fetch", failures);

        return new HashSet<string>(visited.Keys);
    }

    /// <summary>
    /// Reachable pages mapped to click distance, level-synchronous across
    /// <paramref name="workers"/> threads.
    ///
    /// WHY NOT just carry (uri, depth) through the free-running queue above: the
    /// depth recorded is the depth of whichever worker DISCOVERED the page first,
    /// and with several workers in flight that is no longer the smallest depth. A
    /// worker holding a depth-3 page can finish its call before another worker
    /// has even started expanding a depth-1 page that also links there, and the
    /// page is then permanently recorded as 4. Nothing crashes; the numbers are
    /// just wrong, on some runs and not others. Fixing it by relaxing (re-expand
    /// whenever a shorter route turns up) throws away "fetch each page once",
    /// which was the whole point.
    ///
    /// A level barrier restores it: the entire frontier is fetched in parallel,
    /// then ONE thread folds the results in, so "first touch" is decided in level
    /// order exactly as in the sequential BFS. Deduping single-threaded between
    /// levels also means there is no lock anywhere. The cost is the barrier -- a
    /// level runs only as fast as its slowest page -- and that is the honest
    /// trade to state out loud: exact distances OR maximum throughput, with the
    /// free-running crawl above being the other side of it.
    /// </summary>
    public static Dictionary<string, int> CrawlDistancesParallel(string startUri, LinkApi getLinkedPages, int workers = 4)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);
        if (workers < 1)
            throw new ArgumentOutOfRangeException(nameof(workers), "need at least one worker");

        var dist = new Dictionary<string, int> { [startUri] = 0 };
        var frontier = new List<string> { startUri };
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };

        for (int depth = 1; frontier.Count > 0; depth++)
        {
            var batches = new IReadOnlyList<string>[frontier.Count];

            // Index-keyed results, so the fold below is deterministic even though
            // the fetches complete in whatever order they like.
            Parallel.For(0, frontier.Count, options, i =>
                batches[i] = getLinkedPages(frontier[i]) ?? Array.Empty<string>());

            var next = new List<string>();
            foreach (var links in batches)
                foreach (var link in links)
                    if (dist.TryAdd(link, depth))
                        next.Add(link);

            frontier = next;
        }

        return dist;
    }

    // ----------------------------------------------------- rate-limit follow-up

    /// <summary>
    /// Memoize the API. The cheapest call is the one you never make.
    ///
    /// A single BFS never repeats a page anyway, so this pays off ACROSS runs
    /// (many shortest-click queries over one neighbourhood) and it makes a retry
    /// after a partial failure nearly free. GetOrAdd may run the factory twice
    /// for the same key under contention -- harmless here, since the fetch is
    /// idempotent and only one result is stored; if a duplicate fetch is itself
    /// expensive, cache a Lazy&lt;T&gt; instead so the second caller waits on the
    /// first rather than racing it.
    ///
    /// The rest of the rate-limit answer, which caching does not cover:
    ///   * Batch. If the API takes many URIs per request, fetch a whole BFS level
    ///     in one call -- another reason the frontier is the right unit of work.
    ///   * Size the concurrency to the QUOTA, not to the CPU: a SemaphoreSlim or
    ///     a token bucket at the allowed requests/second.
    ///   * Retry 429/5xx with exponential backoff plus jitter, and keep the retry
    ///     inside the same in-flight unit so the termination accounting holds.
    ///   * Persist the cache and the visited set if the crawl can be resumed.
    ///   * Evict with an LRU + TTL if the neighbourhood outgrows memory; links
    ///     change, so an unbounded forever-cache is also a staleness decision.
    /// </summary>
    public static LinkApi Cached(LinkApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var store = new ConcurrentDictionary<string, IReadOnlyList<string>>();
        return uri => store.GetOrAdd(uri, key => api(key) ?? Array.Empty<string>());
    }

    // -------------------------------------------------------- bidirectional

    /// <summary>
    /// The same answer as <see cref="ShortestClicks"/>, meeting in the middle.
    ///
    /// Worth MENTIONING in a screen, rarely worth writing, and it carries a hard
    /// precondition that people skip: it needs a reverse index -- "who links TO
    /// this page" -- which a forward-only link API cannot give you. On a real
    /// wiki that is a second service or a precomputed backlink table, so the
    /// honest line is "if backlinks exist, here is the win; if not, this option
    /// is not on the table".
    ///
    /// The win where it does exist: one-directional BFS touches ~b^d pages, two
    /// halves touch ~2 * b^(d/2). At branching factor 100 and d = 6 that is 10^12
    /// versus 2 * 10^6 -- the difference between impossible and instant.
    ///
    /// Two details that are the whole correctness argument:
    ///   * Expand the SMALLER frontier each round. That is what keeps the halves
    ///     balanced when in-degree and out-degree differ wildly, as they do on a
    ///     wiki (one popular page has a million backlinks and forty links out).
    ///   * FINISH the level before returning. Bailing out at the first touch
    ///     between the two searches is the classic bug: the first meeting found
    ///     is not necessarily on a shortest path, so the level is completed and
    ///     the minimum over all meetings in it is taken.
    /// </summary>
    public static int BidirectionalClicks(
        string startUri, string targetUri, LinkApi getLinkedPages, LinkApi getLinkingPages)
    {
        ArgumentNullException.ThrowIfNull(getLinkedPages);
        ArgumentNullException.ThrowIfNull(getLinkingPages);

        if (startUri == targetUri)
            return 0;

        var distForward = new Dictionary<string, int> { [startUri] = 0 };
        var distBackward = new Dictionary<string, int> { [targetUri] = 0 };
        var frontForward = new List<string> { startUri };
        var frontBackward = new List<string> { targetUri };

        while (frontForward.Count > 0 && frontBackward.Count > 0)
        {
            bool goForward = frontForward.Count <= frontBackward.Count;

            var front = goForward ? frontForward : frontBackward;
            var mine = goForward ? distForward : distBackward;
            var theirs = goForward ? distBackward : distForward;
            var api = goForward ? getLinkedPages : getLinkingPages;

            var next = new List<string>();
            int best = int.MaxValue;

            foreach (string uri in front)
            {
                foreach (var link in api(uri) ?? Array.Empty<string>())
                {
                    if (theirs.TryGetValue(link, out int other))
                        best = Math.Min(best, mine[uri] + 1 + other);
                    if (mine.TryAdd(link, mine[uri] + 1))
                        next.Add(link);
                }
            }

            if (best != int.MaxValue)
                return best;                            // level finished, so this is minimal

            if (goForward)
                frontForward = next;
            else
                frontBackward = next;
        }

        return Unreachable;
    }

    // ------------------------------------------------------------ simulator
    //
    // The other follow-up: "write GetLinkedPages yourself so we can run this". An
    // adjacency dictionary behind a method, plus a per-URI call counter -- and the
    // counter is not decoration. "Every page expanded exactly once" is the
    // property this whole problem is about, and the counter is the only thing
    // that can assert it.

    public sealed class PageGraph
    {
        private readonly Dictionary<string, List<string>> _adjacency;
        private readonly int _latencyMs;
        private readonly ConcurrentDictionary<string, int> _calls = new();

        public PageGraph(Dictionary<string, IEnumerable<string>> adjacency, int latencyMs = 0)
        {
            _adjacency = adjacency.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
            _latencyMs = latencyMs;
        }

        /// <summary>The API under test. Thread-safe, because Task 3 calls it from everywhere.</summary>
        public IReadOnlyList<string> GetLinkedPages(string uri)
        {
            _calls.AddOrUpdate(uri, 1, (_, n) => n + 1);

            if (_latencyMs > 0)
                Thread.Sleep(_latencyMs);               // stands in for the round trip

            // A link to a page that does not exist is a wiki red link, not an
            // error. Returning empty here is a CONTRACT DECISION -- ask, do not
            // assume, and note that it makes the dangling URI a reachable page.
            return _adjacency.TryGetValue(uri, out var links) ? links : Array.Empty<string>();
        }

        /// <summary>Backlinks. A real link API usually does NOT have this -- see above.</summary>
        public IReadOnlyList<string> GetLinkingPages(string uri) =>
            _adjacency.Where(kv => kv.Value.Contains(uri)).Select(kv => kv.Key).ToList();

        public int CallCount => _calls.Values.Sum();

        public int CallsFor(string uri) => _calls.TryGetValue(uri, out int n) ? n : 0;

        /// <summary>Pages fetched more than once. Must always be empty.</summary>
        public List<string> Refetched =>
            _calls.Where(kv => kv.Value > 1).Select(kv => kv.Key).OrderBy(u => u).ToList();

        public void Reset() => _calls.Clear();
    }

    // ---------------------------------------------------------------- tests

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

    public static void Main()
    {
        _checks = _failures = 0;

        Console.WriteLine("== the interview's own example ==");

        var example = new PageGraph(new Dictionary<string, IEnumerable<string>>
        {
            ["A"] = new[] { "B", "C" },
            ["B"] = new[] { "D" },
            ["C"] = new[] { "D", "E" },
            ["D"] = Array.Empty<string>(),
        });

        Console.WriteLine($"  ShortestClicks(A, D) = {ShortestClicks("A", "D", example.GetLinkedPages)} (expect 2)");
        Console.WriteLine($"  ShortestClicks(A, B) = {ShortestClicks("A", "B", example.GetLinkedPages)} (expect 1)");
        Console.WriteLine($"  ShortestPath(A, D)   = [{string.Join(" -> ", ShortestPath("A", "D", example.GetLinkedPages))}]");

        Check(ShortestClicks("A", "D", example.GetLinkedPages) == 2, "A -> D is 2 clicks");
        Check(ShortestClicks("A", "E", example.GetLinkedPages) == 2, "A -> E is 2 clicks");
        Check(ShortestClicks("A", "B", example.GetLinkedPages) == 1, "A -> B is 1 click");

        var abd = ShortestPath("A", "D", example.GetLinkedPages);
        Check(abd.Count == 3 && abd[0] == "A" && abd[2] == "D", "the path has the right ends and length");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        Check(ShortestClicks("A", "A", example.GetLinkedPages) == 0, "start == target is 0 clicks");
        Check(ShortestPath("A", "A", example.GetLinkedPages).SequenceEqual(new[] { "A" }), "the zero-click path is [start]");
        Check(ShortestClicks("D", "A", example.GetLinkedPages) == Unreachable, "links are DIRECTED: D -> A is -1");
        Check(ShortestClicks("A", "Nope", example.GetLinkedPages) == Unreachable, "missing target is -1");
        Check(ShortestPath("D", "A", example.GetLinkedPages) is null, "unreachable path is null");
        Check(ShortestClicks("Ghost", "A", example.GetLinkedPages) == Unreachable, "start outside the graph is -1");

        Console.WriteLine($"  start == target      -> {ShortestClicks("A", "A", example.GetLinkedPages)}");
        Console.WriteLine($"  backwards (D -> A)   -> {ShortestClicks("D", "A", example.GetLinkedPages)}");
        Console.WriteLine($"  target does not exist -> {ShortestClicks("A", "Nope", example.GetLinkedPages)}");

        // Cycles and self-links must terminate, and must not re-fetch anything.
        var loops = new PageGraph(new Dictionary<string, IEnumerable<string>>
        {
            ["A"] = new[] { "A", "B" },                 // self-link
            ["B"] = new[] { "A", "C" },
            ["C"] = new[] { "B", "A" },                 // cycle back
        });

        Check(ShortestClicks("A", "C", loops.GetLinkedPages) == 2, "a self-link and a cycle do not hang");

        loops.Reset();                                  // per-run counters, so measure one run
        Check(CrawlAll("A", loops.GetLinkedPages).SetEquals(new[] { "A", "B", "C" }), "cyclic crawl finds all three");
        Check(loops.Refetched.Count == 0, "cyclic crawl fetches nothing twice");
        Console.WriteLine("  self-link + cycle: terminates, nothing re-fetched");

        // A budget stops the search early -- and reports "not within budget" the
        // same way it reports "unreachable".
        var chain = new PageGraph(new Dictionary<string, IEnumerable<string>>
        {
            ["A"] = new[] { "B" }, ["B"] = new[] { "C" }, ["C"] = new[] { "D" },
            ["D"] = new[] { "E" }, ["E"] = new[] { "F" },
        });

        Check(ShortestClicks("A", "F", chain.GetLinkedPages) == 5, "the 5-link chain measures 5");
        Check(ShortestClicks("A", "F", chain.GetLinkedPages, maxClicks: 3) == Unreachable, "the budget cuts the search off");
        Console.WriteLine($"  chain A..F: {ShortestClicks("A", "F", chain.GetLinkedPages)} clicks, " +
                          $"{ShortestClicks("A", "F", chain.GetLinkedPages, maxClicks: 3)} under a 3-click budget");

        Console.WriteLine();
        Console.WriteLine("== ONE API call per visited page (the point of the problem) ==");

        example.Reset();
        ShortestClicks("A", "D", example.GetLinkedPages);
        Console.WriteLine($"  A -> D expanded: A={example.CallsFor("A")} B={example.CallsFor("B")} " +
                          $"C={example.CallsFor("C")} D={example.CallsFor("D")}");
        Console.WriteLine("  Only 2 calls for a 5-page graph: the target is recognised when it is");
        Console.WriteLine("  DISCOVERED, so neither D nor its level-mate C is ever expanded.");
        Check(example.CallCount == 2, "2 calls: found on discovery, so C is never expanded either");
        Check(example.CallsFor("D") == 0, "the target itself is never expanded");
        Check(example.Refetched.Count == 0, "no page fetched twice");

        // The enqueue-time vs dequeue-time comparison needs a graph with a
        // diamond in it, so it lives with the crawl below.

        Console.WriteLine();
        Console.WriteLine("== crawl-all: one graph with a cycle AND a diamond ==");

        //   HOME -> A -> C -> D          D links back to HOME (cycle)
        //   HOME -> B -> C               A and B share C      (diamond)
        //           B -> D               D -> E is the deep tail
        //   ISLAND links IN to HOME, so it is NOT reachable from HOME.
        var site = new PageGraph(new Dictionary<string, IEnumerable<string>>
        {
            ["HOME"] = new[] { "A", "B" },
            ["A"] = new[] { "C" },
            ["B"] = new[] { "C", "D" },
            ["C"] = new[] { "D" },
            ["D"] = new[] { "HOME", "E" },
            ["E"] = Array.Empty<string>(),
            ["ISLAND"] = new[] { "HOME" },
        });

        var expected = new HashSet<string> { "HOME", "A", "B", "C", "D", "E" };
        var expectedDistances = new Dictionary<string, int>
        {
            ["HOME"] = 0, ["A"] = 1, ["B"] = 1, ["C"] = 2, ["D"] = 2, ["E"] = 3,
        };

        site.Reset();
        var bfsSet = CrawlAll("HOME", site.GetLinkedPages);
        Check(bfsSet.SetEquals(expected), "BFS crawl finds exactly the reachable pages");
        Check(site.CallCount == expected.Count && site.Refetched.Count == 0, "BFS crawl: exactly V calls");
        Console.WriteLine($"  BFS crawl: {{{string.Join(", ", bfsSet.OrderBy(u => u))}}} in {site.CallCount} calls");

        site.Reset();
        Check(CrawlAllDepthFirst("HOME", site.GetLinkedPages).SetEquals(expected), "DFS finds the same set");
        Check(site.Refetched.Count == 0, "DFS crawl: exactly V calls too");

        var distances = CrawlDistances("HOME", site.GetLinkedPages);
        Check(distances.Count == expectedDistances.Count && expectedDistances.All(kv => distances[kv.Key] == kv.Value),
            "click distances are right");
        Check(!bfsSet.Contains("ISLAND"), "ISLAND links IN, so it is not reachable OUT of HOME");
        Console.WriteLine($"  distances: {string.Join(", ", distances.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"))}");

        // Enqueue-time vs dequeue-time marking, measured on this graph: 6
        // reachable pages joined by 8 links, so the queue traffic differs even
        // though the fetch count does not.
        var atEnqueue = QueueTraffic("HOME", site.GetLinkedPages, markOnDequeue: false);
        var atDequeue = QueueTraffic("HOME", site.GetLinkedPages, markOnDequeue: true);

        Console.WriteLine($"  mark on ENQUEUE: {atEnqueue.Enqueued} pushes, {atEnqueue.Fetched} fetches  (V and V)");
        Console.WriteLine($"  mark on DEQUEUE: {atDequeue.Enqueued} pushes, {atDequeue.Fetched} fetches  (E + 1 and V)");
        Check(atEnqueue.Enqueued == 6 && atEnqueue.Fetched == 6, "enqueue-time marking pushes each page once");
        Check(atDequeue.Enqueued == 9 && atDequeue.Fetched == 6, "dequeue-time marking pushes every LINK, fetches the same");

        Console.WriteLine();
        Console.WriteLine("== threaded crawl: same set, each page expanded exactly once ==");

        foreach (int poolSize in new[] { 1, 2, 4, 8 })
        {
            site.Reset();
            var parallelSet = CrawlAllParallel("HOME", site.GetLinkedPages, poolSize);
            Check(parallelSet.SetEquals(expected), $"{poolSize} workers find the same set");
            Check(site.Refetched.Count == 0, $"{poolSize} workers never double-fetch a page");
            Check(site.CallCount == expected.Count, $"{poolSize} workers spend exactly V calls");

            site.Reset();
            var parallelDistances = CrawlDistancesParallel("HOME", site.GetLinkedPages, poolSize);
            Check(expectedDistances.All(kv => parallelDistances[kv.Key] == kv.Value),
                $"{poolSize} workers reproduce the sequential distances");
            Check(site.Refetched.Count == 0, $"{poolSize} workers never double-fetch while measuring");

            Console.WriteLine($"  {poolSize} worker(s): {parallelSet.Count} pages, {site.CallCount} calls, " +
                              $"{site.Refetched.Count} re-fetches");
        }

        // Why the check-and-add has to be ONE step. A wide fan into a single
        // shared child is the shape that exposes it: eight workers expand eight
        // parents at once and all of them look at SHARED before any of them has
        // written it. The count below is not asserted -- it is a race, so it can
        // legitimately come out at zero on a lucky run, and "it passed" is
        // exactly how this bug survives to production.
        var fan = new Dictionary<string, IEnumerable<string>> { ["ROOT"] = Enumerable.Range(0, 8).Select(i => $"c{i}").ToList() };
        for (int i = 0; i < 8; i++)
            fan[$"c{i}"] = new[] { "SHARED" };
        fan["SHARED"] = Array.Empty<string>();

        var fanGraph = new PageGraph(fan, latencyMs: 2);

        fanGraph.Reset();
        CrawlAllParallel("ROOT", fanGraph.GetLinkedPages, 8);
        int safeDuplicates = fanGraph.CallCount - 10;

        fanGraph.Reset();
        CrawlAllParallelRacy("ROOT", fanGraph.GetLinkedPages, 8);
        int racyDuplicates = fanGraph.CallCount - 10;

        Console.WriteLine($"  8 parents -> 1 shared child: TryAdd {safeDuplicates} duplicate fetches, " +
                          $"check-then-add {racyDuplicates}");
        Check(safeDuplicates == 0, "the atomic check-and-add never double-fetches");

        // A worker that throws must surface, not deadlock the join.
        try
        {
            CrawlAllParallel("HOME", uri => uri == "B"
                ? throw new HttpRequestException("503 from the wiki")
                : site.GetLinkedPages(uri));
            Check(false, "the worker's exception should have propagated");
        }
        catch (AggregateException ex)
        {
            Check(ex.InnerExceptions.Any(e => e.Message.Contains("503")), "a failing fetch surfaces instead of hanging");
            Console.WriteLine($"  a page that 503s: {ex.InnerExceptions.Count} failure(s) surfaced, no deadlock");
        }

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(31);
        bool agreed = true;

        for (int trial = 0; trial < 60; trial++)
        {
            int n = rng.Next(1, 25);
            var names = Enumerable.Range(0, n).Select(i => $"p{i}").ToList();
            var adjacency = new Dictionary<string, IEnumerable<string>>();

            foreach (var name in names)
            {
                int outDegree = rng.Next(0, Math.Min(5, n + 1));
                var links = new HashSet<string>();
                for (int k = 0; k < outDegree; k++)
                    links.Add(names[rng.Next(n)]);      // self-links included on purpose
                adjacency[name] = links.ToList();
            }

            var graph = new PageGraph(adjacency);
            var wantSet = CrawlAll("p0", graph.GetLinkedPages);
            var wantDist = CrawlDistances("p0", graph.GetLinkedPages);

            graph.Reset();
            agreed &= CrawlAllParallel("p0", graph.GetLinkedPages, 4).SetEquals(wantSet);
            agreed &= graph.Refetched.Count == 0 && graph.CallCount == wantSet.Count;

            graph.Reset();
            var gotDist = CrawlDistancesParallel("p0", graph.GetLinkedPages, 4);
            agreed &= gotDist.Count == wantDist.Count && wantDist.All(kv => gotDist[kv.Key] == kv.Value);
            agreed &= graph.Refetched.Count == 0;

            agreed &= CrawlAllDepthFirst("p0", graph.GetLinkedPages).SetEquals(wantSet);

            // Every pair, against a full BFS distance map from that source.
            foreach (var src in new[] { "p0", names[^1] })
            {
                var reference = CrawlDistances(src, graph.GetLinkedPages);

                foreach (var dst in names)
                {
                    int want = reference.TryGetValue(dst, out int d) ? d : Unreachable;
                    agreed &= ShortestClicks(src, dst, graph.GetLinkedPages) == want;
                    agreed &= BidirectionalClicks(src, dst, graph.GetLinkedPages, graph.GetLinkingPages) == want;

                    var path = ShortestPath(src, dst, graph.GetLinkedPages);
                    agreed &= (path is null) == (want == Unreachable);

                    if (path is null)
                        continue;

                    agreed &= path.Count - 1 == want && path[0] == src && path[^1] == dst;
                    for (int i = 0; i + 1 < path.Count; i++)
                        agreed &= graph.GetLinkedPages(path[i]).Contains(path[i + 1]);   // a real click
                }
            }
        }

        Check(agreed, "60 random graphs: sequential, threaded, DFS and bidirectional all agree");
        Console.WriteLine($"  60 random cyclic graphs, every source/target pair: agree = {agreed}");

        Console.WriteLine();
        Console.WriteLine("== rate-limit follow-up: the cache ==");

        var cachedApi = Cached(site.GetLinkedPages);
        site.Reset();
        CrawlAll("HOME", cachedApi);
        int firstRun = site.CallCount;
        CrawlAll("HOME", cachedApi);
        Console.WriteLine($"  first crawl {firstRun} calls, second crawl {site.CallCount - firstRun} calls (served from cache)");
        Check(site.CallCount == firstRun, "a warm cache costs zero API calls");

        Console.WriteLine();
        Console.WriteLine("== what the threads actually buy (60ms of fake latency per page) ==");

        var slow = new PageGraph(new Dictionary<string, IEnumerable<string>>
        {
            ["HOME"] = new[] { "A", "B" },
            ["A"] = new[] { "C" },
            ["B"] = new[] { "C", "D" },
            ["C"] = new[] { "D" },
            ["D"] = new[] { "HOME", "E" },
            ["E"] = Array.Empty<string>(),
        }, latencyMs: 60);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        CrawlAll("HOME", slow.GetLinkedPages);
        long serialMs = clock.ElapsedMilliseconds;

        clock.Restart();
        CrawlAllParallel("HOME", slow.GetLinkedPages, 4);
        long threadedMs = clock.ElapsedMilliseconds;

        Console.WriteLine($"  6 pages: serial {serialMs}ms, 4 threads {threadedMs}ms");
        Console.WriteLine("  The speedup is capped by the graph's DEPTH, not by the worker count --");
        Console.WriteLine("  level 3 here holds a single page, and one page cannot be split.");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"All {_checks} checks passed."
            : $"{_failures} of {_checks} checks FAILED.");
    }

    /// <summary>
    /// The same crawl under both visited-marking policies, instrumented. Pushes
    /// is the number that separates them: V when a page is marked as it is
    /// enqueued, E + 1 when it is marked as it is dequeued, for identical fetch
    /// counts. Both terminate; only one of them stays O(V) in memory.
    /// </summary>
    private static (int Enqueued, int Fetched) QueueTraffic(string startUri, LinkApi getLinkedPages, bool markOnDequeue)
    {
        var frontier = new Queue<string>();
        var visited = new HashSet<string>();
        int enqueued = 0, fetched = 0;

        frontier.Enqueue(startUri);
        enqueued++;
        if (!markOnDequeue)
            visited.Add(startUri);

        while (frontier.Count > 0)
        {
            string uri = frontier.Dequeue();
            if (markOnDequeue && !visited.Add(uri))
                continue;                               // duplicate copy, thrown away

            fetched++;
            foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
            {
                if (!markOnDequeue && !visited.Add(link))
                    continue;

                frontier.Enqueue(link);
                enqueued++;
            }
        }

        return (enqueued, fetched);
    }

    /// <summary>
    /// DELIBERATELY WRONG, kept runnable: the check and the add are two separate
    /// operations on a thread-safe dictionary, which does not make the PAIR
    /// thread-safe. Two workers reaching the same child page inside that window
    /// both decide to enqueue it, and it is fetched twice.
    ///
    /// The yield only widens a window that is already there -- this is the bug
    /// that shows up once a month in production and never in a test run on a
    /// four-node graph. Correctness of the returned SET survives (a page fetched
    /// twice still lands in the set once); what dies is the cost guarantee, and
    /// with it any hope of being polite to the API.
    /// </summary>
    private static HashSet<string> CrawlAllParallelRacy(string startUri, LinkApi getLinkedPages, int workers)
    {
        var visited = new ConcurrentDictionary<string, byte>();
        var queue = new BlockingCollection<string>();
        int pending = 0;

        visited[startUri] = 0;
        Interlocked.Increment(ref pending);
        queue.Add(startUri);

        void Work()
        {
            foreach (string uri in queue.GetConsumingEnumerable())
            {
                try
                {
                    foreach (var link in getLinkedPages(uri) ?? Array.Empty<string>())
                    {
                        if (visited.ContainsKey(link))  // check...
                            continue;

                        Thread.Yield();                 // ...window...
                        visited[link] = 0;              // ...and add. Not one step.

                        Interlocked.Increment(ref pending);
                        queue.Add(link);
                    }
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0)
                        queue.CompleteAdding();
                }
            }
        }

        var threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
        {
            threads[i] = new Thread(Work) { IsBackground = true };
            threads[i].Start();
        }
        foreach (var thread in threads)
            thread.Join();

        queue.Dispose();
        return new HashSet<string>(visited.Keys);
    }
}

// ---- Notes for the follow-up questions ----
//
// "The graph is huge -- how do you not run out of memory?"
//     The visited set is the bound, not the queue. Hash the URIs to fixed-width
//     keys (or a Bloom filter, accepting that a false positive silently skips a
//     page), spill the frontier to disk, or shard the crawl by URI hash across
//     machines so each one holds only its slice of `visited`. At that point the
//     dedupe is a distributed set, which is a different design question.
//
// "Now the crawler runs on many machines."
//     Partition by hash(uri) % N: the machine that owns a URI is the only one
//     allowed to expand it, so the check-and-add stays local and no lock crosses
//     the network. Discovered links are shipped to their owner. Termination is
//     the same counting argument, one level up -- a distributed barrier or a
//     coordinator tracking outstanding work per shard.
//
// "Pages change while you crawl."
//     The answer becomes a snapshot with no consistency guarantee, and it is
//     worth saying so. If it matters, stamp each fetch with a version/ETag and
//     re-crawl by staleness with a priority queue rather than by breadth.
//
// "Some pages are far more important than others."
//     BFS treats every click as equal. Weight the frontier (a priority queue on
//     estimated value, or A* with a heuristic on the URI) and it stops being a
//     shortest-click answer -- which is fine, but only if the interviewer wanted
//     coverage rather than distance.
//
// "How would you test this against the real API?"
//     Exactly as above: the adjacency dict IS the fake. Keep the API behind a
//     one-method interface, run the whole suite against the fake, and reserve
//     the live API for a smoke test. The call counter is what makes "expanded
//     exactly once" testable at all -- without it, the double-fetch bug is
//     invisible to every assertion you can write about the returned set.
