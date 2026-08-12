// LeetCode 872 - Leaf-Similar Trees
// Difficulty: Easy
// Pattern: DFS, left-to-right leaf collection (the tree's "leaf value sequence")
//
// Two trees are leaf-similar when the values of their leaves, read left to
// right, form the same sequence. Only leaves matter -- shape, internal values
// and depth are all irrelevant, so [3,5,1,6,2,9,8,null,null,7,4] and a
// completely different-looking tree can still match on (6,7,4,9,8).
//
// The whole problem reduces to: any left-to-right DFS (preorder / inorder /
// postorder -- they all agree on relative leaf order because they all visit the
// left subtree before the right) emits leaves in exactly the required order.
// Collect both sequences, compare element by element.
//
// Traps:
//   - A leaf is Left == null && Right == null. A node with one child is NOT a
//     leaf, so it contributes nothing even though it looks like a dead end.
//   - Compare as SEQUENCES, not sets/multisets: (1,2) != (2,1).
//   - Length must match too; a prefix is not a match. SequenceEqual handles
//     both, but a hand-rolled loop must check counts before/after.
//   - Two null roots are trivially similar (both sequences empty).
//
// Time: O(n + m)  Space: O(h1 + h2) recursion + O(leaves) for the lists
//
// Follow-up (interviewer's favourite): "what if the trees are huge / streamed?"
// -> don't materialise both lists; walk them in lockstep with explicit stacks
//    and bail on the first mismatch. That is Approach 2 below: O(h) extra space
//    and it short-circuits instead of always paying for the full traversal.

namespace CodingPatterns.Trees;

public class LeafSimilarTrees
{
    // ---- Approach 1: collect both leaf sequences, then compare ----
    public bool LeafSimilar(TreeNode root1, TreeNode root2)
    {
        var a = new List<int>();
        var b = new List<int>();
        CollectLeaves(root1, a);
        CollectLeaves(root2, b);
        return a.SequenceEqual(b);
    }

    private static void CollectLeaves(TreeNode node, List<int> leaves)
    {
        if (node == null) return;
        if (node.Left == null && node.Right == null)
        {
            leaves.Add(node.Val);
            return;                       // a leaf has no children to recurse into
        }
        CollectLeaves(node.Left, leaves);  // left before right == left-to-right order
        CollectLeaves(node.Right, leaves);
    }

    // ---- Approach 2: lockstep iteration, O(h) space, early exit ----
    // Each iterator is a preorder DFS stack that only ever stops on leaves.
    // We pull one leaf from each tree at a time and compare; the first mismatch
    // ends the walk, and both must run out at the same moment.
    public bool LeafSimilarLockstep(TreeNode root1, TreeNode root2)
    {
        var s1 = new Stack<TreeNode>();
        var s2 = new Stack<TreeNode>();
        if (root1 != null) s1.Push(root1);
        if (root2 != null) s2.Push(root2);

        while (s1.Count > 0 && s2.Count > 0)
        {
            if (NextLeaf(s1) != NextLeaf(s2)) return false;
        }

        // Both exhausted together -> same length. One left over -> a prefix match.
        return s1.Count == 0 && s2.Count == 0;
    }

    // Advance the stack until a leaf pops, and return its value.
    // Push Right first so Left comes off the stack first (left-to-right order).
    private static int NextLeaf(Stack<TreeNode> stack)
    {
        while (true)
        {
            var node = stack.Pop();
            if (node.Left == null && node.Right == null) return node.Val;
            if (node.Right != null) stack.Push(node.Right);
            if (node.Left != null) stack.Push(node.Left);
        }
    }

    // ---- Tests ----
    public static void Main()
    {
        var sol = new LeafSimilarTrees();

        void Check(string label, TreeNode t1, TreeNode t2, bool expected)
        {
            var a = sol.LeafSimilar(t1, t2);
            var b = sol.LeafSimilarLockstep(t1, t2);
            var ok = a == expected && b == expected;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-22} {a}" +
                              (ok ? "" : $"  expected {expected}  lockstep {b}"));
        }

        //      3                     3
        //     / \                   / \
        //    5   1                 5   1
        //   / \ / \               / \ / \
        //  6  2 9  8             6  7 4  2
        //    / \                        / \
        //   7   4                      9   8
        // Both leaf sequences: 6,7,4,9,8
        var t1 = new TreeNode(3,
            new TreeNode(5, new TreeNode(6), new TreeNode(2, new TreeNode(7), new TreeNode(4))),
            new TreeNode(1, new TreeNode(9), new TreeNode(8)));
        var t2 = new TreeNode(3,
            new TreeNode(5, new TreeNode(6), new TreeNode(7)),
            new TreeNode(1, new TreeNode(4), new TreeNode(2, new TreeNode(9), new TreeNode(8))));
        Check("LC example (similar)", t1, t2, true);

        // [1,2,3] vs [1,3,2]: same leaf VALUES, different order.
        Check("order matters",
            new TreeNode(1, new TreeNode(2), new TreeNode(3)),
            new TreeNode(1, new TreeNode(3), new TreeNode(2)),
            false);

        // A single node is its own (only) leaf.
        Check("single vs single", new TreeNode(1), new TreeNode(1), true);
        Check("single mismatch", new TreeNode(1), new TreeNode(2), false);

        // Internal values differ wildly, leaves agree -> still similar.
        Check("shape differs",
            new TreeNode(1, new TreeNode(2), new TreeNode(3)),
            new TreeNode(99, new TreeNode(42, new TreeNode(2), null), new TreeNode(3)),
            true);

        // One-child chain: only the bottom node is a leaf.
        Check("one-child chain",
            new TreeNode(1, new TreeNode(2, new TreeNode(3, new TreeNode(4), null), null), null),
            new TreeNode(9, null, new TreeNode(4)),
            true);

        // Prefix is not a match -- the second tree has an extra leaf.
        Check("prefix not equal",
            new TreeNode(1, new TreeNode(2), null),
            new TreeNode(1, new TreeNode(2), new TreeNode(3)),
            false);

        Check("both null", null, null, true);
        Check("one null", null, new TreeNode(1), false);
    }
}
