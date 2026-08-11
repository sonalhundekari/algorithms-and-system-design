// Forest as a Parent Array -> Delete a Node
// Difficulty: Easy to state, Medium to get right
// Pattern: index remapping (old -> new) over a parent-pointer forest;
//          nearest-surviving-ancestor lift; subtree marking
//
// The encoding: parent[i] is node i's parent, and a ROOT stores ITSELF
// (parent[i] == i). That is the union-find representative convention, not the
// -1-for-root convention, and it is worth saying out loud because it removes the
// only sentinel value the array has -- every entry is a real index, so any bug in
// the rewrite produces another *structurally legal* array rather than a crash.
// Multiple roots are allowed: this is a FOREST, not a tree.
//
// THE QUESTION TO ASK BEFORE WRITING CODE (one sentence):
//
//     "When I delete a node, do its children become roots, do they attach to the
//      deleted node's parent, or does the whole subtree go with it?"
//
// All three are defensible and they return different arrays. This file
// implements all three (ChildRepair) and defaults to PromoteToRoot, which is the
// convention the problem statement usually names.
//
// THE ACTUAL DIFFICULTY is not traversal. The array shrinks, so every index
// above the deleted one MOVES, and each surviving node's stored parent is an
// index into the OLD array. Two rewrites have to happen to every entry:
//
//     1. repair   -- if my parent was deleted, who is my parent now?
//     2. reindex  -- whoever it is, what is their index in the SHORTER array?
//
// Do them in that order and in the right space. The classic bug is doing the
// repair after the shift, or shifting a freshly promoted root: a child promoted
// to "root" is written as its OWN index, and if the `if (p > deleted) p--` sweep
// then runs over that value, the node points at its old neighbour and you have
// silently rebuilt a different forest that still passes every validity check.
//
// The safe shape, and the one to write on the whiteboard:
//
//     oldToNew[i] = -1 for deleted nodes, else a running counter
//     for each survivor i:
//         p = parent[i]
//         if p was deleted:  p = repair(i)      // in OLD indices, -1 = "be a root"
//         out[oldToNew[i]] = p == -1 ? oldToNew[i] : oldToNew[p]
//
// For a single deletion oldToNew[i] is exactly `i - (i > deleted ? 1 : 0)` -- the
// "shift everything above down by one" rule, derived rather than assumed. Keep
// the map anyway: it costs one array, it generalises to deleting a whole subtree
// or a batch of nodes for free, and it is the version that stays correct when
// the interviewer adds "now delete these five".
//
// A parent may have a LARGER index than its child. Nothing in the encoding orders
// parents before children, so `parent = [2,2,2]` (root 2, children 0 and 1) is
// legal and is the input that catches anyone who assumed a topological layout.
//
// Cost: O(n) time, O(n) extra space, for any of the three policies and for any
// number of simultaneous deletions.

namespace CodingPatterns.Graphs;

/// <summary>
/// What happens to the direct children of a deleted node. There is no obvious
/// default -- ask before coding.
/// </summary>
public enum ChildRepair
{
    /// <summary>Each child becomes a root of its own tree (child stores itself).</summary>
    PromoteToRoot,

    /// <summary>
    /// Each child rises to the nearest ancestor that survived, becoming a root
    /// only if every ancestor was deleted. For one deletion this is "attach to
    /// the grandparent"; for a batch it keeps chaining upward.
    /// </summary>
    AttachToSurvivingAncestor,

    /// <summary>The node's entire subtree is deleted along with it.</summary>
    DeleteSubtree,
}

/// <summary>
/// The new forest plus the translation tables, because the caller almost always
/// holds data keyed by the OLD indices and has to move it too.
/// </summary>
/// <param name="Parent">The new parent array. Same encoding, valid indices.</param>
/// <param name="OldToNew">Old index -&gt; new index, or -1 if the node was deleted.</param>
/// <param name="NewToOld">New index -&gt; old index. Length is the new node count.</param>
public sealed record ForestDeletion(int[] Parent, int[] OldToNew, int[] NewToOld);

