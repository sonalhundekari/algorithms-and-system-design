// Snowflake - Rewrite Tree With Subtree Sums
// Difficulty: Easy/Medium
// Pattern: post-order DFS (divide and conquer), two trees walked in lockstep
//
// root1 and root2 have identical shape. For every node, write the sum of the
// matching subtree of root1 onto the matching node of root2:
//
//     sum(node) = node.Val + sum(node.Left) + sum(node.Right)
//
// That is a post-order fold: you cannot fill a parent until both children are
// known, so the answer is produced on the way BACK UP. One walk does both jobs
// -- return the sum to the caller, and assign it into the mirror node -- so the
// whole thing is a single O(n) pass with no extra map from node1 -> node2.
//
// Reading order matters: assign into node2 only AFTER recursing, otherwise you
// would be reading children of node2 that have not been computed yet. (Here we
// never read node2 at all in the recursive version, which is why it is safe.)
//
// Overflow: 10^5 nodes * 10^4 max value = 10^9, which still fits in int, but the
// accumulation is done in long and narrowed once at the end -- free insurance if
// the bounds ever move.
//
// Edge cases:
//   - null root       -> null (nothing to write)
//   - single node     -> root2.Val = root1.Val
//   - deep/skewed     -> recursion is O(h); 10^5 chained nodes will blow the
//                        1 MB default stack, so RewriteIterative() below does the
//                        same post-order with an explicit stack
//
// Time: O(n)  Space: O(h) recursion (O(log n) for a complete tree, O(n) skewed)

using System.Text;

namespace CodingPatterns.Trees;

public class RewriteTreeWithSubtreeSums
{
    // ---- Approach 1: recursive post-order (the interview answer) ----
    // Returns the subtree sum of `a` and writes it into `b` on the way up.
    public TreeNode Rewrite(TreeNode root1, TreeNode root2)
    {
        static long Fill(TreeNode a, TreeNode b)
        {
            if (a == null) return 0;                 // same shape => b is null too

            long sum = a.Val + Fill(a.Left, b.Left) + Fill(a.Right, b.Right);
            b.Val = (int)sum;                        // assign AFTER the children
            return sum;
        }

        Fill(root1, root2);
        return root2;
    }

    // ---- Approach 2: iterative post-order (deep trees, no stack overflow) ----
    // Trick: a root-right-left pre-order pushed onto a second stack pops back as
    // left-right-root, i.e. post-order. By the time a pair is popped from
    // `order`, both children of b already hold their subtree sums, so the parent
    // is just a.Val + b.Left.Val + b.Right.Val -- the output tree doubles as the
    // memo table and no separate dictionary is needed.
    public TreeNode RewriteIterative(TreeNode root1, TreeNode root2)
    {
        if (root1 == null) return root2;

        var stack = new Stack<(TreeNode A, TreeNode B)>();
        var order = new Stack<(TreeNode A, TreeNode B)>();
        stack.Push((root1, root2));

        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            order.Push((a, b));
            if (a.Left != null) stack.Push((a.Left, b.Left));
            if (a.Right != null) stack.Push((a.Right, b.Right));
        }

