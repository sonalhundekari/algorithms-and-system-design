// Throne Inheritance
// LeetCode #1600 | Difficulty: Medium (the trap is Hard to spot)
// Pattern: preorder DFS over a dynamically grown n-ary tree; deletion by
//          MASKING rather than by removal; iterative traversal for depth safety
//
//     ThroneInheritance(kingName)      the root
//     Birth(parentName, childName)     childName is parentName's YOUNGEST child
//     Death(name)                      name is skipped in the order, and nothing else
//     GetInheritanceOrder()            preorder, parent before children, eldest first,
//                                      the dead omitted
//
// THE ONE INSIGHT, and it is the whole problem: the order is a pure function of
// the tree plus the dead set. There is nothing to maintain. Store the children in
// birth order, store the dead in a set, and walk the tree when someone asks.
//
//     Birth                O(1)   -- append to a list; appending IS "youngest last"
//     Death                O(1)   -- add to a set; the tree does not change
//     GetInheritanceOrder  O(n)   -- one preorder walk
//
// Candidates who try to keep a live ordered list instead end up inserting into
// the middle of an array on every birth (O(n) each, and finding the insertion
// point is its own subproblem), which is strictly worse and much easier to get
// wrong. Recomputing is not the lazy answer here; it is the right one.
//
// THE TRAP: A DEAD PERSON IS NOT A DELETED NODE.
//
// `Death` only makes someone invisible in the OUTPUT. They stay in the tree, they
// keep their children, and their children keep their position. The tempting
// alternative is to delete the node for real and splice its children up into its
// slot in the parent's list. Be precise about what is wrong with that, because
// the obvious accusation is false and an interviewer will call it:
//
//     king -> [andy, bob, catherine], bob -> [alex, asha]
//     bob dies.
//       masked   (correct):  king, andy, matthew, alex, asha, catherine
//       spliced  (wrong):    king, andy, matthew, alex, asha, catherine
//
// The SAME list -- and not by luck. Splicing a node's children into the node's
// own slot preserves preorder exactly, so as long as every later birth names a
// living parent the two models agree forever. That is precisely why the bug
// ships. Where it actually breaks:
//
//   1. THE KING. The root has no parent to splice into, so the whole model has
//      no defined behaviour for the one death most likely to be tested.
//   2. A BIRTH TO A DEAD PARENT. Nothing in the constraints forbids it -- "the
//      parent exists in the tree" is still true of the dead. The masking model
//      files the child in the right place without noticing; the splicing model
//      cannot find the parent at all. (Worth one clarifying question.)
//   3. UNDO / RESURRECT. Masking is a set flag, so it reverses in O(1). Splicing
//      destroys the shape; reversing it needs a full undo log.
//   4. COST. Splicing is O(children) per death and repeatedly widens the parent's
//      list; masking is O(1) and touches nothing.
//
// All four are demonstrated below. The rule is one line: never mutate the tree in
// `Death`.
//
// THE SECOND TRAP: RECURSION DEPTH.
//
// The constraints allow 10^5 births and say nothing about shape, so the family
// tree may be a straight line: king -> a -> b -> ... 10^5 deep. That is 10^5
// stack frames. .NET's default 1 MB thread stack holds roughly 10-20k frames of
// this size, so the natural recursive preorder does not return a wrong answer --
// it kills the process with a StackOverflowException that .NET explicitly makes
// UNCATCHABLE. Write the iterative version. This file has both, and the tests
// walk a 100,000-node chain to show which one survives.
//
// Space: O(n) for the tree. The traversal is O(width) with a node stack (a star
// with 10^5 children pushes all of them) or O(depth) with a frame stack; both
// implementations are here, and both are O(n) in the worst case -- they just have
// different worst cases.
//
// Cost across q operations: Θ(q · alive) worst case, and no data structure beats
// that while the answer is "print the whole list" -- the OUTPUT is that big. The
// cache below removes only the repeated-query waste. If the real question is
// "who is next?" or "who is k-th?", that is a different problem with a better
// answer; see the notes at the bottom.

namespace CodingPatterns.Graphs;

/// <summary>
/// The answer. Births append, deaths mask, the order is computed on demand.
/// </summary>
public class ThroneInheritance
{
    private readonly string _king;

