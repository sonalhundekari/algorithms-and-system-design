// LeetCode 102 - Binary Tree Level Order Traversal
// Difficulty: Medium
// Pattern: BFS with queue
//
// Time: O(n)  Space: O(n)

namespace CodingPatterns.Trees;

public class LevelOrderTraversal
{
    public IList<IList<int>> LevelOrder(TreeNode root)
    {
        var result = new List<IList<int>>();
        if (root == null) return result;

        var queue = new Queue<TreeNode>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            int levelSize = queue.Count;
            var level = new List<int>();
            for (int i = 0; i < levelSize; i++)
            {
                var node = queue.Dequeue();
                level.Add(node.Val);
                if (node.Left != null)  queue.Enqueue(node.Left);
                if (node.Right != null) queue.Enqueue(node.Right);
            }
            result.Add(level);
        }
        return result;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new LevelOrderTraversal();

        //      3
        //     / \
        //    9  20
        //      /  \
        //     15   7
        var root = new TreeNode(3,
            new TreeNode(9),
            new TreeNode(20, new TreeNode(15), new TreeNode(7)));

        foreach (var level in sol.LevelOrder(root))
            Console.WriteLine($"[{string.Join(",", level)}]");   // [3] [9,20] [15,7]

        Console.WriteLine(sol.LevelOrder(null).Count);           // 0
    }
}
