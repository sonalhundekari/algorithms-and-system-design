// Tree Node Deletion -> Max Height  (+ min deletions to force height <= k)
// Difficulty: Medium base case, Hard follow-ups
// Pattern: post-order DFS on an N-ary tree; tree DP over a height budget;
//          bottom-up greedy; subset brute force as the oracle
//
// The base question sounds like "height of a tree" and is not. Deleting a node
// does not delete its subtree -- the children are PROMOTED to the nearest
// surviving ancestor, and consecutive deletions keep promoting upward. Deleting
// the root does not empty the tree either; it splits it into a FOREST, and the
// answer becomes the tallest tree in that forest.
//
// One recurrence handles all of it. Define, for every node, the height in LEVELS
// of everything in its subtree measured from the frame it hangs at:
//
//     Contrib(v) = 1 + max(Contrib(c) for c in children)     if v survives
//     Contrib(v) =     max(Contrib(c) for c in children)     if v is deleted
//                                                            (empty max = 0)
//
// A deleted node passes its children's height through WITHOUT adding a level --
// that single "no +1" is the whole promotion rule. And because a deleted root
// also just passes through, Contrib(root) is already the max over the forest's
// trees. No special case for a deleted root, no separate forest pass:
//
//     post-deletion height, in LEVELS = Contrib(root)      (0 = nothing survives)
//
// THE UNITS ARE A REAL TRAP HERE. The N-ary phrasing of this question counts
// EDGES ("1 -> 3 -> 6 -> 7 is height 3"), the binary phrasing counts LEVELS
// ("[1,2,3] with node 1 deleted has height 1"). They differ by one on every
// non-empty tree, so the same code returns 3 or 4 for the same input depending
// on a convention nobody states. HeightMeasure makes the caller say which, and
// everything internal works in levels because levels is the measure that stays
// sane when the forest is empty. Ask which one before writing the recurrence.
//
// The pieces and their costs, n nodes and a budget of k:
//
//   MaxHeight                one recursive post-order DFS           O(n)
//   MaxHeightIterative       same, explicit stack, no recursion     O(n)
//   Components               the surviving forest, tree by tree     O(n)
//   MinDeletions / BestPlan  tree DP over (node, remaining budget)  O(n * k)
//   MinDeletionsGreedy       bottom-up, delete only when forced     O(n)
//   BestPlanBruteForce       every subset -- the oracle, n <= 22    O(2^n * n)
//
// The reformulation that makes the follow-ups easy: promotion never reorders
// ancestors, so a root-to-node path in the final forest is exactly the set of
// surviving nodes on some root-to-leaf path of the ORIGINAL tree. Therefore
//
//     height <= k levels  <=>  every root-to-leaf path keeps at most k nodes
//
// and "minimum deletions" is just "n - (largest set of nodes that keeps at most
// k per root-to-leaf path)". Say that sentence out loud in the interview and
// both follow-ups stop being about trees at all.
//
// Follow-up 2 (prefer deleting deeper) is NOT free. The greedy deletes ancestors
// and keeps the deep nodes, which is the exact opposite of what the tiebreak
// wants -- on a 3-chain with k = 1 edge the greedy deletes the root (depth sum
// 0) while an equally small solution deletes the leaf (depth sum 2). That is why
// the tiebreak rides on the DP and not on the greedy.

namespace CodingPatterns.Graphs;

/// <summary>How the caller counts height. There is no sensible default, so ask.</summary>
public enum HeightMeasure
{
    /// <summary>Number of edges on the longest path: a lone node is 0.</summary>
    Edges,

    /// <summary>Number of levels: a lone node is 1, an empty forest is 0.</summary>
    Levels,
}

/// <summary>
/// A minimum-deletion solution: how many nodes, the sum of their depths (the
/// follow-up 2 tiebreak), and which nodes, by label, sorted.
/// </summary>
public sealed record DeletionPlan(int Deletions, long DepthSum, IReadOnlyList<int> Deleted);

/// <summary>
/// An N-ary rooted tree with integer labels, built once from whichever input
/// shape the interviewer hands over. Everything downstream works on the internal
/// indices 0..Count-1 and only translates back to labels at the boundary.
/// </summary>
public sealed class RootedTree
{
    private readonly Dictionary<int, int> _index;

    private RootedTree(int[] label, List<int>[] children, int root)
    {
        Label = label;
        Children = children;
        Root = root;
        Count = label.Length;

        _index = new Dictionary<int, int>(Count);
        for (int i = 0; i < Count; i++)
            _index[label[i]] = i;

        Parent = new int[Count];
        Depth = new int[Count];
        BottomUp = new int[Count];

        if (Count == 0)
        {
            Root = -1;
            return;
        }

        // Reverse pre-order. A pre-order emits every parent before its children,
        // so walking it backwards visits every child before its parent -- which
        // is all any of the bottom-up passes below actually need. Built with an
        // explicit stack so a 100,000-node chain is a non-event.
        var order = new int[Count];
        var seenNode = new bool[Count];
        var stack = new Stack<int>();

        Parent[root] = -1;
        Depth[root] = 0;
        stack.Push(root);
        int seen = 0;

        while (stack.Count > 0)
        {
            int v = stack.Pop();
            if (seenNode[v])
                throw new ArgumentException($"node {label[v]} is reachable twice -- this is not a tree");

            seenNode[v] = true;
            order[seen++] = v;

            foreach (int c in children[v])
            {
                Parent[c] = v;
                Depth[c] = Depth[v] + 1;
                stack.Push(c);
            }
        }

        if (seen != Count)
            throw new ArgumentException("the input is not connected: some node is unreachable from the root");

        for (int i = 0; i < Count; i++)
            BottomUp[i] = order[Count - 1 - i];
    }

