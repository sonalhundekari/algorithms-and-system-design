// Service Failure Forensics -- first error log, blast radius, longest cascade
// Difficulty: Easy (part 1), Medium (part 2), Medium-Hard (part 3)
// Pattern: monotone-predicate binary search, then reverse-graph reachability,
//          then longest path on a DAG (and what to do when it is not one)
//
// Three parts of one outage post-mortem:
//
//   1. logs[] is tagged [Info] / [Warn] / [Error]. Errors form a SUFFIX. Find the
//      index of the first [Error], or -1.
//   2. calls maps caller -> callees ("A": ["B","C"] means A calls B and C). Given
//      the service that failed first, return every service that fails with it.
//   3. Return one LONGEST chain of failures starting at that service.
//
//        calls = { A: [B, C], B: [D], C: [D], E: [A], F: [C] }
//        first_error_service = D
//        impacted = {D, B, C, A, E, F}
//        a longest chain = D -> B -> A -> E   (4 services deep)
//
// ============================================================================
// PART 1 -- the predicate is the sorted thing, not the array
// ============================================================================
//
// Binary search does not need a sorted array. It needs a predicate that is false
// then true and never flips back. Here that predicate is
//
//       p(i) = logs[i] starts with "[Error]"
//
// and the problem statement hands you its monotonicity ("once an [Error] appears,
// every line after it is also an [Error]"). Everything else about the log format
// is decoration.
//
//   THE RED HERRING. "If there is an error at line i, line i-1 must be a [Warn]"
//   invites you to search for the [Warn] instead. Do not. The rule is
//   error => previous is warn; it is NOT warn => next is error. Warns can sit
//   anywhere in a log that never errors at all, so "is [Warn]" is not monotone
//   and binary search over it returns garbage. What the rule is actually good for
//   is a POSTCONDITION -- once you have an answer k, `k == 0 || IsWarn(logs[k-1])`
//   is a free assertion that your input matched its contract. See IsWellFormed.
//
//   OVERFLOW. `(lo + hi) / 2` is fine in Python and wrong in C#: at lo + hi >
//   int.MaxValue it goes negative and indexes out of range. `lo + (hi - lo) / 2`
//   costs nothing. Nobody's log has 2^31 lines; write it correctly anyway,
//   because the reviewer who spots it will not know that you knew.
//
//   WHY BOTHER WITH log n. On an in-memory list, O(n) vs O(log n) over a log file
//   is noise -- you spent more time reading the file than scanning it. The
//   version that earns its keep is the one where a "read" is a PAGE FETCH against
//   a log service, and the number to quote is reads, not comparisons. That is
//   also the version where you do not know n up front, which is why
//   FirstErrorPaged exists: without a length you cannot compute a midpoint, so
//   you gallop (1, 2, 4, 8, ...) to bracket the answer first. O(log k) reads,
//   where k is the answer, not the log size.
//
//   AND THE HONEST CAVEAT. Real logs are not sorted like this. Several processes
//   write to one stream, clocks skew, buffers flush late, and an [Info] lands
//   after an [Error] routinely. Monotonicity here is a GIFT FROM THE PROBLEM
//   STATEMENT. If it does not hold, binary search does not fail loudly -- it
//   returns *an* error line, not *the first*, and nothing in the output says
//   which. Ask whether the ordering is guaranteed; if the answer is "mostly",
//   the correct algorithm is the linear scan.
//
// ============================================================================
// PART 2 -- the whole problem is which way the arrow points
// ============================================================================
//
// The input is caller -> callee. Failure travels the OTHER WAY: if D dies, the
// services that die with it are the ones that CALL D. So reverse the graph and
// run any traversal you like. Getting this backwards produces a confident,
// well-tested, completely wrong answer -- from D it would report D's own
// dependencies, which are the one set of services that are provably fine.
//
//   Say the direction out loud before you build anything. "A calls B" means A
//   DEPENDS ON B; dependency edges point at what you need, blame edges point at
//   who needed you, and they are transposes.
//
//   THE MISSING KEYS. A leaf service appears only as a value in `calls`, never as
//   a key -- D has no entry in the example. Any `calls[x]` on the raw dict throws
//   or, worse, silently inserts an empty list. Build the reverse adjacency once
//   and let every node be a key in it.
//
//   BFS OR DFS is not a real question when the answer is a SET: both visit every
//   reverse-reachable node exactly once, both are O(V + E). BFS wins on two
//   non-algorithmic grounds. It hands you the HOP COUNT for free, which is what
//   an on-call engineer actually wants ("who is one call away from the fire"),
//   and it cannot blow the stack, which recursive DFS will on a 10^5-deep
//   dependency chain -- and in .NET a StackOverflowException cannot be caught.
//
//   THE MODELLING ASSUMPTION WORTH CHALLENGING. "Any caller of a failed service
//   fails" describes a system with zero resilience. Timeouts, caches, circuit
//   breakers and replica sets all break it, and the moment a service has TWO
//   interchangeable backends, failure stops being reachability: it fails only if
//   ALL of them are down. That is a monotone AND/OR formula, not a traversal --
//   see ImpactedWithRedundancy, where reachability turns out to be the special
//   case in which every dependency group has exactly one member.
//
// ============================================================================
// PART 3 -- longest path, and the complexity claim that is quietly false
// ============================================================================
//
// On a DAG, longest path is linear: relax every node in topological order. The
// textbook answer is a memoized DFS, and it is CORRECT -- but the O(V + E) that
// usually gets written under it is not, when the memo stores paths:
//
//       candidate = [service] + dfs(caller)      # copies the whole chain
//
// Each concatenation copies up to V entries and there are E of them, so that is
// O(V * E) time, and the memo holds up to V lists of up to V entries: O(V^2)
// memory. Both are measured in the tests below on a graph where they bite
// (~1.3M cells copied where the loop below touches ~20k).
//
//   THE FIX IS THE STANDARD ONE: memoize the LENGTH and a successor pointer, and
//   reconstruct the single path you actually return at the end. O(V + E) time,
//   O(V) space, and the path comes out identical.
//
//   AND DO IT ITERATIVELY. Same 10^5-deep chain, same uncatchable overflow.
//   Kahn's algorithm over the impacted subgraph gives the topological order, the
//   relaxation, and cycle detection in ONE pass -- and the seeding is free,
//   because within the impacted set the origin is the unique node with no
//   impacted predecessor (every other impacted node was discovered through one).
//
//   CYCLES ARE NOT HYPOTHETICAL in a service graph -- A calls B calls A is a
//   Tuesday. And "longest simple path" on a general digraph is NP-hard (Hamilton
//   path reduces to it), so there is no clever loop waiting to be found. The
//   answer is to condense strongly connected components first: everything inside
//   an SCC fails together the instant any member does, so the chain becomes a
//   chain of LEVELS with each level weighted by its size, and the condensation is
//   a DAG, so the same relaxation runs on it. Be precise about what that number
//   is: it is the depth of the cascade counting every service in each level, and
//   it is an UPPER BOUND on the longest simple path -- a strongly connected
//   component need not contain a Hamiltonian path, so those services all fail but
//   they may not be orderable into one walk. For "how deep does this go", it is
//   the right number. For "print me a literal call chain", it is not, and no
//   polynomial algorithm gives you that one.
//
//   FINALLY, IS THIS THE NUMBER ANYONE WANTS? The blast radius (part 2) is what
//   pages people. The chain length is worst-case cascade DEPTH -- useful as
//   time-to-propagate, and as the thing to shorten when you go fix the
//   architecture. They are different questions and a long chain can have a tiny
//   radius. Say which one you are answering.
//
// Time:  O(log n) part 1; O(V + E) parts 2 and 3
// Space: O(V + E) for the reverse adjacency

