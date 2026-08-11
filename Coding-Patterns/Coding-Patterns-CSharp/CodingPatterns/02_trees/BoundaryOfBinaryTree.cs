// LeetCode 545 - Boundary of Binary Tree
// Difficulty: Medium
// Pattern: DFS, partitioned into three disjoint walks
//
// The boundary is root -> left boundary (top-down) -> leaves (left-to-right)
// -> right boundary (bottom-up). The whole trick is making the three groups
// DISJOINT so nothing is emitted twice:
//
//   - The root is emitted once, up front, and excluded from every walk below.
//   - The left/right boundary walks stop at the first leaf they reach and never
//     emit it -- leaves belong exclusively to the middle group.
//   - The right boundary is collected top-down and reversed at the end.
//
// Boundary walk rule: from root.Left, go Left if it exists, otherwise Right.
// (Mirror for the right boundary.) A node whose left child is missing still has
// a left boundary -- it just bends through the right child.
//
// Edge cases:
//   - null root            -> []
//   - single node          -> [root]  (the root is never treated as a leaf)
//   - root with one child  -> the missing side contributes nothing, so a
//                             fully skewed tree emits each node exactly once
//
// Time: O(n)  Space: O(h) recursion + O(n) output

namespace CodingPatterns.Trees;

public class BinaryTreeBoundary
{
    // ---- Approach 1: three explicit walks (easiest to explain out loud) ----
    public IList<int> BoundaryOfBinaryTree(TreeNode root)
    {
        var result = new List<int>();
        if (root == null) return result;

        // The root always leads, even when it is childless. Everything after
        // this point skips the root, so it can never be duplicated.
        result.Add(root.Val);
        if (IsLeaf(root)) return result;

        AddLeftBoundary(root.Left, result);
        AddLeaves(root, result);
        AddRightBoundary(root.Right, result);
        return result;
    }

    private static bool IsLeaf(TreeNode node)
        => node.Left == null && node.Right == null;

    // Top-down, excluding leaves: emit before descending.
    private static void AddLeftBoundary(TreeNode node, List<int> result)
    {
        while (node != null && !IsLeaf(node))
        {
            result.Add(node.Val);
            node = node.Left ?? node.Right;   // bend right only when left is gone
        }
    }

    // Bottom-up, excluding leaves: collect top-down, then reverse.
    private static void AddRightBoundary(TreeNode node, List<int> result)
    {
        int start = result.Count;
        while (node != null && !IsLeaf(node))
        {
            result.Add(node.Val);
            node = node.Right ?? node.Left;
        }
        result.Reverse(start, result.Count - start);
    }

    // Any left-to-right DFS visits leaves in left-to-right order.
    private static void AddLeaves(TreeNode node, List<int> result)
    {
        if (node == null) return;
        if (IsLeaf(node))
        {
            result.Add(node.Val);
            return;
        }
        AddLeaves(node.Left, result);
        AddLeaves(node.Right, result);
    }

    // ---- Approach 2: one DFS, boundary membership carried as flags ----
    // Same O(n), but touches each node once instead of walking the two spines
    // and then re-walking the whole tree for leaves. The flags encode the same
    // "bend only when the preferred child is missing" rule:
    //   a left child stays on the left boundary;
    //   a right child joins it only if there is no left child.
    public IList<int> BoundaryOfBinaryTreeOnePass(TreeNode root)
    {
        var result = new List<int>();
        if (root == null) return result;

        var left = new List<int>();
        var leaves = new List<int>();
        var right = new List<int>();

        if (!IsLeaf(root))
        {
            Collect(root.Left, true, false, left, leaves, right);
            Collect(root.Right, false, true, left, leaves, right);
        }

        result.Add(root.Val);
        result.AddRange(left);
        result.AddRange(leaves);
        right.Reverse();
        result.AddRange(right);
        return result;
    }

    private static void Collect(TreeNode node, bool onLeft, bool onRight,
                                List<int> left, List<int> leaves, List<int> right)
    {
        if (node == null) return;

        // Leaf check wins over the boundary flags -- that is what keeps a leaf
        // sitting on a spine out of the left/right groups.
        if (IsLeaf(node))
        {
            leaves.Add(node.Val);
            return;
        }

        if (onLeft) left.Add(node.Val);
        else if (onRight) right.Add(node.Val);

        Collect(node.Left, onLeft, onRight && node.Right == null, left, leaves, right);
        Collect(node.Right, onLeft && node.Left == null, onRight, left, leaves, right);
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new BinaryTreeBoundary();

        void Check(string label, TreeNode root, string expected)
        {
            var a = string.Join(",", sol.BoundaryOfBinaryTree(root));
            var b = string.Join(",", sol.BoundaryOfBinaryTreeOnePass(root));
            var ok = a == expected && b == expected;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-18} [{a}]" +
                              (ok ? "" : $"  expected [{expected}]  onePass [{b}]"));
        }

        //   1
        //    \
        //     2
        //    / \
        //   3   4
        Check("right-leaning",
            new TreeNode(1, null, new TreeNode(2, new TreeNode(3), new TreeNode(4))),
            "1,3,4,2");

        //          1
        //        /   \
        //       2     3
        //      / \   /
        //     4   5 6
        //        / \ / \
        //       7  8 9  10
        var root = new TreeNode(1,
            new TreeNode(2,
                new TreeNode(4),
                new TreeNode(5, new TreeNode(7), new TreeNode(8))),
            new TreeNode(3,
                new TreeNode(6, new TreeNode(9), new TreeNode(10)),
                null));
        Check("full example", root, "1,2,4,7,8,9,10,6,3");

        Check("single node", new TreeNode(1), "1");

        // Left-skewed: right boundary is empty, so no node is emitted twice.
        Check("left skewed",
            new TreeNode(1, new TreeNode(2, new TreeNode(3), null), null),
            "1,2,3");

        // Right-skewed: left boundary is empty.
        Check("right skewed",
            new TreeNode(1, null, new TreeNode(2, null, new TreeNode(3))),
            "1,3,2");

        // Left boundary bends through a right child when the left child is gone.
        //   1
        //  /
        // 2
        //  \
        //   3
        //  /
        // 4
        Check("bending spine",
            new TreeNode(1,
                new TreeNode(2, null, new TreeNode(3, new TreeNode(4), null)),
                null),
            "1,2,3,4");

        Check("null root", null, "");
    }
}