public static class ForestParentArrayDelete
{
    // --------------------------------------------------------------- the answer

    /// <summary>
    /// Delete one node and return the new parent array. The literal ask.
    /// </summary>
    public static int[] Delete(int[] parent, int index, ChildRepair repair = ChildRepair.PromoteToRoot)
        => DeleteMany(parent, new[] { index }, repair).Parent;

    /// <summary>
    /// Delete any set of nodes at once, with the translation tables. Batching is
    /// free here precisely because the index map is built from the survivor set
    /// rather than from "one node vanished, shift by one".
    ///
    /// Deleting nothing is legal and returns a copy. Deleting everything returns
    /// an empty array, which is a valid forest of zero trees.
    /// </summary>
    public static ForestDeletion DeleteMany(
        int[] parent, IEnumerable<int> indices, ChildRepair repair = ChildRepair.PromoteToRoot)
    {
        Validate(parent);
        int n = parent.Length;

        var removed = new bool[n];
        foreach (int i in indices ?? Enumerable.Empty<int>())
        {
            if (i < 0 || i >= n)
                throw new ArgumentOutOfRangeException(nameof(indices), $"node {i} is not in [0, {n})");
            removed[i] = true;                   // duplicates are harmless
        }

        if (repair == ChildRepair.DeleteSubtree)
            MarkDescendants(parent, removed);

        // 1. old -> new. Survivors keep their relative order, so this is the
        //    "shift down past every deleted index" rule, computed once.
        var oldToNew = new int[n];
        int kept = 0;
        for (int i = 0; i < n; i++)
            oldToNew[i] = removed[i] ? -1 : kept++;

        var newToOld = new int[kept];
        for (int i = 0; i < n; i++)
            if (!removed[i])
                newToOld[oldToNew[i]] = i;

        // 2. Only AttachToSurvivingAncestor needs to look further than one hop.
        int[]? lift = repair == ChildRepair.AttachToSurvivingAncestor
            ? BuildLift(parent, removed)
            : null;

        // 3. Rewrite every survivor's parent: repair first, in OLD indices, then
        //    translate. -1 means "you are a root now", which in this encoding is
        //    spelled as the node's own NEW index -- never its old one.
        var result = new int[kept];
        for (int i = 0; i < n; i++)
        {
            if (removed[i])
                continue;

            int p = parent[i];
            if (removed[p])
            {
                p = repair switch
                {
                    ChildRepair.PromoteToRoot => -1,
                    ChildRepair.AttachToSurvivingAncestor => lift![p],
                    // Survivors never point into a deleted subtree, so this arm
                    // is unreachable -- but a wrong MarkDescendants would make it
                    // reachable, and silence is the failure mode to avoid.
                    ChildRepair.DeleteSubtree =>
                        throw new InvalidOperationException($"node {i} survived under deleted parent {p}"),
                    _ => throw new ArgumentOutOfRangeException(nameof(repair)),
                };
            }

            result[oldToNew[i]] = p == -1 ? oldToNew[i] : oldToNew[p];
        }

        return new ForestDeletion(result, oldToNew, newToOld);
    }

    /// <summary>
    /// lift[v] = the nearest node on v's own chain (v, parent[v], ...) that
    /// survives, or -1 if the chain reaches a deleted root without finding one.
    ///
    /// Written as an iterative climb with memoisation rather than recursion: the
    /// input may be a 100,000-node chain, and this is the one place in the file
    /// that would otherwise recurse to that depth. Each node is pushed on the
    /// path at most once across the whole loop, so the total is O(n).
    /// </summary>
    private static int[] BuildLift(int[] parent, bool[] removed)
    {
        int n = parent.Length;
        var lift = new int[n];
        Array.Fill(lift, Unknown);

        var path = new List<int>();

        for (int start = 0; start < n; start++)
        {
            if (lift[start] != Unknown)
                continue;

            path.Clear();
            int v = start;
            int answer;

            while (true)
            {
                if (lift[v] != Unknown) { answer = lift[v]; break; }
                if (!removed[v]) { answer = v; break; }
                if (parent[v] == v) { answer = -1; break; }   // deleted root: chain ends

                path.Add(v);
                v = parent[v];                                // Validate() ruled out cycles
            }

            lift[v] = answer;
            foreach (int u in path)
                lift[u] = answer;                             // one linear chain, one answer
        }

        return lift;
    }

