// LeetCode 687 - Longest Univalue Path
// Difficulty: Medium
// Pattern: Post-order DFS, "arrow down" vs "path bending at this node"
//
// Return the number of EDGES on the longest path whose nodes all share one
// value. The path may start and end anywhere; it need not touch the root.
//
// The whole family (#543 diameter, #124 max path sum, this one) rests on one
// split. Every path has a single highest node -- the point where it bends -- so
// ask two different questions at each node and never mix them up:
//
//   global best  = left arrow + right arrow   (the bend; BOTH sides count)
//   return value = max(left, right)           (ONE arrow, extendable upward)
//
// A parent can only ever extend one branch, so the return picks a side while
// the global best records the bend. The recursion visits every node, so every
// possible bend point is scored exactly once.
//
//     5         node 4:  neither child is a 4  -> arrows 0, 0; bend 0; returns 0
//    / \        node 5 (right): right child is 5 -> arrows 0, 1; bend 1; returns 1
//   4   5       root 5:  left child 4 differs   -> left arrow 0
//  / \    \              right child 5 matches  -> right arrow = 1 + 1 = 2
// 1   1    5             bend 0 + 2 = 2  <- the answer, the 5-5-5 spine
//
// Traps:
//   - Edges, not nodes. A single node scores 0, not 1. An n-node spine is n-1.
//   - Recurse into BOTH children UNCONDITIONALLY, then compare values. Guarding
//     the recursion behind `child.Val == node.Val` skips whole subtrees, and the
//     longest path is often inside a subtree that disagrees with its parent
//     ([1,4,5,4,4,null,5]: the 4-4-4 answer hangs under a root valued 1).
//   - The child's arrow is only usable when the EDGE matches, so the +1 and the
//     value test belong in one expression: match ? Arrow(child) + 1 : 0.
//   - Null children contribute 0, which falls out of that same expression --
//     no separate leaf case.
//
// Time: O(n)  Space: O(h) -- O(n) on a degenerate skewed tree.
//
// Depth note: n goes to 10^4, and a skewed input makes h = n. The recursion is
// fine on the default 1 MB CLR stack at that size, but the iterative version
// below is the one to reach for when the interviewer pushes on depth: same
// fold, explicit stack, arrow lengths memoised in a dictionary.

namespace CodingPatterns.Trees;

public class LongestUnivaluePath
{
    // ---- Approach 1: recursive post-order ----
    public int LongestPath(TreeNode root)
    {
        var best = 0;
        Arrow(root, ref best);
        return best;
    }

    // Longest univalue path in EDGES that starts at `node` and only goes down.
    private static int Arrow(TreeNode node, ref int best)
    {
        if (node == null) return 0;

        // Recurse first, whatever the values are -- the answer may live in a
        // subtree that has nothing in common with this node.
        var leftDown = Arrow(node.Left, ref best);
        var rightDown = Arrow(node.Right, ref best);

        // An arrow crosses the edge only when the child agrees with us.
        var left = node.Left != null && node.Left.Val == node.Val ? leftDown + 1 : 0;
        var right = node.Right != null && node.Right.Val == node.Val ? rightDown + 1 : 0;

        best = Math.Max(best, left + right);   // the path that bends here
        return Math.Max(left, right);          // what the parent can extend
    }

    // ---- Approach 2: iterative post-order, no call stack ----
    // Each node is pushed twice: once to expand its children, once (after they
    // are finished) to combine. `down` memoises each node's arrow length; the
    // dictionary uses reference equality, so equal Vals never collide.
    public int LongestPathIterative(TreeNode root)
    {
        var best = 0;
        var down = new Dictionary<TreeNode, int>();
        var stack = new Stack<(TreeNode Node, bool Expanded)>();
        stack.Push((root, false));

        while (stack.Count > 0)
        {
            var (node, expanded) = stack.Pop();
            if (node == null) continue;

            if (!expanded)
            {
                stack.Push((node, true));          // revisit after both children
                stack.Push((node.Left, false));
                stack.Push((node.Right, false));
                continue;
            }

            var left = node.Left != null && node.Left.Val == node.Val
                ? down[node.Left] + 1 : 0;
            var right = node.Right != null && node.Right.Val == node.Val
                ? down[node.Right] + 1 : 0;

            best = Math.Max(best, left + right);
            down[node] = Math.Max(left, right);
        }

        return best;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new LongestUnivaluePath();

        void Check(string label, int?[] level, int expected)
        {
            var a = sol.LongestPath(Build(level));
            var b = sol.LongestPathIterative(Build(level));
            var ok = a == expected && b == expected;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-22} {a}" +
                              (ok ? "" : $"  expected {expected}  iterative {b}"));
        }

        Check("LC example 1", [5, 4, 5, 1, 1, null, 5], 2);      // 5-5-5 down the right
        Check("LC example 2", [1, 4, 5, 4, 4, null, 5], 2);      // 4-4-4 under a root of 1
        Check("empty", [], 0);
        Check("single node", [1], 0);                            // edges, not nodes
        Check("two equal", [1, 1], 1);
        Check("two different", [1, 2], 0);
        Check("all distinct", [1, 2, 3, 4, 5, 6, 7], 0);
        Check("all equal, full", [7, 7, 7, 7, 7, 7, 7], 4);      // leaf -> root -> leaf
        Check("bend at a child", [1, 2, 2, 2, 2], 2);
        Check("answer hidden below", [9, 9, 1, 1, 1, 1, 1], 2);  // the 1-1-1 bend on the right
        Check("negative values", [-1, -1, -1], 2);

        // Depth: a 10,000-node chain of equal values -> 9,999 edges.
        var chain = new TreeNode(3);
        var node = chain;
        for (var i = 0; i < 9_999; i++)
        {
            node.Left = new TreeNode(3);
            node = node.Left;
        }
        var deep = sol.LongestPathIterative(chain);
        Console.WriteLine($"{(deep == 9_999 ? "PASS" : "FAIL")}  {"deep skewed chain",-22} {deep}");
    }

    // Build a tree from a LeetCode level-order array (null marks a missing child).
    private static TreeNode Build(int?[] level)
    {
        if (level.Length == 0 || level[0] == null) return null;

        var root = new TreeNode(level[0].Value);
        var queue = new Queue<TreeNode>();
        queue.Enqueue(root);
        var i = 1;

        while (queue.Count > 0 && i < level.Length)
        {
            var node = queue.Dequeue();

            if (i < level.Length)
            {
                if (level[i] != null)
                {
                    node.Left = new TreeNode(level[i].Value);
                    queue.Enqueue(node.Left);
                }
                i++;
            }
            if (i < level.Length)
            {
                if (level[i] != null)
                {
                    node.Right = new TreeNode(level[i].Value);
                    queue.Enqueue(node.Right);
                }
                i++;
            }
        }

        return root;
    }
}