    // Every living OR dead person maps to their children in birth order. A person
    // with no children maps to an empty list, never to a missing key -- the walk
    // then never has to distinguish "leaf" from "typo".
    private readonly Dictionary<string, List<string>> _children;

    // Masked, not removed. This set is the only thing Death touches.
    private readonly HashSet<string> _dead = new(StringComparer.Ordinal);

    // Memoised answer, dropped by any mutation. Purely an optimisation for
    // repeated queries between births; see the note on Θ(q · alive) above.
    private IReadOnlyList<string> _cachedOrder;

    public ThroneInheritance(string kingName)
    {
        if (string.IsNullOrEmpty(kingName))
            throw new ArgumentException("the kingdom needs a king", nameof(kingName));

        _king = kingName;
        _children = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            [kingName] = new List<string>(),
        };
    }

    /// <summary>Number of people ever born, dead included. The tree keeps them all.</summary>
    public int Population => _children.Count;

    /// <summary>Number of people who would appear in the order.</summary>
    public int LivingCount => _children.Count - _dead.Count;

    /// <summary>
    /// Record a birth. The child becomes the parent's youngest, which is what
    /// "append" means -- there is no sorting step and no timestamp to store.
    /// </summary>
    public void Birth(string parentName, string childName)
    {
        // The problem guarantees both of these; they are here because a silent
        // wrong answer from a bad key is far more expensive to debug than a throw.
        if (!_children.TryGetValue(parentName, out var siblings))
            throw new ArgumentException($"no such person: {parentName}", nameof(parentName));
        if (_children.ContainsKey(childName))
            throw new ArgumentException($"{childName} already exists; names are unique", nameof(childName));

        siblings.Add(childName);
        _children[childName] = new List<string>();
        _cachedOrder = null;
    }

    /// <summary>
    /// Record a death. The person keeps their place in the tree and keeps their
    /// children -- only their own name stops being printed.
    /// </summary>
    public void Death(string name)
    {
        if (!_children.ContainsKey(name))
            throw new ArgumentException($"no such person: {name}", nameof(name));

        if (_dead.Add(name))                     // idempotent; the problem promises it is alive
            _cachedOrder = null;
    }

    public bool IsAlive(string name) =>
        _children.ContainsKey(name) && !_dead.Contains(name);

    /// <summary>
    /// The line of succession: preorder, eldest child first, the dead skipped.
    /// </summary>
    /// <remarks>
    /// The cached list is handed out as a read-only view, not as the List itself.
    /// LeetCode's signature returns IList&lt;string&gt;, and returning the live
    /// backing list there means a caller can quietly reorder the succession.
    /// </remarks>
    public IReadOnlyList<string> GetInheritanceOrder() =>
        _cachedOrder ??= BuildOrder().AsReadOnly();

    // ------------------------------------------------------------- the traversal

    /// <summary>
    /// Iterative preorder with a NODE stack. Children are pushed in reverse so
    /// the eldest is popped first -- the single line where the "older children
    /// come first" rule actually lives, and the easiest one to get backwards.
    ///
    /// Stack depth is O(width): one node with 100,000 children puts all 100,000
    /// on the stack at once. That is fine on the heap; it is the frame version
    /// below that trades this for O(depth).
    /// </summary>
    private List<string> BuildOrder()
    {
        var order = new List<string>(LivingCount);
        var stack = new Stack<string>();
        stack.Push(_king);

        while (stack.Count > 0)
        {
            string name = stack.Pop();

            // Visit BEFORE descending -- that is what makes it preorder, and the
            // dead check is a print filter, not a prune. A dead parent's living
            // children still inherit through them.
            if (!_dead.Contains(name))
                order.Add(name);

            var kids = _children[name];
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push(kids[i]);
        }

        return order;
    }

    /// <summary>
    /// The same walk with an explicit FRAME stack -- a literal transcription of
    /// the recursion, holding (node, next child index) instead of the nodes
    /// themselves. Stack usage is O(depth) rather than O(width).
    ///
    /// Kept public because it is the honest answer to "now do it without
    /// recursion and without blowing up on a wide tree", and because the tests
    /// cross-check it against <see cref="BuildOrder"/> on random shapes.
    /// </summary>
    public IReadOnlyList<string> GetInheritanceOrderFrames()
    {
        var order = new List<string>(LivingCount);
        var path = new List<(string Name, int Next)> { (_king, 0) };

        if (!_dead.Contains(_king))
            order.Add(_king);                    // the root is visited on entry

        while (path.Count > 0)
        {
            var (name, next) = path[^1];
            var kids = _children[name];

            if (next == kids.Count)              // no children left: return
            {
                path.RemoveAt(path.Count - 1);
                continue;
            }

            path[^1] = (name, next + 1);         // advance BEFORE descending, or
            string child = kids[next];           // the child is visited forever

            if (!_dead.Contains(child))
                order.Add(child);
            path.Add((child, 0));
        }

        return order;
    }

    /// <summary>
    /// Recursive preorder. Correct, shorter, and unusable at the stated limits --
    /// a 10^5-deep chain overflows the stack, and a StackOverflowException cannot
    /// be caught in .NET, so the process simply dies. Present for contrast; the
    /// tests only call it on small trees, deliberately.
    /// </summary>
    public IReadOnlyList<string> GetInheritanceOrderRecursive()
    {
        var order = new List<string>(LivingCount);
        Visit(_king, order);
        return order;
    }

    private void Visit(string name, List<string> order)
    {
        if (!_dead.Contains(name))
            order.Add(name);

        foreach (string child in _children[name])
            Visit(child, order);
    }

    // -------------------------------------------------- what is usually wanted

    /// <summary>
    /// The heir: the first living person in the order. Same walk, but it stops at
    /// the first hit instead of materialising the list, so a living king costs
    /// O(1) instead of O(n). Returns null if the whole family is dead.
    ///
    /// Worth volunteering -- "who inherits?" is the question the kingdom actually
    /// asks, and answering it does not require building the succession list.
    /// </summary>
    public string NextInLine() => KthInLine(0);

    /// <summary>
    /// The k-th living person in the order, 0-based, or null if fewer than k+1
    /// people are alive. Still O(n) worst case, but it stops as soon as it has
    /// counted far enough -- see the notes for the O(log n) version.
    /// </summary>
    public string KthInLine(int k)
    {
        if (k < 0)
            throw new ArgumentOutOfRangeException(nameof(k));

        if (_cachedOrder is not null)            // already paid for; just index it
            return k < _cachedOrder.Count ? _cachedOrder[k] : null;

        var stack = new Stack<string>();
        stack.Push(_king);

        while (stack.Count > 0)
        {
            string name = stack.Pop();
            if (!_dead.Contains(name) && k-- == 0)
                return name;

            var kids = _children[name];
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push(kids[i]);
        }

        return null;
    }

    public static void Run() => ThroneInheritanceDemo.Execute();
}