    private const int Unknown = -2;

    /// <summary>
    /// Extend <paramref name="removed"/> to every descendant of a removed node,
    /// same climb-and-memoise trick: a node is doomed exactly when its parent
    /// chain hits a removed node before it hits a root.
    /// </summary>
    private static void MarkDescendants(int[] parent, bool[] removed)
    {
        const byte unknown = 0, doomed = 1, safe = 2;

        int n = parent.Length;
        var state = new byte[n];
        var path = new List<int>();

        for (int start = 0; start < n; start++)
        {
            if (state[start] != unknown)
                continue;

            path.Clear();
            int v = start;
            byte answer;

            while (true)
            {
                if (state[v] != unknown) { answer = state[v]; break; }
                if (removed[v]) { answer = doomed; break; }
                if (parent[v] == v) { answer = safe; break; }

                path.Add(v);
                v = parent[v];
            }

            state[v] = answer;
            foreach (int u in path)
                state[u] = answer;
        }

        for (int i = 0; i < n; i++)
            if (state[i] == doomed)
                removed[i] = true;
    }

    // ------------------------------------------------------------- validation

    /// <summary>
    /// Every entry in range, and no cycle other than the self-loops that mark
    /// roots. The problem promises valid input; this exists so the tests can
    /// assert the OUTPUT is valid, which is the actual deliverable.
    /// </summary>
    public static void Validate(int[] parent)
    {
        if (parent is null)
            throw new ArgumentNullException(nameof(parent));

        const byte unseen = 0, onPath = 1, settled = 2;

        int n = parent.Length;
        for (int i = 0; i < n; i++)
            if (parent[i] < 0 || parent[i] >= n)
                throw new ArgumentOutOfRangeException(
                    nameof(parent), $"node {i} points at {parent[i]}, outside [0, {n})");

        var state = new byte[n];
        var path = new List<int>();

        for (int start = 0; start < n; start++)
        {
            if (state[start] != unseen)
                continue;

            path.Clear();
            int v = start;

            while (state[v] == unseen && parent[v] != v)
            {
                state[v] = onPath;
                path.Add(v);
                v = parent[v];
            }

            if (state[v] == onPath)
                throw new ArgumentException($"parent pointers cycle through node {v}", nameof(parent));

            foreach (int u in path)
                state[u] = settled;
            state[v] = settled;
        }
    }