namespace CodingPatterns.Graphs;

public static class ServiceFailureForensics
{
    /// <summary>No [Error] line anywhere in the log.</summary>
    public const int NoError = -1;

    // ==================================================== Part 1: binary search

    public static bool IsError(string line) =>
        line is not null && line.StartsWith("[Error]", StringComparison.Ordinal);

    public static bool IsWarn(string line) =>
        line is not null && line.StartsWith("[Warn]", StringComparison.Ordinal);

    /// <summary>
    /// Index of the first [Error] line, or <see cref="NoError"/>.
    ///
    /// Lower-bound form: the loop maintains "everything below lo is not an error,
    /// everything at or above hi is", so lo == hi is the boundary and needs no
    /// separate best-so-far variable. hi starts at Count, and lo landing there
    /// means the predicate is false everywhere -- that is the -1 case, not a bug.
    /// </summary>
    public static int FirstErrorIndex(IReadOnlyList<string> logs)
    {
        if (logs is null || logs.Count == 0)
            return NoError;

        int lo = 0, hi = logs.Count;

        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;           // never (lo + hi) / 2

            if (IsError(logs[mid]))
                hi = mid;                           // this one is an error; look left
            else
                lo = mid + 1;                       // and so is nothing at or below mid
        }

        return lo == logs.Count ? NoError : lo;
    }

    /// <summary>The O(n) oracle. Also the right answer when the ordering is only "mostly" true.</summary>
    public static int FirstErrorIndexScan(IReadOnlyList<string> logs)
    {
        for (int i = 0; logs is not null && i < logs.Count; i++)
            if (IsError(logs[i]))
                return i;

        return NoError;
    }

    /// <summary>
    /// Does this log actually obey the contract? Two clauses, and only the first
    /// one matters to the search:
    ///
    ///   * errors form a suffix -- if this is false, binary search is meaningless
    ///     and every answer it gives is arbitrary;
    ///   * the line before the first error is a [Warn] -- decoration for the
    ///     search, but a free assertion on the input, and a cheap way to catch a
    ///     truncated or interleaved log.
    /// </summary>
    public static bool IsWellFormed(IReadOnlyList<string> logs, out string why)
    {
        why = null;
        if (logs is null)
            return true;

        bool seenError = false;

        for (int i = 0; i < logs.Count; i++)
        {
            bool error = IsError(logs[i]);

            if (seenError && !error)
            {
                why = $"line {i} is not an [Error] but line {i - 1} was: errors are not a suffix";
                return false;
            }

            if (error && !seenError && i > 0 && !IsWarn(logs[i - 1]))
            {
                why = $"first error at {i} is not preceded by a [Warn]";
                return false;
            }

            seenError |= error;
        }

        return true;
    }

    /// <summary>A page of the log, or null once the index is past the end.</summary>
    public delegate string LogReader(int index);

    /// <summary>
    /// The same answer against a log service that will not tell you how long the
    /// log is -- so there is no midpoint to compute and the loop above cannot
    /// start. Gallop first (1, 2, 4, 8, ...) until a read comes back "error or
    /// past the end", which brackets the answer, then binary search the bracket.
    ///
    /// The predicate that is monotone here is `error OR past-the-end`: both halves
    /// are suffixes, so their union is one too. Reads is the number that matters
    /// -- O(log k) in the ANSWER, not in the log size, which is the whole point
    /// when the log has a billion lines and the outage started at line 40.
    /// </summary>
    public static (int Index, int Reads) FirstErrorPaged(LogReader read)
    {
        ArgumentNullException.ThrowIfNull(read);

        int reads = 0;

        bool ErrorOrEnd(int i)
        {
            reads++;
            string line = read(i);
            return line is null || IsError(line);
        }

        // lo is known-false, hi is known-true. lo = -1 is the virtual "before the
        // log", which is false by construction and saves special-casing index 0.
        int lo = -1, hi = 0;
        while (!ErrorOrEnd(hi))
        {
            lo = hi;
            hi = hi == 0 ? 1 : hi * 2;
        }

        while (hi - lo > 1)
        {
            int mid = lo + (hi - lo) / 2;
            if (ErrorOrEnd(mid))
                hi = mid;
            else
                lo = mid;
        }

        reads++;
        return (read(hi) is null ? NoError : hi, reads);      // "true" can mean past-the-end
    }

    // ================================================ Part 2: who else goes down

    /// <summary>
    /// callee -> everyone who calls it. Every service mentioned anywhere is a key,
    /// including the leaves that only ever appear as callees.
    ///
    /// Parallel edges are kept rather than deduped: they cost a wasted visited
    /// check, and the in-degree counting in <see cref="LongestErrorChain"/>
    /// decrements from the same lists, so the two stay consistent. Dedupe here
    /// only if the caller list is untrusted and can be enormous.
    /// </summary>
    public static Dictionary<string, List<string>> BuildReverseGraph(
        IReadOnlyDictionary<string, IEnumerable<string>> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);

        var reverse = new Dictionary<string, List<string>>();

        foreach (var (caller, callees) in calls)
        {
            if (!reverse.ContainsKey(caller))
                reverse[caller] = new List<string>();

            foreach (string callee in callees ?? Enumerable.Empty<string>())
            {
                if (!reverse.TryGetValue(callee, out var callers))
                    reverse[callee] = callers = new List<string>();

                callers.Add(caller);
            }
        }

        return reverse;
    }

    /// <summary>
    /// Every service that fails when <paramref name="firstErrorService"/> does,
    /// itself included. A service not present in the graph at all impacts only
    /// itself -- that is a decision, and the alternative (throw on an unknown
    /// service name) is just as defensible; pick one and say it.
    /// </summary>
    public static HashSet<string> ImpactedServices(
        IReadOnlyDictionary<string, IEnumerable<string>> calls, string firstErrorService) =>
        new(ImpactRadius(calls, firstErrorService).Keys);

    /// <summary>
    /// The same set, mapped to how many calls away from the origin each service
    /// is: 0 for the origin, 1 for its direct callers, and so on.
    ///
    /// Multi-source, because outages are rarely one service: seed the queue with
    /// every failed service at distance 0 and one pass gives the distance to the
    /// NEAREST cause, no repetition per origin.
    /// </summary>
    public static Dictionary<string, int> ImpactRadius(
        IReadOnlyDictionary<string, IEnumerable<string>> calls, params string[] origins)
    {
        var reverse = BuildReverseGraph(calls);
        var hops = new Dictionary<string, int>();
        var frontier = new Queue<string>();

        foreach (string origin in origins ?? Array.Empty<string>())
            if (hops.TryAdd(origin, 0))
                frontier.Enqueue(origin);

        while (frontier.Count > 0)
        {
            string service = frontier.Dequeue();
            if (!reverse.TryGetValue(service, out var callers))
                continue;                                     // nobody calls it

            foreach (string caller in callers)
                if (hops.TryAdd(caller, hops[service] + 1))    // check and mark in one step
                    frontier.Enqueue(caller);
        }

        return hops;
    }

    /// <summary>
    /// The version that survives contact with a real architecture.
    ///
    /// A service is described by its dependency GROUPS: each group is a set of
    /// interchangeable backends, and the service fails when any single group is
    /// entirely down. Reachability is exactly the case where every group has one
    /// member, so this strictly generalizes <see cref="ImpactedServices"/> -- and
    /// it is no longer a traversal, because "did this group go down" cannot be
    /// answered until every member has been decided.
    ///
    /// Evaluated as a LEAST FIXPOINT: start with the known-failed services and
    /// keep marking until a round changes nothing. That is what makes it correct
    /// under cycles -- the least fixpoint refuses to conclude "A is down because B
    /// is down because A is down", which a transitive-closure argument would
    /// happily assert. Monotone, so it terminates in at most V rounds; the tidy
    /// version is a worklist with a per-group counter of surviving members, which
    /// is O(V + total group size) and worth mentioning if asked for the cost.
    /// </summary>
    public static HashSet<string> ImpactedWithRedundancy(
        IReadOnlyDictionary<string, IEnumerable<string[]>> dependencyGroups,
        params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(dependencyGroups);

        var failed = new HashSet<string>(origins ?? Array.Empty<string>());

        for (bool changed = true; changed;)
        {
            changed = false;

            foreach (var (service, groups) in dependencyGroups)
            {
                if (failed.Contains(service))
                    continue;

                foreach (string[] group in groups ?? Enumerable.Empty<string[]>())
                {
                    // An empty group is "depends on nothing", not "all members are
                    // down" -- vacuous truth here would fail every service at once.
                    if (group.Length == 0 || !group.All(failed.Contains))
                        continue;

                    failed.Add(service);
                    changed = true;
                    break;
                }
            }
        }

        return failed;
    }

    // ============================================== Part 3: the deepest cascade

    /// <summary>
    /// One longest chain of failures starting at <paramref name="firstErrorService"/>,
    /// origin first. Throws on a dependency cycle -- see
    /// <see cref="LongestChainOverCycles"/> for the graph that has one.
    ///
    /// Kahn over the impacted subgraph, which does four jobs in one pass:
    /// topological order, the relaxation, cycle detection, and the seeding (the
    /// origin is the only impacted service with no impacted dependency, because
    /// every other one was reached THROUGH such a dependency).
    ///
    /// The memo is an int and a pointer, never a list: the path is rebuilt once,
    /// at the end, from the winner backwards. That is the difference between
    /// O(V + E) and the O(V * E) the list-concatenating version actually costs.
    /// </summary>
    public static List<string> LongestErrorChain(
        IReadOnlyDictionary<string, IEnumerable<string>> calls, string firstErrorService)
    {
        var reverse = BuildReverseGraph(calls);
        var impacted = new HashSet<string>(ImpactRadius(calls, firstErrorService).Keys);

        // In-degree inside the impacted subgraph. A self-call is skipped on
        // purpose: a service calling itself is recursion inside one process, not a
        // dependency cycle, and counting it would make every such node unreachable
        // by Kahn and report a cycle that is not there.
        var pending = impacted.ToDictionary(s => s, _ => 0);

        foreach (string service in impacted)
            foreach (string caller in Callers(reverse, service))
                if (caller != service && pending.ContainsKey(caller))
                    pending[caller]++;

        var depth = impacted.ToDictionary(s => s, s => s == firstErrorService ? 1 : 0);
        var previous = new Dictionary<string, string>();
        var ready = new Queue<string>();

        foreach (var (service, count) in pending)
            if (count == 0)
                ready.Enqueue(service);                       // the origin, and only it

        int settled = 0;
        string deepest = firstErrorService;

        while (ready.Count > 0)
        {
            string service = ready.Dequeue();
            settled++;

            if (depth[service] > depth[deepest])
                deepest = service;

            foreach (string caller in Callers(reverse, service))
            {
                if (caller == service || !pending.ContainsKey(caller))
                    continue;

                if (depth[service] + 1 > depth[caller])
                {
                    depth[caller] = depth[service] + 1;
                    previous[caller] = service;               // the pointer, not the path
                }

                if (--pending[caller] == 0)
                    ready.Enqueue(caller);
            }
        }

        if (settled != impacted.Count)
            throw new InvalidOperationException(
                "dependency cycle among the impacted services: longest simple path is NP-hard, " +
                "condense the SCCs first (see LongestChainOverCycles)");

        var chain = new List<string>();
        for (string at = deepest; at is not null; at = previous.GetValueOrDefault(at))
            chain.Add(at);

        chain.Reverse();                                      // walked deepest -> origin
        return chain;
    }

    private static List<string> Callers(Dictionary<string, List<string>> reverse, string service) =>
        reverse.TryGetValue(service, out var callers) ? callers : EmptyCallers;

    private static readonly List<string> EmptyCallers = new();

    /// <summary>
    /// The write-up's memoized DFS, kept runnable because it is the version people
    /// write and it is not wrong -- it is just not the complexity it advertises.
    /// <paramref name="cellsCopied"/> counts entries copied by the list
    /// concatenation, which is the hidden O(V * E): on a graph of n nodes whose
    /// reverse edges form a transitive tournament it grows like n^3/6 while
    /// <see cref="LongestErrorChain"/> stays at V + E.
    ///
    /// Recursive, so it also dies at ~10^4 of depth. Both problems have the same
    /// cause -- treating the PATH as the memoized value instead of its length.
    /// </summary>
    public static List<string> LongestErrorChainMemoized(
        IReadOnlyDictionary<string, IEnumerable<string>> calls,
        string firstErrorService,
        out long cellsCopied)
    {
        var reverse = BuildReverseGraph(calls);
        var memo = new Dictionary<string, List<string>>();
        var visiting = new HashSet<string>();
        long copied = 0;

        List<string> Dfs(string service)
        {
            if (visiting.Contains(service))
                throw new InvalidOperationException("dependency cycle");
            if (memo.TryGetValue(service, out var done))
                return done;

            visiting.Add(service);
            var best = new List<string> { service };

            foreach (string caller in Callers(reverse, service))
            {
                if (caller == service)
                    continue;

                var tail = Dfs(caller);
                copied += tail.Count + 1;                     // the concatenation, priced

                if (tail.Count + 1 > best.Count)
                {
                    best = new List<string>(tail.Count + 1) { service };
                    best.AddRange(tail);
                }
            }

            visiting.Remove(service);
            memo[service] = best;
            return best;
        }

        var chain = Dfs(firstErrorService);
        cellsCopied = copied;
        return chain;
    }

    /// <summary>
    /// The deepest cascade when the dependency graph HAS cycles, as one does.
    ///
    /// Returns the chain as a list of LEVELS: every service inside a level is
    /// strongly connected to the others, so they all fail the moment any of them
    /// does, and the level counts as its full size. The condensation of an SCC
    /// decomposition is a DAG, so the same relaxation as above runs on it.
    ///
    /// What this number is: the cascade's depth in services, and an upper bound on
    /// the longest simple path. What it is NOT: a literal call chain. A strongly
    /// connected component need not contain a Hamiltonian path, so its members can
    /// all be doomed without being orderable into a single walk -- and finding the
    /// true longest simple path is NP-hard, so that is not an oversight to fix.
    ///
    /// Kosaraju, iteratively both times, on the impacted subgraph only.
    /// </summary>
    public static List<List<string>> LongestChainOverCycles(
        IReadOnlyDictionary<string, IEnumerable<string>> calls, string firstErrorService)
    {
        var reverse = BuildReverseGraph(calls);
        var impacted = new HashSet<string>(ImpactRadius(calls, firstErrorService).Keys);

        // Blame graph restricted to the impacted set, and its transpose. Indexed,
        // because from here on everything is arrays.
        var nodes = impacted.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var id = nodes.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);
        int n = nodes.Count;

        var forward = new List<int>[n];                       // failure propagates along these
        var backward = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            forward[i] = new List<int>();
            backward[i] = new List<int>();
        }

        for (int u = 0; u < n; u++)
            foreach (string caller in Callers(reverse, nodes[u]))
                if (id.TryGetValue(caller, out int v))
                {
                    forward[u].Add(v);
                    backward[v].Add(u);
                }

        var order = FinishOrder(forward, n);                  // pass 1: finishing times
        var component = new int[n];
        Array.Fill(component, -1);
        int components = 0;

        for (int k = n - 1; k >= 0; k--)                      // pass 2: reverse finish order
        {
            int root = order[k];
            if (component[root] != -1)
                continue;

            var stack = new Stack<int>();
            stack.Push(root);
            component[root] = components;

            while (stack.Count > 0)
                foreach (int prev in backward[stack.Pop()])
                    if (component[prev] == -1)
                    {
                        component[prev] = components;
                        stack.Push(prev);
                    }

            components++;
        }

        var members = new List<string>[components];
        for (int c = 0; c < components; c++)
            members[c] = new List<string>();
        for (int u = 0; u < n; u++)
            members[component[u]].Add(nodes[u]);

        // Condensation: deduped, so the in-degrees Kahn decrements are the ones it
        // counted. Parallel edges between two components are common here -- every
        // pair of services that call each other across a boundary makes one.
        var condensed = new HashSet<int>[components];
        var indegree = new int[components];
        for (int c = 0; c < components; c++)
            condensed[c] = new HashSet<int>();

        for (int u = 0; u < n; u++)
            foreach (int v in forward[u])
                if (component[u] != component[v] && condensed[component[u]].Add(component[v]))
                    indegree[component[v]]++;

        // Longest path weighted by component size, from the origin's component --
        // which is again the unique source, for the same reason as before.
        int origin = component[id[firstErrorService]];
        var weight = members.Select(m => m.Count).ToArray();
        var best = new int[components];
        var previous = new int[components];
        Array.Fill(previous, -1);
        best[origin] = weight[origin];

        var ready = new Queue<int>();
        for (int c = 0; c < components; c++)
            if (indegree[c] == 0)
                ready.Enqueue(c);

        int deepest = origin;

        while (ready.Count > 0)
        {
            int c = ready.Dequeue();
            if (best[c] > best[deepest])
                deepest = c;

            foreach (int next in condensed[c])
            {
                if (best[c] > 0 && best[c] + weight[next] > best[next])
                {
                    best[next] = best[c] + weight[next];
                    previous[next] = c;
                }

                if (--indegree[next] == 0)
                    ready.Enqueue(next);
            }
        }

        var chain = new List<List<string>>();
        for (int at = deepest; at != -1; at = previous[at])
            chain.Add(members[at]);

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Iterative DFS finishing order: node pushed once its whole subtree is done.
    /// Iterative because "the dependency chain is 10^5 long" is the case this file
    /// keeps coming back to. The frame stack holds (node, next child index), so it
    /// is O(depth) rather than the O(width) a node stack would cost.
    /// </summary>
    private static int[] FinishOrder(List<int>[] adjacency, int n)
    {
        var order = new List<int>(n);
        var seen = new bool[n];
        var stack = new Stack<(int Node, int Next)>();

        for (int start = 0; start < n; start++)
        {
            if (seen[start])
                continue;

            seen[start] = true;
            stack.Push((start, 0));

            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();

                if (next == adjacency[node].Count)
                {
                    order.Add(node);                          // all children finished
                    continue;
                }

                stack.Push((node, next + 1));                 // resume here afterwards
                int child = adjacency[node][next];

                if (!seen[child])
                {
                    seen[child] = true;
                    stack.Push((child, 0));
                }
            }
        }

        return order.ToArray();
    }

    // ==================================================================== tests

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

    private static Dictionary<string, IEnumerable<string>> Example() => new()
    {
        ["A"] = new[] { "B", "C" },
        ["B"] = new[] { "D" },
        ["C"] = new[] { "D" },
        ["E"] = new[] { "A" },
        ["F"] = new[] { "C" },
    };

    public static void Main()
    {
        _checks = _failures = 0;

        Console.WriteLine("== Part 1: the interview's own example ==");

        var logs = new[]
        {
            "[Info] boot",
            "[Info] warmup",
            "[Warn] timeout retries high",
            "[Error] downstream unavailable",
            "[Error] service unhealthy",
        };

        Console.WriteLine($"  FirstErrorIndex = {FirstErrorIndex(logs)} (expect 3)");
        Check(FirstErrorIndex(logs) == 3, "the example's first error is at index 3");
        Check(IsWellFormed(logs, out _), "the example log obeys its own contract");

        Console.WriteLine();
        Console.WriteLine("== Part 1: edge cases ==");

        Check(FirstErrorIndex(Array.Empty<string>()) == NoError, "empty log -> -1");
        Check(FirstErrorIndex(null) == NoError, "null log -> -1");
        Check(FirstErrorIndex(new[] { "[Info] a", "[Warn] b" }) == NoError, "no errors -> -1");
        Check(FirstErrorIndex(new[] { "[Error] a" }) == 0, "an error at index 0");
        Check(FirstErrorIndex(new[] { "[Error] a", "[Error] b" }) == 0, "all errors -> 0");
        Check(FirstErrorIndex(new[] { "[Info] a" }) == NoError, "single clean line -> -1");

        // The trap: warns are NOT a suffix, so searching for one is not a search.
        var warnTrap = new[] { "[Warn] a", "[Info] b", "[Warn] c", "[Info] d" };
        Check(FirstErrorIndex(warnTrap) == NoError, "warns scattered through a clean log -> still -1");
        Check(IsWellFormed(warnTrap, out _), "a log full of warns and no errors is well-formed");
        Console.WriteLine("  [Warn] can appear anywhere in an error-free log, so 'find the last");
        Console.WriteLine("  warn and add one' is not an algorithm -- only [Error] is monotone.");

        Check(!IsWellFormed(new[] { "[Error] a", "[Info] b" }, out string why1), "errors must be a suffix");
        Console.WriteLine($"  rejected: {why1}");
        Check(!IsWellFormed(new[] { "[Info] a", "[Error] b" }, out string why2), "first error needs a [Warn] before it");
        Console.WriteLine($"  rejected: {why2}");

        Console.WriteLine();
        Console.WriteLine("== Part 1: binary vs linear on 400 random well-formed logs ==");

        var rng = new Random(17);
        bool agree = true;

        for (int trial = 0; trial < 400; trial++)
        {
            int n = rng.Next(0, 60);
            int firstError = rng.Next(0, n + 2);              // n+1 => no errors at all
            var built = new List<string>();

            for (int i = 0; i < n; i++)
            {
                if (i >= firstError)
                    built.Add("[Error] down");
                else if (i == firstError - 1)
                    built.Add("[Warn] elevated");             // the contract's own rule
                else
                    built.Add(rng.Next(2) == 0 ? "[Info] ok" : "[Warn] slow");
            }

            int want = FirstErrorIndexScan(built);
            agree &= FirstErrorIndex(built) == want;
            agree &= IsWellFormed(built, out _);

            // Same log behind a paged reader that will not reveal its length.
            var (pagedIndex, reads) = FirstErrorPaged(i => i < built.Count ? built[i] : null);
            agree &= pagedIndex == want;
            agree &= reads <= 4 * (int)Math.Log2(built.Count + 2) + 8;
        }

        Check(agree, "400 random logs: binary, linear and paged all agree");

        var huge = new List<string>();
        for (int i = 0; i < 1_000_000; i++)
            huge.Add(i == 39 ? "[Warn] elevated" : i >= 40 ? "[Error] down" : "[Info] ok");

        var (idx, pagedReads) = FirstErrorPaged(i => i < huge.Count ? huge[i] : null);
        Console.WriteLine($"  1,000,000 lines, error at {idx}: {pagedReads} reads with an UNKNOWN length");
        Check(idx == 40 && pagedReads < 20, "galloping is O(log k) in the answer, not in the log size");

        Console.WriteLine();
        Console.WriteLine("== Part 2: blast radius ==");

        var calls = Example();
        var impacted = ImpactedServices(calls, "D");
        Console.WriteLine($"  D fails -> {{{string.Join(", ", impacted.OrderBy(s => s, StringComparer.Ordinal))}}}");
        Check(impacted.SetEquals(new[] { "A", "B", "C", "D", "E", "F" }), "D takes down everything");

        var radius = ImpactRadius(calls, "D");
        Console.WriteLine($"  hops: {string.Join(", ", radius.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))}");
        Check(radius["D"] == 0 && radius["B"] == 1 && radius["C"] == 1, "direct callers of D are one hop out");
        Check(radius["A"] == 2 && radius["F"] == 2 && radius["E"] == 3, "and the rest follow");

        // The direction check: A's own dependencies are the services that are fine.
        var fromA = ImpactedServices(calls, "A");
        Console.WriteLine($"  A fails  -> {{{string.Join(", ", fromA.OrderBy(s => s, StringComparer.Ordinal))}}}  (B, C, D are DOWNSTREAM and unaffected)");
        Check(fromA.SetEquals(new[] { "A", "E" }), "failure travels callee -> caller, never the other way");
        Check(!fromA.Contains("B") && !fromA.Contains("D"), "a failed service does not break the things it calls");

        Check(ImpactedServices(calls, "Ghost").SetEquals(new[] { "Ghost" }), "an unknown service impacts only itself");
        Check(ImpactRadius(calls, "B", "F").Count == 4, "multi-source: {B,F} plus A and E");
        Check(ImpactedServices(calls, "E").SetEquals(new[] { "E" }), "nothing calls E, so E is a leaf of the blame graph");

        // Cycles and self-calls must terminate.
        var cyclic = new Dictionary<string, IEnumerable<string>>
        {
            ["A"] = new[] { "B" },
            ["B"] = new[] { "C" },
            ["C"] = new[] { "A" },                            // A -> B -> C -> A
            ["S"] = new[] { "S", "B" },                       // self-call
        };
        Check(ImpactedServices(cyclic, "A").SetEquals(new[] { "A", "B", "C", "S" }), "a cyclic call graph terminates");
        Console.WriteLine("  cycle A->B->C->A plus a self-caller: terminates, everything down");

        Console.WriteLine();
        Console.WriteLine("== Part 2: what redundancy changes ==");

        // orders depends on (db-primary OR db-replica) and on auth.
        var groups = new Dictionary<string, IEnumerable<string[]>>
        {
            ["orders"] = new[] { new[] { "db-primary", "db-replica" }, new[] { "auth" } },
            ["web"] = new[] { new[] { "orders" } },
            ["auth"] = new[] { new[] { "db-primary", "db-replica" } },
        };

        var onePrimary = ImpactedWithRedundancy(groups, "db-primary");
        var bothDbs = ImpactedWithRedundancy(groups, "db-primary", "db-replica");

        Console.WriteLine($"  db-primary down       -> {{{string.Join(", ", onePrimary.OrderBy(s => s, StringComparer.Ordinal))}}}");
        Console.WriteLine($"  both replicas down    -> {{{string.Join(", ", bothDbs.OrderBy(s => s, StringComparer.Ordinal))}}}");
        Check(onePrimary.SetEquals(new[] { "db-primary" }), "one replica of two is survivable: nothing else falls");
        Check(bothDbs.SetEquals(new[] { "db-primary", "db-replica", "orders", "web", "auth" }), "lose both and the AND/OR cascade runs");

        // Reachability is the singleton-group special case -- assert it, do not claim it.
        var singleton = calls.ToDictionary(
            kv => kv.Key,
            kv => (IEnumerable<string[]>)kv.Value.Select(c => new[] { c }).ToList());
        Check(ImpactedWithRedundancy(singleton, "D").IsSupersetOf(new[] { "A", "B", "C", "E", "F" }),
            "with one backend per dependency, AND/OR degenerates to plain reachability");

        Console.WriteLine();
        Console.WriteLine("== Part 3: the longest cascade ==");

        var chain = LongestErrorChain(calls, "D");
        Console.WriteLine($"  {string.Join(" -> ", chain)}");
        Check(chain.Count == 4 && chain[0] == "D" && chain[^1] == "E", "D -> (B|C) -> A -> E is 4 deep");
        Check(ValidChain(calls, chain, "D"), "every step of the chain is a real call, backwards");

        Check(LongestErrorChain(calls, "E").SequenceEqual(new[] { "E" }), "a service nobody calls is a chain of one");
        Check(LongestErrorChain(calls, "Ghost").SequenceEqual(new[] { "Ghost" }), "an unknown service likewise");

        var selfCall = new Dictionary<string, IEnumerable<string>> { ["X"] = new[] { "X", "Y" } };
        Check(LongestErrorChain(selfCall, "Y").SequenceEqual(new[] { "Y", "X" }), "a self-call is recursion, not a cycle");

        try
        {
            LongestErrorChain(cyclic, "A");
            Check(false, "a real cycle should have been rejected");
        }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains("cycle"), "a real cycle is reported, not silently mis-answered");
            Console.WriteLine("  A->B->C->A: rejected by the DAG version, on purpose");
        }

        Console.WriteLine();
        Console.WriteLine("== Part 3: the memoized version's complexity claim, measured ==");

        // Reverse edges form a transitive tournament: node i is called by every
        // j > i. V = 200, E = 19,900, longest chain = 200.
        const int size = 200;
        var tournament = new Dictionary<string, IEnumerable<string>>();
        for (int j = 0; j < size; j++)
            tournament[$"s{j}"] = Enumerable.Range(0, j).Select(i => $"s{i}").ToList();

        var fast = LongestErrorChain(tournament, "s0");
        var slow = LongestErrorChainMemoized(tournament, "s0", out long copied);
        long edges = (long)size * (size - 1) / 2;

        Console.WriteLine($"  V = {size}, E = {edges}, longest chain = {fast.Count}");
        Console.WriteLine($"  memoized DFS copied {copied:N0} list cells; the Kahn loop touches ~{size + edges:N0}");
        Console.WriteLine($"  ratio {copied / (double)(size + edges):F0}x -- that is the O(V*E) hiding behind '[x] + dfs(y)'.");
        Check(fast.Count == size && slow.Count == size, "both find the 200-deep chain");
        Check(copied > 10L * (size + edges), "the concatenating version is measurably superlinear");

        Console.WriteLine();
        Console.WriteLine("== Part 3: cycles, via SCC condensation ==");

        //   payments <-> ledger  (a genuine two-service cycle)
        //   both are called by checkout, which is called by web
        var withCycle = new Dictionary<string, IEnumerable<string>>
        {
            ["payments"] = new[] { "ledger", "db" },
            ["ledger"] = new[] { "payments" },
            ["checkout"] = new[] { "payments" },
            ["web"] = new[] { "checkout" },
            ["mobile"] = new[] { "checkout" },
        };

        var levels = LongestChainOverCycles(withCycle, "db");
        Console.WriteLine("  db fails ->");
        foreach (var level in levels)
            Console.WriteLine($"    [{string.Join(", ", level.OrderBy(s => s, StringComparer.Ordinal))}]");

        int totalDown = levels.Sum(l => l.Count);
        Check(levels.Count == 4, "db -> {payments,ledger} -> checkout -> (web|mobile)");
        Check(levels[1].Count == 2, "payments and ledger are one strongly connected level");
        Check(totalDown == 5, "five services deep, counting both halves of the cycle");
        Console.WriteLine($"  cascade depth = {totalDown} services across {levels.Count} levels");
        Console.WriteLine("  The level of size 2 is why this is an UPPER BOUND on the longest simple");
        Console.WriteLine("  path, not the path itself -- an SCC need not contain a Hamiltonian walk.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        rng = new Random(2027);
        bool consistent = true;

        for (int trial = 0; trial < 300; trial++)
        {
            int n = rng.Next(1, 9);
            var names = Enumerable.Range(0, n).Select(i => $"n{i}").ToList();
            var acyclic = new Dictionary<string, IEnumerable<string>>();

            // Caller index < callee index keeps the CALL graph acyclic.
            foreach (var (name, i) in names.Select((s, i) => (s, i)))
                acyclic[name] = Enumerable.Range(i + 1, n - i - 1)
                    .Where(_ => rng.Next(3) == 0)
                    .Select(j => names[j])
                    .ToList();

            string origin = names[rng.Next(n)];

            // Blast radius against a transitive-closure oracle.
            var want = new HashSet<string>();
            for (int round = 0; round < n; round++)
                foreach (var (caller, callees) in acyclic)
                    if (callees.Any(c => c == origin || want.Contains(c)))
                        want.Add(caller);
            want.Add(origin);
            consistent &= ImpactedServices(acyclic, origin).SetEquals(want);

            // Longest chain against brute-forced simple paths.
            var got = LongestErrorChain(acyclic, origin);
            consistent &= ValidChain(acyclic, got, origin);
            consistent &= got.Count == BruteForceLongest(BuildReverseGraph(acyclic), origin);

            var memoized = LongestErrorChainMemoized(acyclic, origin, out _);
            consistent &= memoized.Count == got.Count;

            // On a DAG the SCC version must agree exactly: every level is a singleton.
            var condensedLevels = LongestChainOverCycles(acyclic, origin);
            consistent &= condensedLevels.All(l => l.Count == 1);
            consistent &= condensedLevels.Sum(l => l.Count) == got.Count;
        }

        Check(consistent, "300 random DAGs: BFS, Kahn, memoized DFS, SCC and brute force all agree");

        rng = new Random(99);
        bool cyclesHold = true;

        for (int trial = 0; trial < 300; trial++)
        {
            int n = rng.Next(1, 8);
            var names = Enumerable.Range(0, n).Select(i => $"c{i}").ToList();
            var any = names.ToDictionary(
                s => s,
                _ => (IEnumerable<string>)names.Where(_ => rng.Next(4) == 0).ToList());

            string origin = names[rng.Next(n)];
            var impactedSet = ImpactedServices(any, origin);
            var levels2 = LongestChainOverCycles(any, origin);

            // Every level is a real SCC of the impacted subgraph, the levels are
            // disjoint, and consecutive levels are genuinely connected.
            cyclesHold &= levels2.SelectMany(l => l).Distinct().Count() == levels2.Sum(l => l.Count);
            cyclesHold &= levels2.SelectMany(l => l).All(impactedSet.Contains);
            cyclesHold &= levels2[0].Contains(origin);

            for (int i = 0; i + 1 < levels2.Count; i++)
                cyclesHold &= levels2[i + 1].Any(caller =>
                    any[caller].Any(callee => levels2[i].Contains(callee)));

            foreach (var level in levels2)
                cyclesHold &= level.All(a => level.All(b => Reaches(any, a, b) && Reaches(any, b, a)));

            // And no level can be split: two services in one level are mutually
            // reachable, two in different levels are not.
            for (int i = 0; i < levels2.Count; i++)
                for (int j = i + 1; j < levels2.Count; j++)
                    cyclesHold &= !levels2[i].Any(a => levels2[j].Any(b => Reaches(any, a, b) && Reaches(any, b, a)));
        }

        Check(cyclesHold, "300 random cyclic graphs: the levels are exactly the SCCs, in a valid order");

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"All {_checks} checks passed."
            : $"{_failures} of {_checks} checks FAILED.");
    }

    /// <summary>Every consecutive pair must be a real "later calls earlier" edge, and no repeats.</summary>
    private static bool ValidChain(
        IReadOnlyDictionary<string, IEnumerable<string>> calls, List<string> chain, string origin)
    {
        if (chain.Count == 0 || chain[0] != origin || chain.Distinct().Count() != chain.Count)
            return false;

        for (int i = 0; i + 1 < chain.Count; i++)
            if (!calls.TryGetValue(chain[i + 1], out var callees) || !callees.Contains(chain[i]))
                return false;

        return true;
    }

    /// <summary>Longest simple path from the origin, by exhaustion. Tiny graphs only -- that is the point.</summary>
    private static int BruteForceLongest(Dictionary<string, List<string>> reverse, string origin)
    {
        var onPath = new HashSet<string>();

        int Walk(string service)
        {
            onPath.Add(service);
            int best = 1;

            foreach (string caller in Callers(reverse, service))
                if (!onPath.Contains(caller))
                    best = Math.Max(best, 1 + Walk(caller));

            onPath.Remove(service);
            return best;
        }

        return Walk(origin);
    }

    private static bool Reaches(IReadOnlyDictionary<string, IEnumerable<string>> calls, string from, string to)
    {
        // "from fails => to fails", i.e. `to` reaches `from` through call edges.
        var seen = new HashSet<string> { from };
        var stack = new Stack<string>(new[] { from });
        var reverse = BuildReverseGraph(calls);

        while (stack.Count > 0)
            foreach (string caller in Callers(reverse, stack.Pop()))
                if (seen.Add(caller))
                {
                    if (caller == to)
                        return true;
                    stack.Push(caller);
                }

        return from == to || seen.Contains(to);
    }
}

