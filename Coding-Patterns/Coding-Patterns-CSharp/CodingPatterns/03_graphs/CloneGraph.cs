// LeetCode 133 - Clone Graph
// Difficulty: Medium
// Pattern: DFS + HashMap
//
// Time: O(V + E)  Space: O(V)

namespace CodingPatterns.Graphs;

public class GraphNode
{
    public int val;
    public IList<GraphNode> neighbors;
    public GraphNode(int v = 0) { val = v; neighbors = new List<GraphNode>(); }
}

public class CloneGraph
{
    private Dictionary<GraphNode, GraphNode> _visited = new();

    public GraphNode CloneGraphNode(GraphNode node)
    {
        if (node == null) return null;
        if (_visited.ContainsKey(node)) return _visited[node];
        var clone = new GraphNode(node.val);
        _visited[node] = clone;
        foreach (var neighbor in node.neighbors)
            clone.neighbors.Add(CloneGraphNode(neighbor));
        return clone;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new CloneGraph();

        // 1 -- 2
        // |    |
        // 4 -- 3
        var n1 = new GraphNode(1);
        var n2 = new GraphNode(2);
        var n3 = new GraphNode(3);
        var n4 = new GraphNode(4);
        n1.neighbors.Add(n2); n1.neighbors.Add(n4);
        n2.neighbors.Add(n1); n2.neighbors.Add(n3);
        n3.neighbors.Add(n2); n3.neighbors.Add(n4);
        n4.neighbors.Add(n1); n4.neighbors.Add(n3);

        var clone = sol.CloneGraphNode(n1);

        Console.WriteLine(clone.val);                       // 1
        Console.WriteLine(ReferenceEquals(clone, n1));      // False -- genuinely copied
        Console.WriteLine(string.Join(",", clone.neighbors.Select(x => x.val)));  // 2,4
        // The clone must be a closed graph: following 1 -> 2 -> 1 lands back on the clone.
        Console.WriteLine(ReferenceEquals(clone.neighbors[0].neighbors[0], clone)); // True
        Console.WriteLine(sol.CloneGraphNode(null) == null);  // True
    }
}