    /// <summary>Number of nodes. Zero is a legal tree and every method handles it.</summary>
    public int Count { get; }

    /// <summary>Index of the root, or -1 when the tree is empty.</summary>
    public int Root { get; }

    /// <summary>Caller-facing id of each index.</summary>
    public int[] Label { get; }

    /// <summary>Child indices, in input order.</summary>
    public List<int>[] Children { get; }

    /// <summary>Parent index, -1 at the root.</summary>
    public int[] Parent { get; }

    /// <summary>Depth in the ORIGINAL tree, root = 0. Deletion never changes it.</summary>
    public int[] Depth { get; }

    /// <summary>Every index, children strictly before their parent.</summary>
    public int[] BottomUp { get; }

    public int IndexOf(int label) =>
        _index.TryGetValue(label, out int i)
            ? i
            : throw new ArgumentException($"unknown node label {label}", nameof(label));

    /// <summary>Turn a set of labels into the per-index flag array the DFS wants.</summary>
    public bool[] Mark(IEnumerable<int> deletedLabels)
    {
        var deleted = new bool[Count];
        foreach (int label in deletedLabels ?? Enumerable.Empty<int>())
            deleted[IndexOf(label)] = true;      // duplicates are harmless
        return deleted;
    }

    // ------------------------------------------------------------- factories

    /// <summary>
    /// Parent-pointer form: parent[i] is i's parent, or -1 for the root. Labels
    /// are the indices themselves. Exactly one -1 is required; a parent cycle
    /// shows up as an unreachable node, because a node with a parent can never
    /// be the root of the traversal.
    /// </summary>
    public static RootedTree FromParents(int[] parent)
    {
        parent ??= Array.Empty<int>();
        int n = parent.Length;

        var label = new int[n];
        var children = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            label[i] = i;
            children[i] = new List<int>();
        }

        int root = -1;
        for (int i = 0; i < n; i++)
        {
            if (parent[i] == -1)
            {
                if (root != -1)
                    throw new ArgumentException($"two roots: {root} and {i}", nameof(parent));
                root = i;
                continue;
            }

            if (parent[i] < 0 || parent[i] >= n)
                throw new ArgumentOutOfRangeException(nameof(parent), $"node {i} points at {parent[i]}");
            if (parent[i] == i)
                throw new ArgumentException($"node {i} is its own parent", nameof(parent));

            children[parent[i]].Add(i);
        }

        if (n > 0 && root == -1)
            throw new ArgumentException("no root: every node has a parent, so the pointers form a cycle", nameof(parent));

        return new RootedTree(label, children, root);
    }

    /// <summary>
    /// Edge-list form. Edges are treated as UNDIRECTED and the root is named
    /// separately, which is the shape that survives an interviewer who hands
    /// over pairs in whatever order they thought of them. Node labels are
    /// whatever ints appear in the edges (plus the root), so a single-node tree
    /// is FromEdges(no edges, 7).
    /// </summary>
    public static RootedTree FromEdges(int[][] edges, int rootLabel)
    {
        edges ??= Array.Empty<int[]>();

        var index = new Dictionary<int, int>();
        var labels = new List<int>();

        int Intern(int value)
        {
            if (index.TryGetValue(value, out int i))
                return i;
            index[value] = labels.Count;
            labels.Add(value);
            return labels.Count - 1;
        }

        Intern(rootLabel);                       // index 0, so the root always exists

        var pairs = new List<(int A, int B)>(edges.Length);
        foreach (var edge in edges)
        {
            if (edge is null || edge.Length != 2)
                throw new ArgumentException("every edge must be a pair", nameof(edges));
            if (edge[0] == edge[1])
                throw new ArgumentException($"self-loop at node {edge[0]}", nameof(edges));

            pairs.Add((Intern(edge[0]), Intern(edge[1])));
        }

        int n = labels.Count;
        if (pairs.Count != n - 1)
            throw new ArgumentException(
                $"a tree over {n} nodes needs exactly {n - 1} edges, got {pairs.Count}", nameof(edges));

        var adjacency = new List<int>[n];
        for (int i = 0; i < n; i++)
            adjacency[i] = new List<int>();
        foreach (var (a, b) in pairs)
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        // Orient away from the root. BFS rather than DFS only so that children
        // come out in input order, which keeps the demos readable.
        var children = new List<int>[n];
        for (int i = 0; i < n; i++)
            children[i] = new List<int>();

        var visited = new bool[n];
        var queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);

        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            foreach (int u in adjacency[v])
            {
                if (visited[u])
                    continue;
                visited[u] = true;
                children[v].Add(u);
                queue.Enqueue(u);
            }
        }

        return new RootedTree(labels.ToArray(), children, 0);
    }

    /// <summary>
    /// LeetCode level-order array for the BINARY variant: nulls are absent
    /// children and the children of a null are not listed at all. Values double
    /// as the unique node ids the binary phrasing of this question assumes.
    /// </summary>
    public static RootedTree FromBinaryLevelOrder(int?[] levelOrder)
    {
        if (levelOrder is null || levelOrder.Length == 0 || levelOrder[0] is null)
            return new RootedTree(Array.Empty<int>(), Array.Empty<List<int>>(), -1);

        var edges = new List<int[]>();
        var parents = new Queue<int>();
        parents.Enqueue(levelOrder[0].Value);

        int at = 1;
        while (parents.Count > 0 && at < levelOrder.Length)
        {
            int parent = parents.Dequeue();

            for (int side = 0; side < 2 && at < levelOrder.Length; side++, at++)
            {
                if (levelOrder[at] is not int child)
                    continue;                    // null: no node on this side

                edges.Add(new[] { parent, child });
                parents.Enqueue(child);
            }
        }

        return FromEdges(edges.ToArray(), levelOrder[0].Value);
    }
}