// ---- Notes for the follow-up questions ----
//
// "The logs are in S3 / behind a query API, not in memory."
//     Then the cost model is FETCHES, and that is the only reason binary search
//     beats grep here. FirstErrorPaged is the shape: gallop to bracket the answer
//     without knowing the length, then bisect. If reads are chunked, fetch a whole
//     block per probe and scan it locally -- the log(n) is in round trips, and one
//     round trip already pays for a megabyte of scanning.
//
// "The logs come from twelve hosts."
//     The suffix property dies immediately: per-host clocks skew by seconds and
//     the merged stream interleaves. Binary search per host still works (each
//     host's own stream is ordered), then take the earliest of the twelve answers
//     -- and be explicit that "earliest" now means a wall-clock comparison across
//     skewed clocks, which is a data question, not an algorithm question.
//
// "The call graph has 10^6 services and it does not fit."
//     The BFS is fine; the reverse adjacency is what costs. Build it once and keep
//     it (it changes on deploys, not on outages), intern the service names to ints
//     so the visited set is a bitset, and if it still does not fit, shard by
//     hash(service) and ship discovered callers to their owning shard -- the same
//     partitioned-crawl argument as any distributed traversal.
//
// "Rank the services by how bad it would be if they died."
//     That is |ImpactedServices| for every service, which is V reverse-BFS runs at
//     O(V*(V+E)). On a DAG you can do better in one topological sweep with a
//     reachability BITSET per node, unioning callers' sets: O(V*E/64) and it fits
//     for V in the tens of thousands. Past that it is an estimator's job
//     (sampling, or a sketch), not an exact one.
//
// "Rank them by blast radius weighted by traffic."
//     Same traversal, different accumulator -- sum request rates over the impacted
//     set instead of counting it. Worth noticing that this reorders the list
//     completely: the service with the biggest fan-in is usually not the one whose
//     death costs the most requests.
//
// "Which failure is the root cause when three services alert at once?"
//     Not this algorithm. Reachability tells you what COULD have propagated; it
//     cannot distinguish a cause from a symptom, and every service on the chain
//     alerts. The usable signal is the ORDER the alerts fired in, intersected with
//     the reverse-reachable set -- i.e. part 1 and part 2 together, which is the
//     reason this problem has both halves.
//
// "How do you test the graph half?"
//     Properties, not examples. The impacted set must be closed under "calls an
//     impacted service"; the chain must consist of real edges, distinct services,
//     and start at the origin; on a DAG the SCC version's levels must all be
//     singletons and its total must equal the plain chain length. All four are
//     checked above against brute force on graphs small enough to enumerate --
//     which is exactly where a direction-reversal bug shows up, and where a
//     hand-written example will not.