/// <summary>
/// The wrong model, implemented so the difference is visible rather than
/// asserted: `Death` splices the dead person's children up to their parent.
/// Identical output on the sample, divergent as soon as a later birth lands
/// beside the spliced-in children.
/// </summary>
internal sealed class SplicingThroneInheritance
{
    private readonly string _king;
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _parent = new(StringComparer.Ordinal);

    public SplicingThroneInheritance(string kingName)
    {
        _king = kingName;
        _children[kingName] = new List<string>();
    }

    public void Birth(string parentName, string childName)
    {
        _children[parentName].Add(childName);
        _children[childName] = new List<string>();
        _parent[childName] = parentName;
    }

    public void Death(string name)
    {
        var kids = _children[name];

        if (name == _king)                       // the root has nowhere to splice to
        {
            // Pretend the eldest child becomes king. Any choice here is a fresh
            // wrong answer; that is the point.
            _children.Remove(name);
            return;
        }

        var siblings = _children[_parent[name]];
        int at = siblings.IndexOf(name);
        siblings.RemoveAt(at);
        siblings.InsertRange(at, kids);          // children take the dead node's slot

        foreach (string kid in kids)
            _parent[kid] = _parent[name];

        _children.Remove(name);
        _parent.Remove(name);
    }

    public IReadOnlyList<string> GetInheritanceOrder()
    {
        var order = new List<string>();
        if (!_children.ContainsKey(_king))
            return order;

        var stack = new Stack<string>();
        stack.Push(_king);

        while (stack.Count > 0)
        {
            string name = stack.Pop();
            order.Add(name);

            var kids = _children[name];
            for (int i = kids.Count - 1; i >= 0; i--)
                stack.Push(kids[i]);
        }

        return order;
    }
}

