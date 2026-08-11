// DAG Allow / Disallow Propagation  (no LeetCode number; permissions-flavoured)
// Difficulty: Medium (Hard once the interviewer starts moving the semantics)
// Pattern: Kahn topological sort + per-node bitmask DP over ancestors
//
// Every node carries an ALLOW set and a DISALLOW set of letters. Both propagate
// transitively down the edges, and each node's answer is
//
//     effective(v) = (allow of v and all its ancestors)
//                  - (disallow of v and all its ancestors)
//
// with disallow winning any tie -- "once disallowed, always disallowed".
//
// THE ONE INSIGHT. Two masks per node, never one:
//
//     allow[v]    = own_allow    | OR over parents of allow[p]
//     disallow[v] = own_disallow | OR over parents of disallow[p]
//     effective   = allow & ~disallow            <- computed, never propagated
//
// The tempting shortcut is to push the ANSWER down instead: take the union of
// the parents' effective sets, add own allow, remove own disallow. That is
// wrong, and it is wrong in a way that passes every test where no descendant
// re-allows anything. Subtracting early THROWS AWAY the fact that a letter was
// forbidden upstream; a child that lists the letter in its own allow set then
// resurrects it. Keep the disallow mask alive all the way down and the
// resurrection cannot happen. `EffectiveNaive` below implements the shortcut on
// purpose so the demo can show the two answers diverging on a 3-node chain.
//
// WHY DISALLOW WINS, if the interviewer pushes on it. In a DAG a node can have
// two parents that disagree, and unlike a tree there is no "nearer" one to break
// the tie -- both can sit at the same distance. So "closest ancestor wins" is not
// even well defined here without an extra rule. Disallow-wins is the only
// resolution that is order-independent, and it makes the recurrence MONOTONE
// (disallow sets only ever grow along a path), which is what makes a single
// topological pass -- and per-node caching -- provably correct. It is also the
// safe default for a permission system: a deny anywhere on any path denies.
//
// The pieces and their costs, sigma = 26 letters packed into one int:
//
//   TopologicalOrder      Kahn's BFS                      O(V + E)
//   Propagate             one pass, two ints per node     O(V + E)
//   EffectiveMemoized     DFS + memo, same answer         O(V + E)
//   InheritPrivileges     allow-only framing, hash sets   O((V + E) * sigma)
//   AncestorClosureBrute  reachability per node, testing  O(V * (V + E))
//
// EDGE DIRECTION, stated once and never assumed again: edges are [parent, child]
// and a letter flows parent -> child. Half of the failed versions of this
// question are a correct algorithm run on a reversed graph.

namespace CodingPatterns.Graphs;

public static class AllowDisallowPropagation
{
    /// <summary>Letters are 'a'..'z', so a whole set fits in the low 26 bits of an int.</summary>
    public const int Alphabet = 26;

    // ------------------------------------------------------------ mask helpers

    /// <summary>
    /// "abc" -> 0b111. Null or empty is the empty set, which is the right answer
    /// for a node that annotates neither list. Duplicates are harmless -- OR-ing
    /// the same bit twice is still that bit, which is exactly why sets and
    /// bitmasks get along.
    /// </summary>
    public static int MaskOf(string letters)
    {
        int mask = 0;
        foreach (char c in letters ?? string.Empty)
        {
            if (c < 'a' || c > 'z')
                throw new ArgumentException($"'{c}' is not a lowercase letter", nameof(letters));
            mask |= 1 << (c - 'a');
        }
        return mask;
    }

    /// <summary>0b111 -> "abc", always in alphabetical order so results compare cleanly.</summary>
    public static string LettersOf(int mask)
    {
        var buffer = new char[Alphabet];
        int count = 0;
        for (int i = 0; i < Alphabet; i++)
            if ((mask & (1 << i)) != 0)
                buffer[count++] = (char)('a' + i);
        return new string(buffer, 0, count);
    }

    // ------------------------------------------------------------- graph build

