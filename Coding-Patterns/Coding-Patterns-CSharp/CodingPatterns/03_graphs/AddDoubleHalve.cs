// Reach Target via Add / Double / Halve
// Difficulty: Medium (Easy if "any path" is accepted, Hard if "shortest" is)
// Pattern: BFS over integer states -- but the asked-for answer is a construction
//
//   add(n)   = n + 2
//   dub(n)   = n * 2
//   split(n) = floor(n / 2)
//
// func(a, b) returns ONE sequence of ops taking a to b. All inputs and outputs
// are positive integers. The path does NOT have to be optimal.
//
// This is asked without example I/O, so pin the contract down first. The three
// questions that actually change the code:
//
//   1. "Any valid path, or the shortest one?" This is THE question. Any path is
//      a ten-line while loop (Func). Shortest is a BFS (FuncShortest). The
//      interviewer here explicitly wanted the simple construction, so lead with
//      it -- but say out loud that you know BFS is the shortest-path answer, or
//      it reads like you did not notice.
//   2. "Are intermediate values bounded?" A search needs a ceiling to be finite,
//      because split lets a path climb above max(a, b) and come back down. The
//      construction never leaves [1, max(a, b)], which is a real point in its
//      favour and worth saying.
//   3. "Positive integers -- so split(1) = 0 is illegal?" Yes. Every loop below
//      stops splitting at 1. This is the only genuine edge case in the problem
//      and it is one `> 1` away from a bug.
//
// The three answers, and what each costs in NUMBER OF OPS returned:
//
//   Func           split a down to 1, then rebuild b       O(log a + b)  ops
//   FuncCompact    same, but rebuild b via split           O(log^2 b)    ops
//   FuncShortest   BFS from a, shortest under a ceiling    optimal
//
// WHY THE SIMPLE ONE IS O(b) AND NOT O(log b), which is the follow-up that
// separates people. Rebuilding b from 1 with only add and dub, every reachable
// value has the form
//
//       2^k + 2 * (2^d_1 + 2^d_2 + ... + 2^d_j)
//
// where k is the total number of dubs and d_i is how many dubs came AFTER the
// i-th add. If b is odd then 2^k must be odd, so k = 0, so every term is 2^0 = 1
// and j = (b - 1) / 2. That is not a lazy bound -- with only add and dub, an odd
// target costs exactly (b - 1) / 2 adds, and no cleverness inside those two ops
// can beat it. split is the ONLY way to reach a large odd number quickly, by
// arriving from above: split(2b) = split(2b + 1) = b. That is what FuncCompact
// exploits, and it is also why the truly shortest path needs a search rather
// than a formula -- once paths may overshoot and come back down, the choice of
// where to overshoot to is a search problem.

namespace CodingPatterns.Graphs;

public static class AddDoubleHalve
{
    public enum Op
    {
        Add,    // n -> n + 2
        Dub,    // n -> n * 2
        Split   // n -> n / 2   (floor; illegal at n = 1, which would leave the positives)
    }

    // The three operations, spelled out. Worth writing even though they are
    // one-liners: it keeps the search and the construction talking about the
    // same definitions instead of open-coding `/ 2` in four places.
    public static long Add(long n) => n + 2;
    public static long Dub(long n) => n * 2;
    public static long Split(long n) => n / 2;

    // ------------------------------------------------------- the asked-for answer

    /// <summary>
    /// One sequence of ops taking <paramref name="a"/> to <paramref name="b"/>.
    /// Not optimal, and deliberately so.
    ///
    /// Two independent phases, which is the whole idea -- 1 is a universal hub:
    ///
    ///   down: split until we hit 1. Every positive integer reaches 1 this way
    ///         (2 -> 1 and 3 -> 1), so this phase never fails and never needs to
    ///         look at b.
    ///   up:   walk b BACKWARDS to 1 and reverse the trace. Going down from b,
    ///         even means "the last op was a dub" (undo: halve) and odd > 1 means
    ///         "the last op was an add" (undo: subtract 2). Odd stays odd under
    ///         -2 so it lands exactly on 1, never on 0.
    ///
    /// Reversing an undo-trace is the trick worth naming in the interview: it is
    /// much easier to argue "b came from b/2 by a dub" than to guess forwards
    /// which op to apply next.
    /// </summary>
    public static IReadOnlyList<Op> Func(long a, long b)
    {
        if (a < 1) throw new ArgumentOutOfRangeException(nameof(a), a, "must be a positive integer");
        if (b < 1) throw new ArgumentOutOfRangeException(nameof(b), b, "must be a positive integer");

        var ops = new List<Op>();
        if (a == b)
            return ops;

        for (long v = a; v > 1; v = Split(v))   // > 1, not > 0: split(1) = 0
            ops.Add(Op.Split);

        var up = new List<Op>();
        for (long t = b; t > 1; )
        {
            if (t % 2 == 0) { t /= 2; up.Add(Op.Dub); }
            else            { t -= 2; up.Add(Op.Add); }
        }

        up.Reverse();                            // undo-trace -> forward ops
        ops.AddRange(up);
        return ops;
    }