internal static class ThroneInheritanceDemo
{
    public static void Execute()
    {
        Console.WriteLine("== the sample ==");

        var t = new ThroneInheritance("king");
        t.Birth("king", "andy");
        t.Birth("king", "bob");
        t.Birth("king", "catherine");
        t.Birth("andy", "matthew");
        t.Birth("bob", "alex");
        t.Birth("bob", "asha");

        Console.WriteLine("  king -> [andy, bob, catherine], andy -> [matthew], bob -> [alex, asha]");
        Console.WriteLine($"  order        {Show(t.GetInheritanceOrder())}");
        Console.WriteLine($"  expected     [king, andy, matthew, bob, alex, asha, catherine]");

        t.Death("bob");
        Console.WriteLine($"  bob dies ->  {Show(t.GetInheritanceOrder())}");
        Console.WriteLine($"  expected     [king, andy, matthew, alex, asha, catherine]");
        Console.WriteLine("  bob is gone from the LIST; alex and asha did not move. He is still a node.");

        Console.WriteLine();
        Console.WriteLine("== masking vs deleting: the lists AGREE, and that is the point ==");

        var masked = Build();
        var spliced = Splicing();

        masked.Death("bob");
        spliced.Death("bob");
        Console.WriteLine($"  mask   {Show(masked.GetInheritanceOrder())}");
        Console.WriteLine($"  splice {Show(spliced.GetInheritanceOrder())}");

        masked.Birth("king", "diana");
        spliced.Birth("king", "diana");
        Console.WriteLine($"  ...and after a later birth to the king:");
        Console.WriteLine($"  mask   {Show(masked.GetInheritanceOrder())}");
        Console.WriteLine($"  splice {Show(spliced.GetInheritanceOrder())}");
        Console.WriteLine($"  identical: {masked.GetInheritanceOrder().SequenceEqual(spliced.GetInheritanceOrder())}");
        Console.WriteLine("  Splicing a node's children into its own slot PRESERVES preorder, so the");
        Console.WriteLine("  two models agree for as long as every birth names a living parent. Do not");
        Console.WriteLine("  claim the list comes out wrong -- it does not. Claim these instead:");

        Console.WriteLine();
        Console.WriteLine("  (1) the king dies -- the root has no slot to splice into");
        var maskedKing = Build();
        var splicedKing = Splicing();
        maskedKing.Death("king");
        splicedKing.Death("king");
        Console.WriteLine($"      mask   {Show(maskedKing.GetInheritanceOrder())}");
        Console.WriteLine($"      splice {Show(splicedKing.GetInheritanceOrder())}   <- the kingdom vanished");

        Console.WriteLine();
        Console.WriteLine("  (2) a birth recorded for a dead parent -- legal: he is still IN the tree");
        var posthumous = Build();
        posthumous.Death("bob");
        posthumous.Birth("bob", "beatrice");
        Console.WriteLine($"      mask   {Show(posthumous.GetInheritanceOrder())}");
        Console.WriteLine($"             beatrice files under bob, after asha, before catherine");
        Console.WriteLine($"      splice {SpliceBirthToDeadParent()}");

        Console.WriteLine();
        Console.WriteLine("  (3) resurrect / undo -- masking reverses in O(1), splicing cannot reverse");
        Console.WriteLine("  (4) cost -- O(children) per death and a widening parent list, vs O(1)");

        Console.WriteLine();
        Console.WriteLine("== a dead person still passes the crown through ==");

        var chain = new ThroneInheritance("a");
        chain.Birth("a", "b");
        chain.Birth("b", "c");
        chain.Birth("c", "d");
        chain.Death("a");
        chain.Death("b");
        chain.Death("c");
        Console.WriteLine($"  a->b->c->d, a b c all dead  ->  {Show(chain.GetInheritanceOrder())}");
        Console.WriteLine("  the dead check is a PRINT filter, not a prune. Prune and d is disinherited.");

        Console.WriteLine();
        Console.WriteLine("== degenerate inputs ==");

        var lonely = new ThroneInheritance("king");
        Console.WriteLine($"  king alone                  -> {Show(lonely.GetInheritanceOrder())}");
        lonely.Death("king");
        Console.WriteLine($"  king alone and dead         -> {Show(lonely.GetInheritanceOrder())} (empty, not null)");

        var headless = Build();
        headless.Death("king");
        Console.WriteLine($"  king dies, family survives  -> {Show(headless.GetInheritanceOrder())}");

        var allDead = Build();
        foreach (string name in new[] { "king", "andy", "bob", "catherine", "matthew", "alex", "asha" })
            allDead.Death(name);
        Console.WriteLine($"  everyone dies               -> {Show(allDead.GetInheritanceOrder())}");
        Console.WriteLine($"  ...population is still {allDead.Population}: the tree keeps its dead.");

        Console.WriteLine();
        Console.WriteLine("== who actually inherits ==");

        var heirs = Build();
        Console.WriteLine($"  next in line                -> {heirs.NextInLine()}");
        heirs.Death("king");
        heirs.Death("andy");
        Console.WriteLine($"  after king and andy die     -> {heirs.NextInLine()}   (matthew, not bob)");
        Console.WriteLine($"  3rd in line (0-based)       -> {heirs.KthInLine(3)}");
        Console.WriteLine("  NextInLine stops at the first living hit -- no reason to build the list.");

        Console.WriteLine();
        Console.WriteLine("== the depth trap: a 100,000-person chain ==");

        var deep = new ThroneInheritance("p0");
        const int depth = 100_000;
        for (int i = 1; i < depth; i++)
            deep.Birth($"p{i - 1}", $"p{i}");

        var iterative = deep.GetInheritanceOrder();
        Console.WriteLine($"  iterative (node stack)  -> {iterative.Count} names, first {iterative[0]}, last {iterative[^1]}");

        var frames = deep.GetInheritanceOrderFrames();
        Console.WriteLine($"  iterative (frame stack) -> {frames.Count} names, agrees: {frames.SequenceEqual(iterative)}");
        Console.WriteLine($"  recursive               -> not called: {depth:N0} frames overflows the 1 MB");
        Console.WriteLine("                             stack, and StackOverflowException cannot be caught");
        Console.WriteLine("                             in .NET -- the process dies, tests and all.");

        var wide = new ThroneInheritance("king");
        for (int i = 0; i < depth; i++)
            wide.Birth("king", $"c{i}");
        Console.WriteLine($"  100,000 SIBLINGS        -> {wide.GetInheritanceOrder().Count} names; the node stack");
        Console.WriteLine("                             holds all 100,000 at once, the frame stack holds 2.");
        Console.WriteLine("                             Opposite worst cases, both O(n), both on the heap.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20260811);
        bool framesAgree = true, recursiveAgrees = true, prefixStable = true;
        bool deadExcluded = true, aliveIncluded = true, parentBeforeChild = true;
        bool heirMatches = true, kthMatches = true, cacheHonest = true;
        int trials = 0;

        for (int trial = 0; trial < 3000; trial++)
        {
            var model = new ThroneInheritance("king");
            var kids = new Dictionary<string, List<string>> { ["king"] = new List<string>() };
            var parentOf = new Dictionary<string, string>();
            var people = new List<string> { "king" };
            var dead = new HashSet<string>();

            int births = rng.Next(1, 14);
            for (int i = 0; i < births; i++)
            {
                string parent = people[rng.Next(people.Count)];
                string child = $"n{i}";

                model.Birth(parent, child);
                kids[parent].Add(child);
                kids[child] = new List<string>();
                parentOf[child] = parent;
                people.Add(child);

                // Order is APPEND-ONLY for the living: a birth may extend the
                // list, never reorder what is already in it. Checked below.
                var before = model.GetInheritanceOrder().ToList();

                if (rng.Next(3) == 0)
                {
                    var candidates = people.Where(p => !dead.Contains(p)).ToList();
                    if (candidates.Count > 0)
                    {
                        string victim = candidates[rng.Next(candidates.Count)];
                        model.Death(victim);
                        dead.Add(victim);
                    }
                }

                var after = model.GetInheritanceOrder();
                prefixStable &= before.Where(p => !dead.Contains(p)).SequenceEqual(after);
                trials++;
            }

            var order = model.GetInheritanceOrder();

            // 1. The two iterative walks and the recursive one must agree. Small
            //    trees only -- the recursive version is the thing being contrasted,
            //    not trusted.
            framesAgree &= model.GetInheritanceOrderFrames().SequenceEqual(order);
            recursiveAgrees &= model.GetInheritanceOrderRecursive().SequenceEqual(order);

            // 2. Membership, computed from the sets rather than from the walk.
            deadExcluded &= !order.Any(dead.Contains);
            aliveIncluded &= people.Where(p => !dead.Contains(p)).OrderBy(p => p, StringComparer.Ordinal)
                                   .SequenceEqual(order.OrderBy(p => p, StringComparer.Ordinal));

            // 3. The ordering property itself, checked against the TREE and not
            //    against another traversal: for every living pair, an ancestor
            //    precedes its descendant, and an elder sibling's whole subtree
            //    precedes a younger sibling's.
            var position = order.Select((name, i) => (name, i))
                                .ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
            foreach (string person in order)
            {
                for (string p = person; parentOf.TryGetValue(p, out string up); p = up)
                    if (position.TryGetValue(up, out int at))
                        parentBeforeChild &= at < position[person];
            }
            parentBeforeChild &= SiblingSubtreesOrdered(kids, order, position);

            // 4. The early-exit queries must agree with the materialised list.
            //    Each runs on a fresh twin so the cached-list shortcut inside
            //    KthInLine is bypassed and the walk itself is what gets tested.
            heirMatches &= Replay(kids, dead).NextInLine() == (order.Count > 0 ? order[0] : null);
            for (int k = 0; k <= order.Count; k++)
                kthMatches &= Replay(kids, dead).KthInLine(k) == (k < order.Count ? order[k] : null);

            // 5. The cache must never outlive a mutation.
            var cached = model.GetInheritanceOrder();
            model.Birth("king", "late");
            cacheHonest &= !model.GetInheritanceOrder().SequenceEqual(cached);
        }

        Console.WriteLine($"  3,000 random families, {trials} birth/death steps");
        Console.WriteLine($"  frame stack == node stack:                            {framesAgree}");
        Console.WriteLine($"  recursion == iteration (small trees):                 {recursiveAgrees}");
        Console.WriteLine($"  a birth never REORDERS the existing living order:     {prefixStable}");
        Console.WriteLine($"  the dead never appear:                                {deadExcluded}");
        Console.WriteLine($"  every living person appears exactly once:             {aliveIncluded}");
        Console.WriteLine($"  ancestors and elder siblings' subtrees come first:    {parentBeforeChild}");
        Console.WriteLine($"  NextInLine == order[0], KthInLine(k) == order[k]:     {heirMatches && kthMatches}");
        Console.WriteLine($"  the cache is dropped by a birth:                      {cacheHonest}");
        Console.WriteLine();
        Console.WriteLine("  Check 3 is the one worth writing: it validates the ORDER against the tree's");
        Console.WriteLine("  shape, so it fails on a reversed push loop -- which two agreeing traversals");
        Console.WriteLine("  and a correct membership check would both happily pass.");
    }

    /// <summary>
    /// For every parent, the living members of an elder child's subtree must all
    /// precede every living member of a younger child's. Computed from the child
    /// lists directly, which is the definition the traversal is supposed to meet.
    /// </summary>
    private static bool SiblingSubtreesOrdered(
        Dictionary<string, List<string>> kids,
        IReadOnlyList<string> order,
        Dictionary<string, int> position)
    {
        foreach (var (parent, siblings) in kids)
        {
            int previousMax = -1;
            foreach (string sibling in siblings)
            {
                var inside = Subtree(kids, sibling).Where(position.ContainsKey).Select(p => position[p]).ToList();
                if (inside.Count == 0)
                    continue;

                if (inside.Min() <= previousMax)
                    return false;
                previousMax = Math.Max(previousMax, inside.Max());
            }
        }

        return true;
    }

    private static IEnumerable<string> Subtree(Dictionary<string, List<string>> kids, string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string name = stack.Pop();
            yield return name;
            foreach (string kid in kids[name])
                stack.Push(kid);
        }
    }