    /// <summary>
    /// Child adjacency plus in-degree, from [parent, child] pairs. Duplicate
    /// edges are left alone: they bump the in-degree twice and are decremented
    /// twice, so Kahn stays correct, and OR is idempotent so the masks do not
    /// care either. A self-loop is left alone too -- it is a cycle, and cycles
    /// must be REPORTED rather than quietly dropped.
    /// </summary>
    private static (List<int>[] Children, int[] InDegree) Build(int n, int[][] edges)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n), "node count cannot be negative");

        var children = new List<int>[n];
        for (int i = 0; i < n; i++)
            children[i] = new List<int>();

        var inDegree = new int[n];

        foreach (var edge in edges ?? Array.Empty<int[]>())
        {
            if (edge is null || edge.Length != 2)
                throw new ArgumentException("every edge must be a [parent, child] pair", nameof(edges));

            int parent = edge[0], child = edge[1];
            if (parent < 0 || parent >= n || child < 0 || child >= n)
                throw new ArgumentOutOfRangeException(
                    nameof(edges), $"edge [{parent}, {child}] refers to a node outside 0..{n - 1}");

            children[parent].Add(child);
            inDegree[child]++;
        }

        return (children, inDegree);
    }

    /// <summary>
    /// Kahn. Returns an order in which every parent precedes every child, or
    /// null when a cycle makes that impossible. Nodes with no parents are the
    /// seeds; isolated nodes are seeds too and simply pass straight through.
    /// </summary>
    public static int[] TopologicalOrder(int n, int[][] edges)
    {
        var (children, inDegree) = Build(n, edges);

        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        var order = new int[n];
        int placed = 0;

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            order[placed++] = node;

            foreach (int child in children[node])
                if (--inDegree[child] == 0)
                    queue.Enqueue(child);
        }

        return placed == n ? order : null;
    }

    // --------------------------------------------------------- the propagation

    /// <summary>
    /// The whole algorithm. Walks a topological order and OR-s each node's two
    /// masks into every child, so by the time a node is popped every ancestor has
    /// already contributed -- that is the entire correctness argument, and it is
    /// why Kahn and the DP fuse into one loop.
    ///
    /// Pushing to children (rather than pulling from parents) means the reverse
    /// adjacency is never built, and it makes duplicate edges free.
    ///
    /// Returns false on a cycle instead of throwing, so a caller that expects
    /// possibly-cyclic input can react. <paramref name="allowMask"/> and
    /// <paramref name="disallowMask"/> are the ACCUMULATED sets, not the
    /// per-node annotations; combine them with &amp; ~ for the answer.
    /// </summary>
    public static bool TryPropagate(
        int n, int[][] edges, string[] allow, string[] disallow,
        out int[] allowMask, out int[] disallowMask)
    {
        Validate(n, allow, disallow);

        var (children, inDegree) = Build(n, edges);

        allowMask = new int[n];
        disallowMask = new int[n];
        for (int i = 0; i < n; i++)
        {
            allowMask[i] = MaskOf(allow[i]);
            disallowMask[i] = MaskOf(disallow[i]);
        }

        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        int settled = 0;
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            settled++;

            foreach (int child in children[node])
            {
                // `node` is final: every one of ITS ancestors is already folded in.
                allowMask[child] |= allowMask[node];
                disallowMask[child] |= disallowMask[node];

                if (--inDegree[child] == 0)
                    queue.Enqueue(child);
            }
        }

        if (settled == n)
            return true;

        allowMask = null;
        disallowMask = null;
        return false;
    }

    /// <summary>
    /// Per-node effective mask. Throws on a cycle, because "propagate down a DAG"
    /// has no meaning on one -- a cycle's members would each be their own
    /// ancestor. Ask up front whether the graph is guaranteed acyclic; if it is
    /// not, the honest answer is the SCC condensation (every node in an SCC ends
    /// up with the same masks), not a silent wrong number.
    /// </summary>
    public static int[] EffectiveMasks(int n, int[][] edges, string[] allow, string[] disallow)
    {
        if (!TryPropagate(n, edges, allow, disallow, out var allowMask, out var disallowMask))
            throw new ArgumentException("graph has a cycle; allow/disallow only propagate on a DAG", nameof(edges));

        var effective = new int[n];
        for (int i = 0; i < n; i++)
            effective[i] = allowMask[i] & ~disallowMask[i];   // disallow wins, always
        return effective;
    }

    /// <summary>Same answer as <see cref="EffectiveMasks"/>, spelled as sorted letter strings.</summary>
    public static string[] Effective(int n, int[][] edges, string[] allow, string[] disallow)
    {
        var masks = EffectiveMasks(n, edges, allow, disallow);
        var result = new string[n];
        for (int i = 0; i < n; i++)
            result[i] = LettersOf(masks[i]);
        return result;
    }

    /// <summary>
    /// The same DP written top-down: ask each node for its accumulated masks, and
    /// it asks its parents. Worth having in the back pocket because interviewers
    /// often ask "can you avoid the explicit topological sort?" -- and because
    /// caching is only sound thanks to the monotonicity noted at the top: a
    /// node's masks depend on its ancestors alone, never on which query reached
    /// it, so one memo entry per node is always valid.
    ///
    /// Needs the parent lists, and needs its own cycle check (grey = on the
    /// stack). Recursive for readability; a 10^5-node chain would want the
    /// iterative Kahn version above.
    /// </summary>
    public static string[] EffectiveMemoized(int n, int[][] edges, string[] allow, string[] disallow)
    {
        Validate(n, allow, disallow);

        var parents = new List<int>[n];
        for (int i = 0; i < n; i++)
            parents[i] = new List<int>();
        foreach (var edge in edges ?? Array.Empty<int[]>())
            parents[edge[1]].Add(edge[0]);

        const int White = 0, Grey = 1, Black = 2;
        var colour = new int[n];
        var allowMask = new int[n];
        var disallowMask = new int[n];

        void Resolve(int node)
        {
            if (colour[node] == Black)
                return;                                     // memo hit
            if (colour[node] == Grey)
                throw new ArgumentException("graph has a cycle; allow/disallow only propagate on a DAG", nameof(edges));

            colour[node] = Grey;
            int a = MaskOf(allow[node]), d = MaskOf(disallow[node]);

            foreach (int parent in parents[node])
            {
                Resolve(parent);
                a |= allowMask[parent];
                d |= disallowMask[parent];
            }

            allowMask[node] = a;
            disallowMask[node] = d;
            colour[node] = Black;
        }

        var result = new string[n];
        for (int i = 0; i < n; i++)
        {
            Resolve(i);
            result[i] = LettersOf(allowMask[i] & ~disallowMask[i]);
        }
        return result;
    }

    /// <summary>
    /// THE TRAP, implemented so it can be shown failing: propagate the ANSWER
    /// instead of the two masks. Union the parents' effective sets, add own
    /// allow, subtract own disallow.
    ///
    /// It agrees with the correct version on every graph where no descendant
    /// re-allows a letter one of its ancestors disallowed -- which is most casual
    /// test cases, hence the trap. Never ship this; it is here as a foil.
    /// </summary>
    public static string[] EffectiveNaive(int n, int[][] edges, string[] allow, string[] disallow)
    {
        Validate(n, allow, disallow);

        var order = TopologicalOrder(n, edges)
                    ?? throw new ArgumentException("graph has a cycle", nameof(edges));

        var (children, _) = Build(n, edges);
        var inherited = new int[n];

        var effective = new int[n];
        foreach (int node in order)
        {
            effective[node] = (inherited[node] | MaskOf(allow[node])) & ~MaskOf(disallow[node]);
            foreach (int child in children[node])
                inherited[child] |= effective[node];         // <- the loss happens here
        }

        var result = new string[n];
        for (int i = 0; i < n; i++)
            result[i] = LettersOf(effective[i]);
        return result;
    }

    private static void Validate(int n, string[] allow, string[] disallow)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n), "node count cannot be negative");
        if ((allow?.Length ?? 0) != n)
            throw new ArgumentException($"allow must hold exactly {n} entries", nameof(allow));
        if ((disallow?.Length ?? 0) != n)
            throw new ArgumentException($"disallow must hold exactly {n} entries", nameof(disallow));
    }

    // ------------------------------------------- the allow-only framing (roles)
    //
    // A very common variant drops the disallow set entirely: `privileges[i]` is
    // role i's own privileges, `grants` is a list of [parent, child] role pairs,
    // and every role inherits the union of its ancestors' privileges. Identical
    // topological pass with the disallow half deleted.
    //
    // Two things to pin down before writing it, because the prompts are sloppy:
    //
    //   1. Privileges are usually arbitrary STRINGS ("read", "admin"), not
    //      letters, so the bitmask only applies once you intern them. This
    //      version keeps hash sets, which is what the ask actually wants; the
    //      bitmask is the optimisation you offer if the universe is small and
    //      fixed.
    //   2. `grants` frequently mentions a role INDEX BEYOND the privileges array
    //      -- e.g. privileges for roles 0..2 with a grant [2, 3]. Role 3 is a
    //      real role with no privileges of its own that still inherits A, B, C.
    //      Sizing off privileges.Length alone throws; sizing off the grants alone
    //      loses trailing roles that were never granted anything. Take the max.

    /// <summary>
    /// Union of own privileges and all ancestors' privileges, per role. Roles are
    /// numbered 0..n-1 where n covers both arrays; each result is sorted so the
    /// output is deterministic. Throws on a cycle.
    /// </summary>
    public static List<List<string>> InheritPrivileges(string[][] privileges, int[][] grants)
    {
        privileges ??= Array.Empty<string[]>();
        grants ??= Array.Empty<int[]>();

        int n = privileges.Length;
        foreach (var grant in grants)
        {
            if (grant is null || grant.Length != 2)
                throw new ArgumentException("every grant must be a [parent, child] pair", nameof(grants));
            n = Math.Max(n, Math.Max(grant[0], grant[1]) + 1);
        }

        var (children, inDegree) = Build(n, grants);

        var sets = new HashSet<string>[n];
        for (int i = 0; i < n; i++)
            sets[i] = new HashSet<string>(i < privileges.Length
                ? privileges[i] ?? Array.Empty<string>()
                : Array.Empty<string>(), StringComparer.Ordinal);

        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        int settled = 0;
        while (queue.Count > 0)
        {
            int role = queue.Dequeue();
            settled++;

            foreach (int child in children[role])
            {
                sets[child].UnionWith(sets[role]);
                if (--inDegree[child] == 0)
                    queue.Enqueue(child);
            }
        }

        if (settled != n)
            throw new ArgumentException("role graph has a cycle; inheritance is only defined on a DAG", nameof(grants));

        var result = new List<List<string>>(n);
        for (int i = 0; i < n; i++)
        {
            var sorted = new List<string>(sets[i]);
            sorted.Sort(StringComparer.Ordinal);
            result.Add(sorted);
        }
        return result;
    }

    // ------------------------------------------------------------------ tests

    public static void Run()
    {
        Console.WriteLine("== the 5-node diamond, hand-checked ==");
        Console.WriteLine("  0 -> {1, 2} -> 3 -> 4");
        Console.WriteLine("  0 allows ab | 1 allows c | 2 DISALLOWS b | 3 re-allows b | 4 allows d, disallows c");

        var diamond = new[] { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 2, 3 }, new[] { 3, 4 } };
        var diamondAllow = new[] { "ab", "c", "", "b", "d" };
        var diamondDisallow = new[] { "", "", "b", "", "c" };

        var got = Effective(5, diamond, diamondAllow, diamondDisallow);
        var want = new[] { "ab", "abc", "a", "ac", "ad" };
        for (int i = 0; i < 5; i++)
            Console.WriteLine($"    node {i}: \"{got[i]}\" (expect \"{want[i]}\")");
        Console.WriteLine($"  all match: {got.SequenceEqual(want)}");
        Console.WriteLine("  node 3 is the point: 'b' arrives allowed via 1 and disallowed via 2, and");
        Console.WriteLine("  node 3 even re-allows it -- disallow still wins, on this and every descendant.");

        Console.WriteLine();
        Console.WriteLine("== the trap: propagating the ANSWER instead of the two masks ==");

        // Minimal witness: a disallows nothing, b disallows 'x', c re-allows 'x'.
        var chain = new[] { new[] { 0, 1 }, new[] { 1, 2 } };
        var chainAllow = new[] { "x", "", "x" };
        var chainDisallow = new[] { "", "x", "" };

        Console.WriteLine("  0 allows x -> 1 disallows x -> 2 allows x");
        Console.WriteLine($"    two masks (correct): [{string.Join(", ", Effective(3, chain, chainAllow, chainDisallow).Select(s => $"\"{s}\""))}]");
        Console.WriteLine($"    propagate effective: [{string.Join(", ", EffectiveNaive(3, chain, chainAllow, chainDisallow).Select(s => $"\"{s}\""))}]  <- node 2 resurrects x");
        Console.WriteLine("  Subtracting early destroys the information that x was forbidden upstream.");

        Console.WriteLine();
        Console.WriteLine("== top-down memoized DFS agrees ==");
        Console.WriteLine($"  diamond: {EffectiveMemoized(5, diamond, diamondAllow, diamondDisallow).SequenceEqual(got)}");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        Console.WriteLine($"  zero nodes: [{string.Join(", ", Effective(0, Array.Empty<int[]>(), Array.Empty<string>(), Array.Empty<string>()))}] (expect empty)");

        // Isolated nodes and roots: no edges at all, everyone keeps their own sets.
        var lonely = Effective(3, Array.Empty<int[]>(), new[] { "ab", "c", "" }, new[] { "b", "", "z" });
        Console.WriteLine($"  no edges at all: [{string.Join(", ", lonely.Select(s => $"\"{s}\""))}] (expect \"a\", \"c\", \"\")");
        Console.WriteLine("    node 0 disallows a letter it also allows -> disallow wins locally too");
        Console.WriteLine("    node 2 disallows 'z' it never had -> harmless, but it POISONS its descendants");

        // Duplicate edges must not change anything: OR is idempotent.
        var dup = new[] { new[] { 0, 1 }, new[] { 0, 1 }, new[] { 0, 1 } };
        Console.WriteLine($"  triple edge 0->1: [{string.Join(", ", Effective(2, dup, new[] { "a", "b" }, new[] { "", "" }).Select(s => $"\"{s}\""))}] (expect \"a\", \"ab\")");

        // A node with two parents that disagree, and nothing else.
        var fork = new[] { new[] { 0, 2 }, new[] { 1, 2 } };
        Console.WriteLine($"  parents allow-a / disallow-a into 2: [{string.Join(", ", Effective(3, fork, new[] { "a", "", "" }, new[] { "", "a", "" }).Select(s => $"\"{s}\""))}] (expect \"a\", \"\", \"\")");

        foreach (var (label, cyclic) in new[]
                 {
                     ("self-loop [[0,0]]", new[] { new[] { 0, 0 } }),
                     ("2-cycle [[0,1],[1,0]]", new[] { new[] { 0, 1 }, new[] { 1, 0 } }),
                 })
        {
            try
            {
                Effective(2, cyclic, new[] { "a", "b" }, new[] { "", "" });
                Console.WriteLine($"  {label}: NOT rejected -- bug");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  {label} rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        try
        {
            Effective(2, Array.Empty<int[]>(), new[] { "a" }, new[] { "", "" });
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  wrong-length allow rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== allow-only framing: role privileges ==");

        // The prompt's example. Note the inconsistency worth raising out loud:
        // grants mention role 3 while privileges only covers roles 0..2, so the
        // "expected" [[A],[A,B],[A,B,C]] is the answer for roles 0..2 with role 3
        // silently dropped. Role 3 exists and inherits A, B, C.
        var privileges = new[] { new[] { "A" }, new[] { "B" }, new[] { "C" } };
        var grants = new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 } };

        var inherited = InheritPrivileges(privileges, grants);
        for (int i = 0; i < inherited.Count; i++)
            Console.WriteLine($"    role {i}: [{string.Join(", ", inherited[i])}]");
        Console.WriteLine("  roles 0..2 are the prompt's [[A],[A,B],[A,B,C]]; role 3 is real too --");
        Console.WriteLine("  grants referenced it, so it inherits A,B,C with no privileges of its own.");

        // A role with two parents collects from both, and a role nobody granted
        // anything keeps exactly its own list.
        var wide = InheritPrivileges(
            new[] { new[] { "read" }, new[] { "write" }, Array.Empty<string>(), new[] { "audit" } },
            new[] { new[] { 0, 2 }, new[] { 1, 2 } });
        Console.WriteLine($"  two parents into role 2: [{string.Join(", ", wide[2])}] (expect read, write)");
        Console.WriteLine($"  ungranted role 3:        [{string.Join(", ", wide[3])}] (expect audit)");
        Console.WriteLine($"  empty everything:        {InheritPrivileges(Array.Empty<string[]>(), Array.Empty<int[]>()).Count} roles (expect 0)");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(26);
        bool matchesBrute = true, memoAgrees = true, monotone = true, withinAllow = true;
        bool naiveEverDiffers = false;
        int naiveDiffs = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            int n = rng.Next(0, 9);

            // Random DAG: draw edges only from a random permutation's prefix to
            // its suffix, so acyclicity is structural rather than hoped for.
            var label = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
            var edgeList = new List<int[]>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (rng.Next(3) == 0)
                        edgeList.Add(new[] { label[i], label[j] });   // [parent, child]
            var edges = edgeList.ToArray();

            // Small alphabet on purpose: 4 letters over 8 nodes makes conflicts
            // between branches common instead of astronomically rare.
            var allow = new string[n];
            var disallow = new string[n];
            for (int i = 0; i < n; i++)
            {
                allow[i] = RandomLetters(rng, 4);
                disallow[i] = RandomLetters(rng, 4);
            }

            var fast = EffectiveMasks(n, edges, allow, disallow);
            var slow = AncestorClosureBrute(n, edges, allow, disallow);
            matchesBrute &= fast.SequenceEqual(slow);

            var memo = EffectiveMemoized(n, edges, allow, disallow);
            memoAgrees &= memo.SequenceEqual(fast.Select(LettersOf));

            TryPropagate(n, edges, allow, disallow, out var allowMask, out var disallowMask);

            // Disallow only ever grows along an edge -- the monotonicity that
            // makes one pass and per-node caching sound.
            foreach (var e in edges)
                monotone &= (disallowMask[e[0]] & ~disallowMask[e[1]]) == 0;

            // Nothing is ever effective without having been allowed somewhere above.
            for (int i = 0; i < n; i++)
                withinAllow &= (fast[i] & ~allowMask[i]) == 0;

            if (n > 0 && !EffectiveNaive(n, edges, allow, disallow).SequenceEqual(fast.Select(LettersOf)))
            {
                naiveEverDiffers = true;
                naiveDiffs++;
            }
        }

        Console.WriteLine($"  4,000 random DAGs vs brute-force ancestor closure:     {matchesBrute}");
        Console.WriteLine($"  memoized DFS == Kahn pass:                             {memoAgrees}");
        Console.WriteLine($"  disallow grows monotonically along every edge:         {monotone}");
        Console.WriteLine($"  effective is always a subset of accumulated allow:     {withinAllow}");
        Console.WriteLine($"  the naive 'propagate the answer' version is wrong:     {naiveEverDiffers} ({naiveDiffs} of 4000 caught it)");
        Console.WriteLine("  ^ it still AGREES on the other half, and on nearly every hand-written test,");
        Console.WriteLine("    which is exactly why the trap survives casual checking.");
    }

    private static string RandomLetters(Random rng, int alphabet)
    {
        var chars = new List<char>();
        for (int i = 0; i < alphabet; i++)
            if (rng.Next(3) == 0)
                chars.Add((char)('a' + i));
        return new string(chars.ToArray());
    }

    /// <summary>
    /// Reference implementation: the definition read literally. For each node,
    /// find every ancestor (plus itself) by walking parent edges, OR the raw
    /// annotations, subtract. No topological sort, no DP, O(V * (V + E)) -- fine
    /// at n &lt;= 8 and exactly what the one-pass version has to reproduce.
    /// </summary>
    private static int[] AncestorClosureBrute(int n, int[][] edges, string[] allow, string[] disallow)
    {
        var parents = new List<int>[n];
        for (int i = 0; i < n; i++)
            parents[i] = new List<int>();
        foreach (var e in edges)
            parents[e[1]].Add(e[0]);

        var effective = new int[n];
        for (int start = 0; start < n; start++)
        {
            var seen = new bool[n];
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;

            int a = 0, d = 0;
            while (stack.Count > 0)
            {
                int node = stack.Pop();
                a |= MaskOf(allow[node]);
                d |= MaskOf(disallow[node]);

                foreach (int parent in parents[node])
                    if (!seen[parent])
                    {
                        seen[parent] = true;
                        stack.Push(parent);
                    }
            }

            effective[start] = a & ~d;
        }

        return effective;
    }
}