    // -------------------------------------------- same shape, O(log^2 b) ops

    /// <summary>
    /// Same two phases, but the climb uses split to reach odd targets from above
    /// instead of paying (b - 1) / 2 adds for them. Still no search.
    ///
    /// For odd t > 1, note 2(t - 1) is even and its odd part is at most
    /// (t - 1) / 2, so it is strictly cheaper to reach than t itself:
    ///
    ///       ... -> 2(t - 1) --add--> 2t --split--> t
    ///
    /// Each level of that recursion halves the odd part and spends O(log t) dubs,
    /// so the whole thing is O(log^2 t) ops. For b = 1001 the climb is 22 ops
    /// where <see cref="Func"/>'s is 500.
    ///
    /// Offer this only after the simple version is on the board and working.
    /// </summary>
    public static IReadOnlyList<Op> FuncCompact(long a, long b)
    {
        // Appends the ops taking 1 to t.
        static void Climb(long t, List<Op> ops)
        {
            if (t == 1)
                return;

            if (t % 2 == 0)
            {
                // t = 2^k * m with m odd. Reach the odd part, then dub k times.
                long m = t;
                int k = 0;
                while (m % 2 == 0) { m /= 2; k++; }

                Climb(m, ops);
                for (int i = 0; i < k; i++)
                    ops.Add(Op.Dub);
                return;
            }

            if (t <= SmallOddDirect)
            {
                for (long v = 1; v < t; v += 2)
                    ops.Add(Op.Add);
                return;
            }

            Climb(2 * (t - 1), ops);   // even, odd part <= (t - 1) / 2
            ops.Add(Op.Add);           // 2t - 2 -> 2t
            ops.Add(Op.Split);         // 2t     -> t
        }

        if (a < 1) throw new ArgumentOutOfRangeException(nameof(a), a, "must be a positive integer");
        if (b < 1) throw new ArgumentOutOfRangeException(nameof(b), b, "must be a positive integer");

        var ops = new List<Op>();
        if (a == b)
            return ops;

        for (long v = a; v > 1; v = Split(v))
            ops.Add(Op.Split);

        Climb(b, ops);
        return ops;
    }

    /// <summary>Below this, an odd target is cheaper to reach by plain adds.</summary>
    private const long SmallOddDirect = 9;

    // ------------------------------------------------------------- the BFS answer