public static class TreeDeletionMaxHeight
{
    // ------------------------------------------------------------- base case
    //
    // "Deleted nodes are skipped; their children rise to the nearest surviving
    // ancestor." One post-order pass, and the only line that matters is whether
    // the node adds a level to what it passes upward.

    /// <summary>
    /// Post-deletion height. The whole base question, four lines of recursion
    /// plus a unit conversion.
    ///
    /// Empty tree -> 0. Every node deleted -> 0 in both measures (nothing
    /// survives, so there is no "one level" to report). Root deleted -> the
    /// tallest tree of the resulting forest, which falls out of the recurrence
    /// rather than needing a case.
    /// </summary>
    public static int MaxHeight(
        RootedTree tree, IEnumerable<int> deleted, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));
        if (tree.Count == 0)
            return 0;

        return ToMeasure(Contrib(tree, tree.Mark(deleted), tree.Root), measure);
    }

    /// <summary>
    /// The recurrence itself. Deleted nodes hand their children's height up
    /// unchanged; survivors add one level. O(n) time, O(h) stack.
    /// </summary>
    private static int Contrib(RootedTree tree, bool[] deleted, int v)
    {
        int tallest = 0;
        foreach (int c in tree.Children[v])
            tallest = Math.Max(tallest, Contrib(tree, deleted, c));

        return deleted[v] ? tallest : tallest + 1;
    }

    /// <summary>
    /// Same answer without recursion: children already sit before their parents
    /// in <see cref="RootedTree.BottomUp"/>, so one linear sweep fills the table.
    /// Worth having ready -- "what if the tree is a 100,000-node chain?" is the
    /// standard nudge, and the recursive version dies on it.
    /// </summary>
    public static int MaxHeightIterative(
        RootedTree tree, IEnumerable<int> deleted, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));
        if (tree.Count == 0)
            return 0;

        return ToMeasure(ContribAll(tree, tree.Mark(deleted))[tree.Root], measure);
    }

    /// <summary>Contrib for every node at once, bottom-up, no recursion. O(n).</summary>
    private static int[] ContribAll(RootedTree tree, bool[] deleted)
    {
        var contrib = new int[tree.Count];

        foreach (int v in tree.BottomUp)
        {
            int tallest = 0;
            foreach (int c in tree.Children[v])
                tallest = Math.Max(tallest, contrib[c]);

            contrib[v] = deleted[v] ? tallest : tallest + 1;
        }

        return contrib;
    }

    /// <summary>
    /// The surviving forest, one entry per tree: the label of its root and its
    /// height. A node roots a tree exactly when it survives and every one of its
    /// ancestors was deleted -- which is why deleting the root is not a special
    /// case anywhere else, it just produces more than one entry here.
    ///
    /// Useful in the interview as the thing you show when they ask "what does
    /// deleting the root even mean?", and useful in tests because the max over
    /// this list must equal <see cref="MaxHeight"/>.
    /// </summary>
    public static List<(int RootLabel, int Height)> Components(
        RootedTree tree, IEnumerable<int> deleted, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));

        var result = new List<(int, int)>();
        if (tree.Count == 0)
            return result;

        var gone = tree.Mark(deleted);
        var contrib = ContribAll(tree, gone);

        // Top-down: a node is a forest root iff it survives and its parent chain
        // is entirely deleted. Pre-order (reverse BottomUp) guarantees the flag
        // for the parent is set before the child reads it.
        var covered = new bool[tree.Count];      // has a surviving ancestor
        for (int i = tree.Count - 1; i >= 0; i--)
        {
            int v = tree.BottomUp[i];
            int p = tree.Parent[v];

            covered[v] = p != -1 && (covered[p] || !gone[p]);
            if (!gone[v] && !covered[v])
                result.Add((tree.Label[v], ToMeasure(contrib[v], measure)));
        }

        return result;
    }

    // ----------------------------------------------- follow-up 1: minimum deletions
    //
    // "Given k, delete as few nodes as possible so the post-deletion height is
    // at most k."
    //
    // Contrib only ever grows on the way up (a survivor adds a level, a deleted
    // node keeps the max), so Contrib(root) <= k is equivalent to Contrib(v) <= k
    // at EVERY node. That is what makes a per-subtree state legal: a subtree can
    // be summarised by the single number it hands its parent.
    //
    //   dp[v][h] = fewest deletions inside subtree(v) with Contrib(v) <= h
    //
    //   delete v : 1 + sum over children of dp[c][h]        (v adds no level)
    //   keep   v :     sum over children of dp[c][h-1]      (needs h >= 1)
    //
    // dp[v][0] is "delete the whole subtree", because Contrib <= 0 means nothing
    // in it survives. The answer is dp[root][k]. O(n * k) time and space: each
    // node is touched once per budget value.
    //
    // It is never infeasible -- deleting everything always satisfies any k >= 0.

    /// <summary>Minimum deletions so the post-deletion height is at most <paramref name="k"/>.</summary>
    public static int MinDeletions(RootedTree tree, int k, HeightMeasure measure = HeightMeasure.Edges)
        => BestPlan(tree, k, measure).Deletions;

    /// <summary>
    /// Follow-up 1 and 2 together: the fewest deletions, and among all sets of
    /// that size the one maximising the sum of the deleted nodes' depths.
    ///
    /// The tiebreak composes with the DP for free because both objectives are
    /// plain sums over the chosen nodes, so a lexicographic (fewest, then
    /// deepest) comparison is still additive -- swap any child's choice for a
    /// lexicographically better one and the total only improves. Note the depth
    /// ORIGIN is irrelevant to the tiebreak: two solutions being compared always
    /// have the same size, so shifting every depth by a constant shifts both
    /// sums equally. Depths are the ORIGINAL depths; promotion does not renumber
    /// anything, and a node's own depth is fixed before any deletion happens.
    /// </summary>
    public static DeletionPlan BestPlan(RootedTree tree, int k, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));

        int budget = LevelBudget(k, measure);
        var nothing = new DeletionPlan(0, 0, Array.Empty<int>());

        if (tree.Count == 0)
            return nothing;

        // k at or above the current height costs nothing, and clamping here is
        // what keeps the table O(n * min(k, height)) instead of O(n * k) for a
        // silly k.
        if (budget >= ContribAll(tree, new bool[tree.Count])[tree.Root])
            return nothing;

        var dp = new Cost[tree.Count, budget + 1];
        var keep = new bool[tree.Count, budget + 1];

        foreach (int v in tree.BottomUp)
        {
            for (int h = 0; h <= budget; h++)
            {
                // Delete v: children keep the full budget, since v adds no level.
                var best = new Cost(1, tree.Depth[v]);
                foreach (int c in tree.Children[v])
                    best += dp[c, h];

                bool keepHere = false;

                if (h >= 1)
                {
                    // Keep v: v spends one level, every child must fit in h - 1.
                    var kept = default(Cost);
                    foreach (int c in tree.Children[v])
                        kept += dp[c, h - 1];

                    if (kept.IsBetterThan(best))
                    {
                        best = kept;
                        keepHere = true;
                    }
                }

                dp[v, h] = best;
                keep[v, h] = keepHere;
            }
        }

        // Replay the decisions top-down. Keeping a node spends a level of the
        // budget on the way down; deleting one does not.
        var deleted = new List<int>(dp[tree.Root, budget].Deletions);
        var stack = new Stack<(int Node, int Budget)>();
        stack.Push((tree.Root, budget));

        while (stack.Count > 0)
        {
            var (v, h) = stack.Pop();
            bool survives = keep[v, h];

            if (!survives)
                deleted.Add(tree.Label[v]);

            foreach (int c in tree.Children[v])
                stack.Push((c, survives ? h - 1 : h));
        }

        deleted.Sort();
        return new DeletionPlan(dp[tree.Root, budget].Deletions, dp[tree.Root, budget].DepthSum, deleted);
    }

    /// <summary>(deletions, sum of deleted depths) -- fewest first, then deepest.</summary>
    private readonly record struct Cost(int Deletions, long DepthSum)
    {
        public static Cost operator +(Cost a, Cost b)
            => new(a.Deletions + b.Deletions, a.DepthSum + b.DepthSum);

        public bool IsBetterThan(Cost other)
            => Deletions != other.Deletions ? Deletions < other.Deletions : DepthSum > other.DepthSum;
    }

    // ------------------------------------- the no-DP variant the phone screen asks for
    //
    // "Solve the minimum-deletion follow-up WITHOUT dynamic programming."
    //
    // Walk bottom-up and delete a node only when keeping it would break the
    // budget. Since every child's contribution is already final and <= k, a node
    // that overflows does so by exactly one level, and deleting it drops the
    // contribution back to max(children) <= k. So one pass, no table, O(n) time
    // and O(1) extra state per node.
    //
    // Why it is optimal, in the language the reformulation gives us: the goal is
    // to KEEP as many nodes as possible subject to "at most k survivors on any
    // root-to-leaf path". Deep nodes are the cheap ones to keep -- a node lies on
    // fewer root-to-leaf paths the deeper it sits, and keeping it constrains a
    // subset of what keeping any ancestor would constrain. Greedily keeping the
    // deepest nodes first is the standard exchange argument: given any optimal
    // solution that drops a node the greedy kept, swapping it back in and
    // dropping the shallower node it collided with is still feasible and no
    // smaller. The randomized block below checks it against brute force anyway,
    // which is the honest way to trust a greedy invariant under time pressure.
    //
    // WHAT IT DOES NOT DO: follow-up 2. This greedy always deletes ancestors and
    // keeps descendants, so it lands on the min-deletion solution with the
    // SMALLEST depth sum -- precisely the wrong end of the tiebreak. If both
    // follow-ups get asked, the DP is the only one that answers both.

    /// <summary>Minimum deletions for height &lt;= k, greedily, no table. O(n).</summary>
    public static int MinDeletionsGreedy(RootedTree tree, int k, HeightMeasure measure = HeightMeasure.Edges)
        => GreedyPlan(tree, k, measure).Deletions;

    /// <summary>
    /// The greedy with its deletion set, so it can be diffed against
    /// <see cref="BestPlan"/>: same size, and usually a different set with a
    /// worse (smaller) depth sum.
    /// </summary>
    public static DeletionPlan GreedyPlan(RootedTree tree, int k, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));

        int budget = LevelBudget(k, measure);
        if (tree.Count == 0)
            return new DeletionPlan(0, 0, Array.Empty<int>());

        var contrib = new int[tree.Count];
        var deleted = new List<int>();
        long depthSum = 0;

        foreach (int v in tree.BottomUp)
        {
            int tallest = 0;
            foreach (int c in tree.Children[v])
                tallest = Math.Max(tallest, contrib[c]);

            if (tallest + 1 > budget)
            {
                // Keeping v would overflow. Deleting it costs one node and drops
                // the contribution to `tallest`, which is already within budget.
                deleted.Add(tree.Label[v]);
                depthSum += tree.Depth[v];
                contrib[v] = tallest;
            }
            else
            {
                contrib[v] = tallest + 1;
            }
        }

        deleted.Sort();
        return new DeletionPlan(deleted.Count, depthSum, deleted);
    }

    // ------------------------------------------------------------- the oracle
    //
    // The binary phrasing caps n at 15, which is an explicit invitation to brute
    // force. Even when the real answer must scale, this is the thing that makes
    // a greedy invariant trustworthy: enumerate every subset on a 6-8 node tree
    // and compare. Write it FIRST if the greedy is the deliverable.

    /// <summary>
    /// Every subset of nodes, scored by the same lexicographic objective as
    /// <see cref="BestPlan"/>. O(2^n * n) -- capped at 22 nodes on purpose.
    /// </summary>
    public static DeletionPlan BestPlanBruteForce(
        RootedTree tree, int k, HeightMeasure measure = HeightMeasure.Edges)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));
        if (tree.Count > 22)
            throw new ArgumentException($"brute force is for oracles, not for {tree.Count} nodes", nameof(tree));

        int budget = LevelBudget(k, measure);
        if (tree.Count == 0)
            return new DeletionPlan(0, 0, Array.Empty<int>());

        var deleted = new bool[tree.Count];
        var best = new Cost(int.MaxValue, long.MinValue);
        int bestMask = 0;

        for (int mask = 0; mask < 1 << tree.Count; mask++)
        {
            var cost = new Cost(0, 0);
            for (int i = 0; i < tree.Count; i++)
            {
                deleted[i] = (mask >> i & 1) != 0;
                if (deleted[i])
                    cost += new Cost(1, tree.Depth[i]);
            }

            if (!cost.IsBetterThan(best))
                continue;                        // cannot win, so skip the O(n) height check
            if (ContribAll(tree, deleted)[tree.Root] > budget)
                continue;

            best = cost;
            bestMask = mask;
        }

        var labels = new List<int>();
        for (int i = 0; i < tree.Count; i++)
            if ((bestMask >> i & 1) != 0)
                labels.Add(tree.Label[i]);

        labels.Sort();
        return new DeletionPlan(best.Deletions, best.DepthSum, labels);
    }

    // ------------------------------------------------------------- conversions

    private static int ToMeasure(int levels, HeightMeasure measure)
        => measure == HeightMeasure.Levels ? levels : Math.Max(0, levels - 1);

    /// <summary>
    /// The caller's k, in levels. Edges k = 0 means "every survivor is alone",
    /// which is NOT the same as "delete everything" -- that is only expressible
    /// as Levels k = 0. Another reason to pin the convention down early.
    /// </summary>
    private static int LevelBudget(int k, HeightMeasure measure)
    {
        if (k < 0)
            throw new ArgumentOutOfRangeException(nameof(k), "a height budget cannot be negative");

        return measure == HeightMeasure.Levels ? k : k + 1;
    }

    // ------------------------------------------------------------------ tests

    public static void Main()
    {
        //         1
        //       /   \
        //      2     3
        //     / \    |
        //    4   5   6
        //            |
        //            7
        var example = RootedTree.FromEdges(
            new[] { new[] { 1, 2 }, new[] { 1, 3 }, new[] { 2, 4 }, new[] { 2, 5 }, new[] { 3, 6 }, new[] { 6, 7 } },
            rootLabel: 1);

        Console.WriteLine("== base case: skip deleted nodes, promote their children ==");
        Console.WriteLine("  tree: 1 -> {2 -> {4,5}, 3 -> 6 -> 7}");
        Console.WriteLine($"  nothing deleted      -> {MaxHeight(example, Array.Empty<int>())} edges (expect 3: 1-3-6-7)");
        Console.WriteLine($"  deleted {{2}}          -> {MaxHeight(example, new[] { 2 })} edges (expect 3: 4 and 5 rise to 1, 1-3-6-7 still wins)");
        Console.WriteLine($"  deleted {{3}}          -> {MaxHeight(example, new[] { 3 })} edges (expect 2: 6 rises to 1, 1-6-7)");
        Console.WriteLine($"  deleted {{3,6}}        -> {MaxHeight(example, new[] { 3, 6 })} edges (expect 2: 7 rises all the way to 1, 1-2-4 wins)");
        Console.WriteLine($"  deleted {{6}}          -> {MaxHeight(example, new[] { 6 })} edges (expect 2: 7 rises to 3)");
        Console.WriteLine($"  deleted {{4,5,7}}      -> {MaxHeight(example, new[] { 4, 5, 7 })} edges (expect 2: 1-3-6)");
        Console.WriteLine($"  every node deleted   -> {MaxHeight(example, new[] { 1, 2, 3, 4, 5, 6, 7 })} (expect 0: nothing survives)");

        Console.WriteLine();
        Console.WriteLine("  same input, both conventions -- ASK WHICH ONE:");
        Console.WriteLine($"    deleted {{2}} in edges  -> {MaxHeight(example, new[] { 2 }, HeightMeasure.Edges)}");
        Console.WriteLine($"    deleted {{2}} in levels -> {MaxHeight(example, new[] { 2 }, HeightMeasure.Levels)}");

        Console.WriteLine();
        Console.WriteLine("== deleting the root makes a FOREST, not an empty tree ==");

        var afterRoot = Components(example, new[] { 1 });
        Console.WriteLine($"  deleted {{1}} -> {afterRoot.Count} trees: " +
                          string.Join(", ", afterRoot.Select(c => $"root {c.RootLabel} at height {c.Height}")));
        Console.WriteLine($"  max over the forest -> {MaxHeight(example, new[] { 1 })} (expect 2: 3-6-7)");

        var afterTop = Components(example, new[] { 1, 3 });
        Console.WriteLine($"  deleted {{1,3}} -> {afterTop.Count} trees: " +
                          string.Join(", ", afterTop.Select(c => $"root {c.RootLabel} at height {c.Height}")));
        Console.WriteLine("    ^ 6 was promoted past BOTH 3 and 1, so it roots its own tree.");

        Console.WriteLine();
        Console.WriteLine("== degenerate inputs ==");

        var empty = RootedTree.FromParents(Array.Empty<int>());
        var single = RootedTree.FromEdges(Array.Empty<int[]>(), rootLabel: 42);

        Console.WriteLine($"  empty tree                     -> {MaxHeight(empty, Array.Empty<int>())} (expect 0)");
        Console.WriteLine($"  single node, kept, edges       -> {MaxHeight(single, Array.Empty<int>())} (expect 0)");
        Console.WriteLine($"  single node, kept, levels      -> {MaxHeight(single, Array.Empty<int>(), HeightMeasure.Levels)} (expect 1)");
        Console.WriteLine($"  single node, deleted, levels   -> {MaxHeight(single, new[] { 42 }, HeightMeasure.Levels)} (expect 0)");
        Console.WriteLine("    ^ in EDGES both of the last two are 0. The edges convention cannot tell");
        Console.WriteLine("      'one node' from 'no nodes'; that is why the internals count levels.");

        try
        {
            MaxHeight(example, new[] { 99 });
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  deleting an unknown node rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        try
        {
            RootedTree.FromParents(new[] { -1, 0, 3, 2 });      // 2 <-> 3 cycle
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  parent-pointer cycle rejected:    {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== iterative DFS agrees with the recursion ==");

        var chain = RootedTree.FromParents(Enumerable.Range(0, 100_000).Select(i => i - 1).ToArray());
        Console.WriteLine($"  100,000-node chain, no deletions -> {MaxHeightIterative(chain, Array.Empty<int>())} edges");
        Console.WriteLine($"  ...with every odd node deleted   -> " +
                          $"{MaxHeightIterative(chain, Enumerable.Range(0, 100_000).Where(i => i % 2 == 1))} edges (expect 49,999)");
        Console.WriteLine("    ^ the recursive version is NOT called here: 100k frames is a stack overflow,");
        Console.WriteLine("      and a stack overflow in .NET cannot be caught. Say this before they ask.");

        Console.WriteLine();
        Console.WriteLine("== follow-up 1: fewest deletions for height <= k ==");
        Console.WriteLine("  the same 7-node tree, k in EDGES");
        Console.WriteLine("     k | DP | greedy | brute | DP deletes");
        for (int k = 0; k <= 4; k++)
        {
            var dp = BestPlan(example, k);
            int greedy = MinDeletionsGreedy(example, k);
            int brute = BestPlanBruteForce(example, k).Deletions;
            Console.WriteLine($"     {k} | {dp.Deletions,2} | {greedy,6} | {brute,5} | {{{string.Join(",", dp.Deleted)}}}");
        }

        Console.WriteLine();
        Console.WriteLine("  k = 0 EDGES is not 'delete all but the root':");
        var flat = BestPlan(example, 0);
        Console.WriteLine($"    deletes {flat.Deletions} nodes {{{string.Join(",", flat.Deleted)}}}, leaving every survivor alone.");
        Console.WriteLine("    Survivors must form an antichain, and the biggest antichain in a tree is");
        Console.WriteLine("    its leaf set {4,5,7}, so the answer is n - leaves = 7 - 3 = 4, not 6.");
        Console.WriteLine($"    k = 0 LEVELS really does delete everything: {MinDeletions(example, 0, HeightMeasure.Levels)} (expect 7)");
        Console.WriteLine($"    k >= current height          -> {MinDeletions(example, 9)} deletions (expect 0)");

        Console.WriteLine();
        Console.WriteLine("== follow-up 2: among the smallest sets, delete the DEEPEST nodes ==");

        var line = RootedTree.FromEdges(new[] { new[] { 1, 2 }, new[] { 2, 3 } }, rootLabel: 1);
        var lineDp = BestPlan(line, 1);
        var lineGreedy = GreedyPlan(line, 1);
        Console.WriteLine("  chain 1 -> 2 -> 3, k = 1 edge (2 levels). Three single-node solutions exist.");
        Console.WriteLine($"    greedy : deletes {{{string.Join(",", lineGreedy.Deleted)}}}, depth sum {lineGreedy.DepthSum}");
        Console.WriteLine($"    DP     : deletes {{{string.Join(",", lineDp.Deleted)}}}, depth sum {lineDp.DepthSum}");
        Console.WriteLine("    ^ same size, opposite ends of the tiebreak: the greedy always sacrifices");
        Console.WriteLine("      ancestors to keep depth, which is the worst possible depth sum.");

        var exampleDp = BestPlan(example, 1);
        var exampleGreedy = GreedyPlan(example, 1);
        Console.WriteLine($"  7-node tree, k = 1 edge:");
        Console.WriteLine($"    greedy : {{{string.Join(",", exampleGreedy.Deleted)}}}, depth sum {exampleGreedy.DepthSum}");
        Console.WriteLine($"    DP     : {{{string.Join(",", exampleDp.Deleted)}}}, depth sum {exampleDp.DepthSum}");
        Console.WriteLine($"    brute  : {{{string.Join(",", BestPlanBruteForce(example, 1).Deleted)}}}, " +
                          $"depth sum {BestPlanBruteForce(example, 1).DepthSum}");

        Console.WriteLine();
        Console.WriteLine("== the binary variant (unique ids, height in LEVELS) ==");

        var triangle = RootedTree.FromBinaryLevelOrder(new int?[] { 1, 2, 3 });
        Console.WriteLine($"  [1,2,3], nothing deleted    -> {MaxHeight(triangle, Array.Empty<int>(), HeightMeasure.Levels)} levels (expect 2)");
        Console.WriteLine($"  [1,2,3], node 1 deleted     -> {MaxHeight(triangle, new[] { 1 }, HeightMeasure.Levels)} level (expect 1: 2 and 3 stand alone)");
        Console.WriteLine($"  [1,2,3], everything deleted -> {MaxHeight(triangle, new[] { 1, 2, 3 }, HeightMeasure.Levels)} (expect 0)");
        Console.WriteLine($"  [1,2,3], targetHeight 1     -> {MinDeletions(triangle, 1, HeightMeasure.Levels)} deletion (expect 1: drop the root)");

        var lopsided = RootedTree.FromBinaryLevelOrder(new int?[] { 1, 2, 3, null, 4, null, null, null, 5 });
        Console.WriteLine("  [1,2,3,null,4,null,null,null,5] = 1 -> {2 -> 4 -> 5, 3}");
        Console.WriteLine($"    height          -> {MaxHeight(lopsided, Array.Empty<int>(), HeightMeasure.Levels)} levels (expect 4)");
        Console.WriteLine($"    delete {{2}}      -> {MaxHeight(lopsided, new[] { 2 }, HeightMeasure.Levels)} levels (expect 3: 4 rises to 1)");
        Console.WriteLine($"    delete {{2,4}}    -> {MaxHeight(lopsided, new[] { 2, 4 }, HeightMeasure.Levels)} levels (expect 2: 5 rises past both)");
        Console.WriteLine($"    targetHeight 2  -> {MinDeletions(lopsided, 2, HeightMeasure.Levels)} deletions");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(1337);
        bool iterativeAgrees = true, componentsAgree = true, greedyOptimal = true;
        bool dpOptimal = true, dpTiebreakOptimal = true, plansValid = true, dpAtLeastGreedy = true;
        bool leafIdentity = true, monotone = true;
        int greedyMissedTiebreak = 0, trials = 0;

        for (int trial = 0; trial < 1500; trial++)
        {
            int n = rng.Next(1, 9);

            // Random rooted tree: node i > 0 picks any earlier node as its
            // parent, which reaches every shape from a chain to a star.
            var parent = new int[n];
            parent[0] = -1;
            for (int i = 1; i < n; i++)
                parent[i] = rng.Next(i);

            var tree = RootedTree.FromParents(parent);
            var deleted = Enumerable.Range(0, n).Where(_ => rng.Next(3) == 0).ToArray();

            // 1. Recursion, explicit stack, and the forest view all agree.
            int levels = MaxHeight(tree, deleted, HeightMeasure.Levels);
            iterativeAgrees &= MaxHeightIterative(tree, deleted, HeightMeasure.Levels) == levels;

            var forest = Components(tree, deleted, HeightMeasure.Levels);
            int forestMax = forest.Count == 0 ? 0 : forest.Max(c => c.Height);
            componentsAgree &= forestMax == levels && forest.Count == n - CoveredOrDeleted(tree, deleted);

            int height = MaxHeight(tree, Array.Empty<int>(), HeightMeasure.Levels);

            for (int k = 0; k <= height + 1; k++)
            {
                trials++;

                var dp = BestPlan(tree, k, HeightMeasure.Levels);
                var greedy = GreedyPlan(tree, k, HeightMeasure.Levels);
                var brute = BestPlanBruteForce(tree, k, HeightMeasure.Levels);

                // 2. The DP is optimal, and so is the greedy -- on COUNT.
                dpOptimal &= dp.Deletions == brute.Deletions;
                greedyOptimal &= greedy.Deletions == brute.Deletions;

                // 3. The DP also wins the depth tiebreak; the greedy often loses it.
                dpTiebreakOptimal &= dp.DepthSum == brute.DepthSum;
                dpAtLeastGreedy &= dp.DepthSum >= greedy.DepthSum;
                if (greedy.DepthSum < dp.DepthSum)
                    greedyMissedTiebreak++;

                // 4. The returned sets really do satisfy the constraint. A plan
                //    that is the right SIZE but the wrong SET is the bug this
                //    catches, and reconstruction is where it hides.
                plansValid &=
                    MaxHeight(tree, dp.Deleted, HeightMeasure.Levels) <= k &&
                    MaxHeight(tree, greedy.Deleted, HeightMeasure.Levels) <= k &&
                    dp.Deleted.Count == dp.Deletions &&
                    greedy.Deleted.Count == greedy.Deletions;

                // 5. A bigger budget never costs more deletions.
                if (k > 0)
                    monotone &= dp.Deletions <= BestPlan(tree, k - 1, HeightMeasure.Levels).Deletions;
            }

            // 6. Height <= 1 level means the survivors are an antichain, and the
            //    largest antichain in a tree is exactly its leaf set.
            int leaves = Enumerable.Range(0, n).Count(i => tree.Children[i].Count == 0);
            leafIdentity &= MinDeletions(tree, 1, HeightMeasure.Levels) == n - leaves;

            // 7. And the two extremes.
            leafIdentity &= MinDeletions(tree, 0, HeightMeasure.Levels) == n
                         && MinDeletions(tree, height, HeightMeasure.Levels) == 0;
        }

        Console.WriteLine($"  1,500 random trees, {trials} (tree, k) pairs");
        Console.WriteLine($"  iterative DFS == recursive DFS:                        {iterativeAgrees}");
        Console.WriteLine($"  forest view == max height, and roots counted right:    {componentsAgree}");
        Console.WriteLine($"  DP deletion count == brute force:                      {dpOptimal}");
        Console.WriteLine($"  GREEDY deletion count == brute force (no DP needed):   {greedyOptimal}");
        Console.WriteLine($"  DP depth sum == brute force (follow-up 2):             {dpTiebreakOptimal}");
        Console.WriteLine($"  DP depth sum >= greedy depth sum:                      {dpAtLeastGreedy}");
        Console.WriteLine($"  every returned deletion set really achieves height<=k: {plansValid}");
        Console.WriteLine($"  deletions never increase as k grows:                   {monotone}");
        Console.WriteLine($"  k=1 identity (n - leaves), k=0, and k=height:           {leafIdentity}");
        Console.WriteLine($"  cases where the greedy lost the depth tiebreak:        {greedyMissedTiebreak}");
        Console.WriteLine("    ^ the greedy is a valid answer to follow-up 1 and a WRONG answer to");
        Console.WriteLine("      follow-up 2. If the interviewer bans DP and then asks for the deepest");
        Console.WriteLine("      deletions, say that out loud -- it is the point of the pair.");
    }

    /// <summary>Test helper: how many nodes are deleted or sit under a survivor.</summary>
    private static int CoveredOrDeleted(RootedTree tree, IEnumerable<int> deletedLabels)
    {
        var gone = tree.Mark(deletedLabels);
        int count = 0;

        var covered = new bool[tree.Count];
        for (int i = tree.Count - 1; i >= 0; i--)
        {
            int v = tree.BottomUp[i];
            int p = tree.Parent[v];
            covered[v] = p != -1 && (covered[p] || !gone[p]);

            if (gone[v] || covered[v])
                count++;
        }

        return count;
    }
}

// ---- Notes for the follow-up questions ----
//
// "Delete a node and its whole subtree instead of promoting the children."
//     A completely different problem, and worth confirming which one they mean in
//     the first minute. Under subtree deletion the recurrence loses its second
//     case -- height(v) = 0 if deleted, else 1 + max(children) -- and the minimum
//     deletion follow-up becomes trivial-greedy (cut at depth k). Promotion is
//     what makes this question interesting; do not solve the easy one by accident.
//
// "The deleted set arrives as a stream / is queried many times."
//     Each query is O(n) as written. For many queries over the same tree, note
//     that Contrib only changes on the ancestor path of a toggled node, so
//     maintaining it incrementally is O(depth) per toggle -- O(log n) on a
//     balanced tree, still O(n) on a chain unless you go to heavy-light or
//     link-cut, which is well past what a screen wants.
//
// "n is up to 10^5 and k up to 10^5."
//     The DP is O(n * k) and would be 10^10. The clamp in BestPlan saves it: k
//     above the current height costs zero deletions, so the table is really
//     O(n * min(k, height)). Worst case is still a 10^5 chain with k = 5*10^4,
//     which is 5 * 10^9 -- at that point the greedy is not a fallback, it is the
//     only answer, at O(n). That is the honest reason to have both.
//
// "Weighted nodes: minimise total weight deleted, not the count."
//     The DP is unchanged apart from swapping the 1 for w[v]; the objective is
//     still an additive sum over chosen nodes. The GREEDY breaks -- it assumes
//     every node costs the same, so it will happily delete an expensive ancestor
//     to save two cheap leaves. Weights are the cleanest way to see why the DP is
//     the safer thing to write when the constraint is not stated.
//
// "Maximise the number of survivors instead."
//     Same problem: survivors = n - deletions. Stating it that way turns it into
//     "keep as many nodes as possible with at most k per root-to-leaf path",
//     which is the framing that makes the greedy obviously correct and the k = 1
//     answer (the leaf set) immediate.
//
// "Prove the greedy, the interviewer is not convinced."
//     Exchange argument on the survivor formulation. Take an optimal survivor set
//     S and the greedy set G, and look at the deepest node in G \ S. Adding it to
//     S violates the budget on some root-to-leaf path, so that path already holds
//     k survivors from S; at least one of them is strictly shallower (the greedy
//     took this node only because everything below it was already within budget),
//     and swapping it out keeps |S| and moves S one node closer to G. Repeat.