    /// <summary>Non-throwing <see cref="Validate"/>, for assertions.</summary>
    public static bool IsValid(int[] parent)
    {
        try
        {
            Validate(parent);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Bracket sketch of the forest, roots in index order: "0(1(3,4),2) 5(6)".
    /// Cheap, and it turns "verify the shifted indices by hand" into reading one
    /// line instead of squinting at two arrays.
    /// </summary>
    public static string Sketch(int[] parent)
    {
        Validate(parent);
        int n = parent.Length;
        if (n == 0)
            return "(empty)";

        var children = new List<int>[n];
        for (int i = 0; i < n; i++)
            children[i] = new List<int>();

        var roots = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (parent[i] == i)
                roots.Add(i);
            else
                children[parent[i]].Add(i);
        }

        string Walk(int v) =>
            children[v].Count == 0 ? v.ToString() : $"{v}({string.Join(",", children[v].Select(Walk))})";

        return string.Join("  ", roots.Select(Walk));
    }

    // ------------------------------------------------------------------ tests

    public static void Run()
    {
        // 0(1(3,4),2)  5(6)
        var forest = new[] { 0, 0, 0, 1, 1, 5, 5 };

        Console.WriteLine("== the encoding ==");
        Console.WriteLine($"  parent = [{string.Join(",", forest)}]");
        Console.WriteLine($"  forest = {Sketch(forest)}");
        Console.WriteLine("  roots store themselves (parent[0]=0, parent[5]=5), so there is no");
        Console.WriteLine("  sentinel: a wrong rewrite yields another legal-looking forest.");

        Console.WriteLine();
        Console.WriteLine("== the clarifying question, answered three ways: delete node 1 ==");
        Console.WriteLine("  node 1 is an interior node with children {3,4} and parent 0.");
        foreach (var repair in Enum.GetValues<ChildRepair>())
        {
            var after = Delete(forest, 1, repair);
            Console.WriteLine($"  {repair,-26} -> [{string.Join(",", after)}]   {Sketch(after)}");
        }
        Console.WriteLine("    PromoteToRoot             : 3 and 4 head their own trees.");
        Console.WriteLine("    AttachToSurvivingAncestor : 3 and 4 are adopted by 0.");
        Console.WriteLine("    DeleteSubtree             : 3 and 4 go too, so the array loses 3 slots.");
        Console.WriteLine("  Three different arrays from the same input. Ask first.");

        Console.WriteLine();
        Console.WriteLine("== the hand-check: delete a ROOT with two children ==");

        var afterRoot = DeleteMany(forest, new[] { 0 });
        Console.WriteLine($"  before [{string.Join(",", forest)}]  {Sketch(forest)}");
        Console.WriteLine($"  delete 0");
        Console.WriteLine($"  after  [{string.Join(",", afterRoot.Parent)}]  {Sketch(afterRoot.Parent)}");
        Console.WriteLine("  old -> new: " + string.Join(", ",
            Enumerable.Range(0, forest.Length).Select(i =>
                $"{i}->{(afterRoot.OldToNew[i] < 0 ? "x" : afterRoot.OldToNew[i].ToString())}")));
        Console.WriteLine("  every index dropped by exactly one, 1 and 2 became roots (new 0 and 1),");
        Console.WriteLine("  3 and 4 still hang off 1 (now new 0), and 6 still hangs off 5 (now new 4).");

        Console.WriteLine();
        Console.WriteLine("== a parent whose index is GREATER than the deleted node's ==");

        var inverted = new[] { 2, 2, 2 };        // root 2, children 0 and 1
        Console.WriteLine($"  parent = [{string.Join(",", inverted)}]  ->  {Sketch(inverted)}");
        var afterInverted = Delete(inverted, 0);
        Console.WriteLine($"  delete 0 -> [{string.Join(",", afterInverted)}]  {Sketch(afterInverted)}");
        Console.WriteLine("  node 1's parent was 2 and is now 1: the pointer shifted DOWN even though");
        Console.WriteLine("  it points upward in the tree. Nothing orders parents before children.");

        Console.WriteLine();
        Console.WriteLine("== degenerate inputs ==");

        var single = new[] { 0 };
        var chain = new[] { 0, 0, 1, 2 };        // 0 -> 1 -> 2 -> 3
        var flat = new[] { 0, 1, 2 };            // three isolated roots

        Console.WriteLine($"  single node, delete it        -> [{string.Join(",", Delete(single, 0))}] (empty forest, still valid)");
        Console.WriteLine($"  delete a leaf (3 of the chain)-> [{string.Join(",", Delete(chain, 3))}]  {Sketch(Delete(chain, 3))}");
        Console.WriteLine($"  three roots, delete the middle-> [{string.Join(",", Delete(flat, 1))}]  {Sketch(Delete(flat, 1))}");
        Console.WriteLine($"  delete every node             -> [{string.Join(",", DeleteMany(forest, Enumerable.Range(0, 7)).Parent)}] (length 0)");
        Console.WriteLine($"  delete nothing                -> [{string.Join(",", DeleteMany(forest, Array.Empty<int>()).Parent)}] (unchanged copy)");

        var chainInterior = Delete(chain, 1);
        Console.WriteLine($"  chain 0->1->2->3, delete 1, promote -> [{string.Join(",", chainInterior)}]  {Sketch(chainInterior)}");
        Console.WriteLine($"  ...same deletion, attach instead    -> " +
                          $"[{string.Join(",", Delete(chain, 1, ChildRepair.AttachToSurvivingAncestor))}]  " +
                          $"{Sketch(Delete(chain, 1, ChildRepair.AttachToSurvivingAncestor))}");

        Console.WriteLine();
        Console.WriteLine("== batching: the index map generalises, the shift-by-one rule does not ==");

        var batch = DeleteMany(forest, new[] { 0, 5 }, ChildRepair.AttachToSurvivingAncestor);
        Console.WriteLine($"  delete {{0,5}} (both roots), attach -> [{string.Join(",", batch.Parent)}]  {Sketch(batch.Parent)}");
        Console.WriteLine("  1, 2 and 6 had no surviving ancestor left, so they became roots; 3 and 4");
        Console.WriteLine("  climbed only as far as 1, which survived.");

        var subtree = DeleteMany(forest, new[] { 1 }, ChildRepair.DeleteSubtree);
        Console.WriteLine($"  delete subtree of 1 -> [{string.Join(",", subtree.Parent)}], survivors " +
                          $"{{{string.Join(",", subtree.NewToOld)}}} (old indices)");

        Console.WriteLine();
        Console.WriteLine("== rejected inputs ==");

        foreach (var bad in new[] { new[] { 0, 5 }, new[] { 1, 0 }, new[] { 0, 2, 3, 1 } })
        {
            try
            {
                Validate(bad);
                Console.WriteLine($"  [{string.Join(",", bad)}] accepted -- BUG");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  [{string.Join(",", bad)}] rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }
        Console.WriteLine("    ^ [1,0] is the trap: both entries are in range and every node has a");
        Console.WriteLine("      parent, so it is only wrong because nobody is a root.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20250810);
        bool outputValid = true, sizeRight = true, shiftIdentity = true;
        bool ancestorsPreserved = true, promotionCorrect = true, rootCountRight = true;
        bool subtreeRight = true, batchMatchesSequential = true;
        int trials = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            int n = rng.Next(1, 10);

            // Random forest. Node i either roots its own tree or picks any EARLIER
            // node as parent -- then the labels are shuffled, so parents land above
            // and below their children and the "already topological" assumption
            // never accidentally holds.
            var parent = RandomForest(rng, n);
            int victim = rng.Next(n);

            foreach (var repair in Enum.GetValues<ChildRepair>())
            {
                trials++;

                var result = DeleteMany(parent, new[] { victim }, repair);
                var kept = result.NewToOld;

                // 1. The output is a legal forest in the same encoding. This is
                //    the whole deliverable and it is not automatic: an entry that
                //    forgot to shift is still "in range" whenever it is small.
                outputValid &= IsValid(result.Parent);

                // 2. Size, and the shift-by-one identity for the single-node cases.
                if (repair == ChildRepair.DeleteSubtree)
                {
                    var doomed = Subtree(parent, victim);
                    sizeRight &= result.Parent.Length == n - doomed.Count;
                    subtreeRight &= kept.All(v => !doomed.Contains(v));
                }
                else
                {
                    sizeRight &= result.Parent.Length == n - 1;
                    for (int i = 0; i < n; i++)
                        if (i != victim)
                            shiftIdentity &= result.OldToNew[i] == i - (i > victim ? 1 : 0);
                }

                // 3. The structural oracle, computed the slow O(n^2) way from
                //    ancestor SETS rather than from the same index arithmetic the
                //    implementation uses.
                //
                //    Attach / DeleteSubtree preserve the ancestor relation exactly
                //    among survivors. Promote additionally cuts it whenever the
                //    deleted node lies strictly between the two.
                for (int a = 0; a < kept.Length; a++)
                {
                    for (int b = 0; b < kept.Length; b++)
                    {
                        bool now = IsAncestor(result.Parent, ancestor: b, node: a);
                        bool before = IsAncestor(parent, ancestor: kept[b], node: kept[a]);

                        bool expected = repair == ChildRepair.PromoteToRoot
                            ? before && !PathCrosses(parent, kept[a], kept[b], victim)
                            : before;

                        if (repair == ChildRepair.PromoteToRoot)
                            promotionCorrect &= now == expected;
                        else
                            ancestorsPreserved &= now == expected;
                    }
                }

                // 4. Root bookkeeping, counted independently of the rewrite.
                int rootsBefore = Enumerable.Range(0, n).Count(i => parent[i] == i);
                int rootsAfter = Enumerable.Range(0, kept.Length).Count(i => result.Parent[i] == i);
                int victimChildren = Enumerable.Range(0, n).Count(i => i != victim && parent[i] == victim);

                rootCountRight &= repair switch
                {
                    ChildRepair.PromoteToRoot =>
                        rootsAfter == rootsBefore - (parent[victim] == victim ? 1 : 0) + victimChildren,
                    ChildRepair.AttachToSurvivingAncestor =>
                        rootsAfter == rootsBefore - (parent[victim] == victim ? 1 : 0)
                                    + (parent[victim] == victim ? victimChildren : 0),
                    _ => rootsAfter == rootsBefore - (parent[victim] == victim ? 1 : 0),
                };
            }

            // 5. Deleting a batch in one call == deleting the same nodes one at a
            //    time, translating the next victim through each map. If these ever
            //    disagree, the index map is the thing that is wrong.
            var victims = Enumerable.Range(0, n).Where(_ => rng.Next(3) == 0).ToArray();
            var atOnce = DeleteMany(parent, victims, ChildRepair.PromoteToRoot).Parent;

            var running = (int[])parent.Clone();
            var alive = Enumerable.Range(0, n).ToList();
            foreach (int v in victims.OrderByDescending(v => v))
            {
                int at = alive.IndexOf(v);
                running = Delete(running, at, ChildRepair.PromoteToRoot);
                alive.RemoveAt(at);
            }

            batchMatchesSequential &= atOnce.SequenceEqual(running);
        }

        Console.WriteLine($"  4,000 random forests, {trials} (forest, victim, policy) cases");
        Console.WriteLine($"  output is a valid parent array (in range, no cycles):  {outputValid}");
        Console.WriteLine($"  new length is right for every policy:                  {sizeRight}");
        Console.WriteLine($"  oldToNew == 'shift indices above the victim down 1':   {shiftIdentity}");
        Console.WriteLine($"  attach/subtree preserve the ancestor relation:         {ancestorsPreserved}");
        Console.WriteLine($"  promote cuts exactly the paths through the victim:     {promotionCorrect}");
        Console.WriteLine($"  root count matches an independent tally:               {rootCountRight}");
        Console.WriteLine($"  subtree policy removes exactly the subtree:            {subtreeRight}");
        Console.WriteLine($"  one batched call == repeated single deletions:         {batchMatchesSequential}");
        Console.WriteLine();
        Console.WriteLine("  The ancestor-set checks are the ones worth writing in an interview: they");
        Console.WriteLine("  are computed from the forest's SHAPE, so they fail on exactly the bugs a");
        Console.WriteLine("  'the array is the right length and every entry is in range' check misses.");
    }

    /// <summary>Test helper: a random forest whose labels are in no useful order.</summary>
    private static int[] RandomForest(Random rng, int n)
    {
        var shape = new int[n];
        for (int i = 0; i < n; i++)
            shape[i] = rng.Next(3) == 0 ? i : rng.Next(i + 1);   // i, or any earlier node

        var label = Enumerable.Range(0, n).ToArray();
        for (int i = n - 1; i > 0; i--)                          // Fisher-Yates
        {
            int j = rng.Next(i + 1);
            (label[i], label[j]) = (label[j], label[i]);
        }

        var parent = new int[n];
        for (int i = 0; i < n; i++)
            parent[label[i]] = label[shape[i]];

        return parent;
    }

    /// <summary>Test helper: is <paramref name="ancestor"/> a strict ancestor of <paramref name="node"/>?</summary>
    private static bool IsAncestor(int[] parent, int ancestor, int node)
    {
        for (int v = node; parent[v] != v; v = parent[v])
            if (parent[v] == ancestor)
                return true;

        return false;
    }

    /// <summary>
    /// Test helper: climbing from <paramref name="node"/> up to
    /// <paramref name="ancestor"/>, does the path pass through
    /// <paramref name="mark"/> strictly between them?
    /// </summary>
    private static bool PathCrosses(int[] parent, int node, int ancestor, int mark)
    {
        for (int v = parent[node]; v != ancestor; v = parent[v])
        {
            if (v == mark)
                return true;
            if (parent[v] == v)
                return false;                    // hit a root without reaching `ancestor`
        }

        return false;
    }

    /// <summary>Test helper: the set of old indices in <paramref name="root"/>'s subtree.</summary>
    private static HashSet<int> Subtree(int[] parent, int root)
    {
        var inside = new HashSet<int> { root };
        for (int i = 0; i < parent.Length; i++)
        {
            for (int v = i; ; v = parent[v])
            {
                if (v == root) { inside.Add(i); break; }
                if (parent[v] == v) break;
            }
        }

        return inside;
    }
}

// ---- Notes for the follow-up questions ----
//
// "The nodes carry data / names, not just indices."
//     This is why DeleteMany hands back OldToNew and NewToOld. The parent array
//     is only half the structure; any parallel array (labels, weights, payloads)
//     has to be permuted by the SAME map, and `newData[i] = oldData[NewToOld[i]]`
//     is the one-liner that does it. Volunteer this -- an interviewer who asked
//     for "the array" usually has a second array in mind.
//
// "Do it in place / without the extra map."
//     Possible for a single deletion: copy survivors down over the hole, then
//     fix each entry with `p -= p > victim ? 1 : 0` AFTER repairing deleted
//     parents to self-references, and remember that a self-reference written
//     before the shift must not be shifted. That ordering hazard is the whole
//     reason the map version exists; it is O(n) either way, so the map costs one
//     array and buys correctness under batching.
//
// "Deleting is one operation -- support insert too, and keep them cheap."
//     Reindexing on every delete is O(n) per operation. If deletes are frequent,
//     stop compacting: keep a free list and an `alive[]` flag, let indices be
//     stable, and compact lazily when the array is half holes. Then "delete" is
//     O(1) amortised for PromoteToRoot and the array is no longer dense -- which
//     is a different contract, so it has to be agreed, not assumed.
//
// "Is this union-find?"
//     The encoding is (parent[i] == i marks a representative) but the semantics
//     are not: DSU's parent pointers are compressible because only the ROOT of
//     each set matters, while here the exact tree shape is the data. Never path-
//     compress this array -- it would flatten every tree into a star and destroy
//     the structure the caller is storing. Worth saying if the interviewer nudges
//     toward "can't you just find(i)?".
//
// "n is 10^6 and the deletes come in a stream."
//     Batch them. Collecting deletions and applying one DeleteMany is O(n) total
//     instead of O(n) each; the code above already accepts a set, which is the
//     reason to write it that way even when the question asks for one node.