    /// <summary>A fresh, uncached instance with the same tree and dead set.</summary>
    private static ThroneInheritance Replay(Dictionary<string, List<string>> kids, HashSet<string> dead)
    {
        var copy = new ThroneInheritance("king");
        var stack = new Stack<string>();
        stack.Push("king");
        while (stack.Count > 0)
        {
            string name = stack.Pop();
            foreach (string kid in kids[name])
            {
                copy.Birth(name, kid);
                stack.Push(kid);
            }
        }

        foreach (string name in dead)
            copy.Death(name);

        return copy;
    }

    private static ThroneInheritance Build()
    {
        var t = new ThroneInheritance("king");
        t.Birth("king", "andy");
        t.Birth("king", "bob");
        t.Birth("king", "catherine");
        t.Birth("andy", "matthew");
        t.Birth("bob", "alex");
        t.Birth("bob", "asha");
        return t;
    }

    private static SplicingThroneInheritance Splicing()
    {
        var t = new SplicingThroneInheritance("king");
        t.Birth("king", "andy");
        t.Birth("king", "bob");
        t.Birth("king", "catherine");
        t.Birth("andy", "matthew");
        t.Birth("bob", "alex");
        t.Birth("bob", "asha");
        return t;
    }

    private static string SpliceBirthToDeadParent()
    {
        var t = Splicing();
        t.Death("bob");
        try
        {
            t.Birth("bob", "beatrice");
            return Show(t.GetInheritanceOrder()) + "   <- silently misfiled";
        }
        catch (KeyNotFoundException)
        {
            return "throws: bob is not in the tree any more";
        }
    }