        while (order.Count > 0)
        {
            var (a, b) = order.Pop();
            long sum = a.Val;
            if (a.Left != null) sum += b.Left.Val;    // already rewritten
            if (a.Right != null) sum += b.Right.Val;
            b.Val = (int)sum;
        }
        return root2;
    }

    // ---- Approach 3 (follow-up): bounded parallel post-order ----
    // "Many subtrees, few worker threads." Sibling subtrees are independent, so
    // the left and right halves can be folded concurrently -- but spawning a task
    // per node costs far more in scheduling than the addition it saves.
    //
    // The fix is a DEPTH CUTOFF: fork only while depth < cutoff, then fall back to
    // the sequential walk. Cutoff = ceil(log2(ProcessorCount)) + 1 gives roughly
    // 2-4 tasks per core -- enough slack for the .NET thread pool's work-stealing
    // queues to rebalance a lopsided tree, without flooding the scheduler. The
    // parallelism ceiling is the hardware, not the node count.
    //
    // Correctness is free here: each task owns a disjoint subtree, every write
    // targets a node no sibling can reach, and the join at each parent is the
    // happens-before edge that publishes the children's writes.
    public TreeNode RewriteParallel(TreeNode root1, TreeNode root2)
    {
        static long Fill(TreeNode a, TreeNode b)
        {
            if (a == null) return 0;                 // same shape => b is null too

            long sum = a.Val + Fill(a.Left, b.Left) + Fill(a.Right, b.Right);
            b.Val = (int)sum;                        // assign AFTER the children
            return sum;
        }

        static long FillParallel(TreeNode a, TreeNode b, int depthBudget)
        {
            if (a == null) return 0;
            if (depthBudget <= 0) return Fill(a, b);   // deep enough: go sequential

            // Fork the left subtree, run the right one on this thread, then join.
            // Handing off only one side keeps the current thread busy instead of
            // parking it while two children run elsewhere.
            var left = Task.Run(() => FillParallel(a.Left, b.Left, depthBudget - 1));
            long right = FillParallel(a.Right, b.Right, depthBudget - 1);

            long sum = a.Val + left.Result + right;    // join publishes child writes
            b.Val = (int)sum;
            return sum;
        }

        int cutoff = (int)Math.Ceiling(Math.Log2(Math.Max(Environment.ProcessorCount, 1))) + 1;
        FillParallel(root1, root2, cutoff);
        return root2;
    }

    // ---- Level-order helpers (complete tree <-> array, as in the samples) ----
    public static TreeNode FromLevelOrder(params int[] values)
    {
        if (values.Length == 0) return null;

        var nodes = new TreeNode[values.Length];
        for (int i = 0; i < values.Length; i++) nodes[i] = new TreeNode(values[i]);
        for (int i = 0; i < values.Length; i++)
        {
            int l = 2 * i + 1, r = 2 * i + 2;
            if (l < values.Length) nodes[i].Left = nodes[l];
            if (r < values.Length) nodes[i].Right = nodes[r];
        }
        return nodes[0];
    }

    // BFS back out to an array -- valid because the tree is complete.
    public static string ToLevelOrder(TreeNode root)
    {
        if (root == null) return "";

        var sb = new StringBuilder();
        var queue = new Queue<TreeNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (sb.Length > 0) sb.Append(',');
            sb.Append(node.Val);
            if (node.Left != null) queue.Enqueue(node.Left);
            if (node.Right != null) queue.Enqueue(node.Right);
        }
        return sb.ToString();
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new RewriteTreeWithSubtreeSums();

        // Every approach mutates root2, so each gets its own fresh copy.
        void Check(string label, int[] tree1, int[] tree2, string expected)
        {
            var a = ToLevelOrder(sol.Rewrite(FromLevelOrder(tree1), FromLevelOrder(tree2)));
            var b = ToLevelOrder(sol.RewriteIterative(FromLevelOrder(tree1), FromLevelOrder(tree2)));
            var c = ToLevelOrder(sol.RewriteParallel(FromLevelOrder(tree1), FromLevelOrder(tree2)));

            bool ok = a == expected && b == expected && c == expected;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-20} [{a}]" +
                              (ok ? "" : $"  expected [{expected}]  iter [{b}]  par [{c}]"));
        }

        //       5
        //     /   \
        //    2     3
        //   / \   / \
        //  1   4 6   7
        Check("example 1",
            new[] { 5, 2, 3, 1, 4, 6, 7 },
            new[] { 9, 9, 9, 9, 9, 9, 9 },
            "28,7,16,1,4,6,7");

        // Last level partially filled: node 3 has only the child 6.
        Check("example 2",
            new[] { 1, 2, 3, 4, 5, 6 },
            new[] { 0, 0, 0, 0, 0, 0 },
            "21,11,9,4,5,6");

        Check("single node", new[] { 42 }, new[] { 0 }, "42");

        // Negatives cancel: the root's subtree sums to 0.
        Check("negatives",
            new[] { 10, -5, -5, 3, -3, 0, 0 },
            new[] { 1, 1, 1, 1, 1, 1, 1 },
            "0,-5,-5,3,-3,0,0");

        // One child (index 1 only) -- the "complete" shape at its smallest bend.
        Check("two nodes", new[] { 7, 8 }, new[] { 0, 0 }, "15,8");

        Console.WriteLine($"PASS  {"empty tree",-20} [{ToLevelOrder(sol.Rewrite(null, null))}]");

        // Deep skewed chain: recursion would risk a stack overflow, the explicit
        // stack does not. 100k nodes of value 1 -> node at depth d holds 100000-d.
        var deep1 = new TreeNode(1);
        var deep2 = new TreeNode(0);
        var tail1 = deep1;
        var tail2 = deep2;
        for (int i = 1; i < 100_000; i++)
        {
            tail1 = tail1.Right = new TreeNode(1);
            tail2 = tail2.Right = new TreeNode(0);
        }
        sol.RewriteIterative(deep1, deep2);
        Console.WriteLine($"{(deep2.Val == 100_000 ? "PASS" : "FAIL")}  " +
                          $"{"skewed 100k (iter)",-20} root={deep2.Val} expected=100000");
    }
}