    /// <summary>
    /// Shortest sequence, by BFS over integer values. Every edge costs 1, so the
    /// first time the frontier touches a value it arrived along a shortest path
    /// and that value is final -- the same property that makes BFS the right tool
    /// on an unweighted grid. The state space happens to be the integers instead
    /// of cells, which is the only thing that makes this look unlike a graph
    /// problem.
    ///
    /// The ceiling is not optional. split means a path can climb above
    /// max(a, b) and come back down, so without a bound the frontier is infinite
    /// and BFS never terminates on an unreachable target. Any ceiling at least
    /// max(a, b) guarantees a path EXISTS -- split a down to 1, dub to 2 if b is
    /// even, then add up -- and every value on that path is at most max(a, b).
    /// The default leaves headroom above 2b so arrive-from-above routes are in
    /// scope; the result is the shortest path that stays under the ceiling, which
    /// is the honest claim to make about it.
    /// </summary>
    /// <returns>The ops, or null if b is unreachable below the ceiling.</returns>
    public static IReadOnlyList<Op> FuncShortest(long a, long b, long ceiling = 0)
    {
        if (a < 1) throw new ArgumentOutOfRangeException(nameof(a), a, "must be a positive integer");
        if (b < 1) throw new ArgumentOutOfRangeException(nameof(b), b, "must be a positive integer");

        long floorCeiling = Math.Max(a, b);
        if (ceiling <= 0)
            ceiling = 2 * floorCeiling + 2;
        if (ceiling < floorCeiling)
            ceiling = floorCeiling;             // below this, "unreachable" is an artifact

        if (a == b)
            return Array.Empty<Op>();

        static IEnumerable<(long Value, Op Via)> Neighbours(long v, long ceiling)
        {
            if (v + 2 <= ceiling) yield return (v + 2, Op.Add);
            if (v <= ceiling / 2) yield return (v * 2, Op.Dub);   // written to avoid overflow
            if (v > 1)            yield return (v / 2, Op.Split); // v = 1 would leave the positives
        }

        // parent doubles as the visited set: present == already reached, and that
        // first reach is final. No separate HashSet.
        var parent = new Dictionary<long, (long Prev, Op Via)> { [a] = (0, default) };
        var queue = new Queue<long>();
        queue.Enqueue(a);

        while (queue.Count > 0)
        {
            long v = queue.Dequeue();

            foreach (var (next, op) in Neighbours(v, ceiling))
            {
                if (parent.ContainsKey(next))
                    continue;

                parent[next] = (v, op);
                if (next == b)
                {
                    var ops = new List<Op>();
                    for (long at = b; at != a; at = parent[at].Prev)
                        ops.Add(parent[at].Via);

                    ops.Reverse();
                    return ops;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }

    // ----------------------------------------------------------------- plumbing

    /// <summary>
    /// Replays a sequence and returns where it lands. Throws if any intermediate
    /// value leaves the positive integers -- which is the only way these
    /// solutions can be wrong, so the demo checks every path it produces.
    /// </summary>
    public static long Apply(long start, IEnumerable<Op> ops)
    {
        long v = start;
        foreach (var op in ops)
        {
            v = op switch
            {
                Op.Add   => Add(v),
                Op.Dub   => Dub(v),
                Op.Split => Split(v),
                _        => throw new ArgumentOutOfRangeException(nameof(ops), op, "unknown op")
            };

            if (v < 1)
                throw new InvalidOperationException($"{op} left the positive integers (reached {v})");
        }

        return v;
    }

    public static string Format(long start, IReadOnlyList<Op> ops, int maxShown = 12)
    {
        if (ops == null)
            return "(no path)";
        if (ops.Count == 0)
            return $"{start} (already there)";

        var parts = new List<string> { start.ToString() };
        long v = start;
        for (int i = 0; i < ops.Count && i < maxShown; i++)
        {
            v = Apply(v, new[] { ops[i] });
            parts.Add($"-{ops[i].ToString().ToLowerInvariant()}-> {v}");
        }

        if (ops.Count > maxShown)
            parts.Add($"... (+{ops.Count - maxShown} more)");

        return string.Join(" ", parts);
    }

    // --------------------------------------------------------------------- demo

    public static void Run()
    {
        (long A, long B)[] cases =
        {
            (1, 1), (5, 5),          // already there
            (1, 2), (2, 1),          // the smallest non-trivial pairs
            (3, 8), (8, 3),          // up and down
            (4, 7), (7, 4),          // parity crossings
            (1, 1024), (1024, 1),    // pure dub / pure split
            (6, 1001),               // large ODD target -- where Func blows up
            (1000, 999),
            (17, 1000000),
            (999983, 4)              // large prime start
        };

        Console.WriteLine("== all three agree on the endpoint; only the LENGTH differs ==");

        foreach (var (a, b) in cases)
        {
            var simple = Func(a, b);
            var compact = FuncCompact(a, b);
            var shortest = FuncShortest(a, b);

            Check(a, b, simple, nameof(Func));
            Check(a, b, compact, nameof(FuncCompact));
            Check(a, b, shortest, nameof(FuncShortest));

            Console.WriteLine($"  a={a,-8} b={b,-8} simple={simple.Count,-8} compact={compact.Count,-6} shortest={shortest.Count}");
        }

        Console.WriteLine();
        Console.WriteLine("== traces ==");
        foreach (var (a, b) in new (long, long)[] { (4, 7), (3, 8), (6, 25) })
        {
            Console.WriteLine($"  Func({a},{b})         {Format(a, Func(a, b))}");
            Console.WriteLine($"  FuncShortest({a},{b}) {Format(a, FuncShortest(a, b))}");
        }

        Console.WriteLine();
        Console.WriteLine("== the O(b) blow-up the follow-up is about ==");
        Console.WriteLine($"  b = 1001 (odd): Func {Func(6, 1001).Count} ops, "
                        + $"FuncCompact {FuncCompact(6, 1001).Count} ops, "
                        + $"FuncShortest {FuncShortest(6, 1001).Count} ops");
        Console.WriteLine("  Func pays (b-1)/2 = 500 adds because add+dub alone cannot");
        Console.WriteLine("  build a large odd number any faster. split(2b) = b is the escape.");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");
        Console.WriteLine($"  a == b returns empty:            {Func(9, 9).Count} ops");
        Console.WriteLine($"  never splits at 1 (1 -> 0 illegal): Apply verified on every case above");

        // Exhaustive: every path all three produce must land exactly on b and
        // must never leave the positive integers. Apply asserts both.
        for (long a = 1; a <= 60; a++)
            for (long b = 1; b <= 60; b++)
            {
                Check(a, b, Func(a, b), nameof(Func));
                Check(a, b, FuncCompact(a, b), nameof(FuncCompact));
                Check(a, b, FuncShortest(a, b), nameof(FuncShortest));
            }
        Console.WriteLine("  exhaustive 60x60 sweep, all three methods: OK");

        foreach (var bad in new (long A, long B)[] { (0, 5), (5, 0), (-3, 5) })
        {
            try
            {
                Func(bad.A, bad.B);
                Console.WriteLine($"  ({bad.A},{bad.B}) NOT rejected -- bug");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Console.WriteLine($"  ({bad.A},{bad.B}) rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }
    }

    private static void Check(long a, long b, IReadOnlyList<Op> ops, string who)
    {
        if (ops == null)
            throw new InvalidOperationException($"{who}({a},{b}) found no path");

        long landed = Apply(a, ops);   // also asserts every value stayed positive
        if (landed != b)
            throw new InvalidOperationException($"{who}({a},{b}) landed on {landed}");
    }
}

// ---------------------------------------------------------------- FOLLOW-UPS
//
// "Now give me the shortest path."
//     FuncShortest. The only real content is the ceiling: state that without a
//     bound the state space is infinite (split lets paths climb and return), that
//     ceiling >= max(a, b) already guarantees a path exists, and that the answer
//     is therefore "shortest among paths under the ceiling". Claiming global
//     optimality without a bound argument is the trap.
//
// "Make the search faster."
//     Bidirectional BFS. Forward ops from a and reverse ops from b (the reverse
//     of add is -2, of dub is halve-if-even, of split is the PAIR 2v and 2v+1 --
//     that last one is what people forget, and dropping it silently loses paths).
//     Meeting in the middle turns branching^d into 2 * branching^(d/2).
//
// "What if the ops were add(n) = n + 1 instead of n + 2?"
//     The problem collapses. With +1, x2 and floor-halve you get the classic
//     "reach b from a" bit-building greedy: reduce b to a by halving when even
//     and subtracting one when odd. The +2 is what breaks it, because +2 cannot
//     change parity, so odd targets become unreachable-in-log-time from below and
//     the arrive-from-above trick becomes necessary. If you only remember one
//     thing about this question, remember that the step size being 2 is the
//     entire difficulty.
//
// "Is a always able to reach b?"
//     Yes, for all positive a and b, and the proof is the construction: split to
//     1, dub to 2 if b is even, add up. No parity or divisibility obstruction
//     survives, because split is a parity-destroying escape hatch. Say this
//     early -- it kills the "when is it impossible" line of questioning before it
//     eats ten minutes.
//
// "Minimise the number of DISTINCT ops, or weight the ops differently."
//     Weighted edges break BFS's first-touch-is-final property and you move to
//     Dijkstra over the same state space. The construction is unaffected in shape
//     but no longer has any claim to being good.
//
// "a and b are up to 10^18."
//     Func is O(b) ops and is dead on arrival. FuncCompact is O(log^2 b) ~ 3600
//     ops and still works, using only O(log b) memory. BFS is dead too: its
//     visited set is proportional to the number of reachable values under the
//     ceiling. This is the scale at which the "worse" answer is the only answer,
//     which is a nice note to end on.