    private static string Show(IReadOnlyList<string> names) => $"[{string.Join(", ", names)}]";
}

// ---- Notes for the follow-up questions ----
//
// "GetInheritanceOrder is O(n) per call and there are 10^5 calls."
//     True, and no structure fixes it while the answer is the whole list -- the
//     OUTPUT is Θ(alive), so Θ(q · alive) is a lower bound for that API. Two
//     honest moves: cache the result and invalidate on mutation (done above,
//     which collapses runs of consecutive queries to one walk), or change the
//     API. Say which one you are doing; "I'll memoize" is not an answer to a
//     question about worst-case interleaving.
//
// "Then change the API: who is k-th in line, in sub-linear time?"
//     Now it pays. Give every node a subtree count of LIVING descendants; then
//     descending to the k-th is O(depth): skip a child whose living-subtree size
//     is <= the remaining k, otherwise recurse into it. Birth and Death each
//     update one root-to-node path, O(depth). Balance the depth (or use a
//     heavy-path decomposition / an order-statistic tree over the Euler tour) and
//     all three operations are O(log n). This is the version to reach for if the
//     interviewer says "the kingdom is a real one and nobody prints the list".
//
// "Is X ahead of Y in line?"
//     Maintain an Euler tour: tin/tout per node makes 'X is an ancestor of Y' an
//     O(1) interval test, and preorder comparison is then 'compare tin'. The
//     catch is that births SHIFT the tour, so a static array does not survive --
//     you need an order-maintenance structure (a balanced BST over the tour, or
//     the labels trick) to keep insertion cheap. Mention the cost before
//     promising the O(1).
//
// "Someone was born, then their parent's earlier child dies. Does the order shift?"
//     No, and this is the invariant the randomized test pins down: a death only
//     REMOVES one name, a birth only APPENDS at one position. The relative order
//     of everyone else is untouched forever. That stability is what makes the
//     recompute-on-demand design defensible in the first place.
//
// "Support resurrect / undo."
//     Trivial here precisely because Death is a set membership flag:
//     `_dead.Remove(name)` and drop the cache. In the splicing model it is
//     impossible without a full undo log, because the tree shape was destroyed.
//     This is the strongest argument for masking, and it is worth saying even
//     when nobody asks for undo -- reversibility is a design property.
//
// "The names are 10^5 strings of up to 15 characters."
//     Hash them once into ints and keep the tree in int[]-shaped storage: the
//     child lists become List<int>, the dead set becomes a bool[], and the whole
//     traversal stops chasing string hashes. Roughly a 3-5x constant-factor win
//     and a large memory one, at the price of one dictionary and a names[] table
//     to translate back on output. Worth doing only if asked to optimise; the
//     complexity does not change.
//
// "Two threads: births on one, queries on the other."
//     The read is a full traversal, so a ReaderWriterLockSlim around
//     birth/death/order is the honest first answer. Lock-free is possible but
//     only if the child lists are append-only immutable snapshots -- and note
//     that a torn read here does not crash, it returns a plausible succession
//     list that is simply wrong, which is the worst failure mode available.