// ---- Notes for the follow-up questions ----
//
// "The alphabet isn't 26 letters, it's 10,000 named permissions."
//     One int stops working. Options, in the order worth offering: (a) intern the
//     names to ints and use a fixed-size bit array / ulong[] -- still O(sigma/64)
//     per edge; (b) keep hash sets but SHARE them by reference when a node has a
//     single parent and no annotations of its own, which collapses long chains to
//     O(1) per node; (c) persistent/immutable sets with structural sharing when
//     the answer must be queried per node without materialising all of them.
//
// "Which ancestor is responsible for denying letter x on node v?"
//     Propagate a second array alongside the mask: blame[v][x] = the ancestor that
//     first contributed bit x, taking the parent's blame when the bit arrives from
//     above and v itself when v disallows it. O(V * sigma) memory. Interviewers ask
//     this immediately after the base solution because a real permission system
//     has to answer "why am I denied?".
//
// "The nearest ancestor should win, not disallow."
//     Say plainly that on a DAG this is under-specified: two parents at equal
//     distance can disagree, so the rule needs a tie-break (priority per node,
//     edge order, deny-on-tie). Given one, the pass still works but the state per
//     node becomes (letter -> (distance, decision)) rather than two bitmasks, and
//     it is no longer monotone -- so the "once disallowed" caching argument is gone
//     and each letter must be tracked separately. This is the honest reason the
//     standard framing picks disallow-wins.
//
// "Edges arrive one at a time / the annotations change."
//     Adding an edge p -> c only ever GROWS masks in c's descendant cone, so
//     re-propagate from c and stop early wherever nothing changed -- typically
//     tiny. REMOVING an edge or a permission is the hard direction: growth-only
//     reasoning breaks and the affected cone must be recomputed from its
//     surviving parents, which is why permission systems tend to rebuild rather
//     than incrementally revoke.
//
// "What if the graph might have cycles?"
//     Condense the SCCs (Tarjan), propagate over the condensation DAG, then hand
//     every node its component's masks -- inside a cycle everyone is everyone
//     else's ancestor, so they necessarily share one answer. That is a real
//     answer, unlike "assume it doesn't happen", and it is O(V + E) too.
//
// "V = 10^5, E = 10^6, sigma = 26."
//     Two ints per node is 800 KB; the pass is ~10^6 OR-s. The dominant cost is
//     building the adjacency list, and the recursive memoized variant is the only
//     thing here that would blow the stack -- reach for the Kahn version.
