// LeetCode 297 - Serialize and Deserialize Binary Tree
// Difficulty: Hard
// Pattern: BFS encoding
//
// Time: O(n)  Space: O(n)

namespace CodingPatterns.Trees;

public class SerializeDeserialize
{
    public string Serialize(TreeNode root)
    {
        if (root == null) return "[]";
        var sb = new System.Text.StringBuilder();
        var queue = new Queue<TreeNode>();
        queue.Enqueue(root);
        sb.Append("[");
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (sb.Length > 1) sb.Append(",");
            if (node == null) { sb.Append("null"); continue; }
            sb.Append(node.Val);
            queue.Enqueue(node.Left);
            queue.Enqueue(node.Right);
        }
        sb.Append("]");
        return sb.ToString();
    }

    public TreeNode Deserialize(string data)
    {
        data = data.Trim('[', ']');
        if (string.IsNullOrEmpty(data)) return null;
        var vals = data.Split(',');
        var root = new TreeNode(int.Parse(vals[0]));
        var queue = new Queue<TreeNode>();
        queue.Enqueue(root);
        int i = 1;
        while (queue.Count > 0 && i < vals.Length)
        {
            var node = queue.Dequeue();
            if (vals[i] != "null")
            { node.Left = new TreeNode(int.Parse(vals[i])); queue.Enqueue(node.Left); }
            i++;
            if (i < vals.Length && vals[i] != "null")
            { node.Right = new TreeNode(int.Parse(vals[i])); queue.Enqueue(node.Right); }
            i++;
        }
        return root;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new SerializeDeserialize();

        var root = new TreeNode(1,
            new TreeNode(2),
            new TreeNode(3, new TreeNode(4), new TreeNode(5)));

        var encoded = sol.Serialize(root);
        Console.WriteLine(encoded);

        // Round-tripping must reproduce the same encoding.
        var decoded = sol.Deserialize(encoded);
        Console.WriteLine(sol.Serialize(decoded) == encoded);   // True

        Console.WriteLine(sol.Serialize(null));                 // []
        Console.WriteLine(sol.Deserialize("[]") == null);       // True
    }
}
