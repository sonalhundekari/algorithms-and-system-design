// LeetCode 236 - Lowest Common Ancestor of a Binary Tree
// Difficulty: Medium
// Pattern: DFS post-order
//
// Approach: Post-order DFS. Return node if it matches p or q.
// If both left and right are non-null, current node is LCA.
//
// Time: O(n)  Space: O(h)

namespace CodingPatterns.Trees;

public class LowestCommonAncestor
{
    public TreeNode LCA(TreeNode root, TreeNode p, TreeNode q)
    {
        if (root == null || root == p || root == q) return root;
        var left = LCA(root.Left, p, q);
        var right = LCA(root.Right, p, q);
        if (left != null && right != null) return root;
        return left ?? right;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new LowestCommonAncestor();

        //        3
        //       / \
        //      5   1
        //     / \
        //    6   2
        var six = new TreeNode(6);
        var two = new TreeNode(2);
        var five = new TreeNode(5, six, two);
        var one = new TreeNode(1);
        var root = new TreeNode(3, five, one);

        Console.WriteLine(sol.LCA(root, five, one).Val);   // 3
        Console.WriteLine(sol.LCA(root, six, two).Val);    // 5
        Console.WriteLine(sol.LCA(root, five, six).Val);   // 5 (a node is its own ancestor)
    }
}
