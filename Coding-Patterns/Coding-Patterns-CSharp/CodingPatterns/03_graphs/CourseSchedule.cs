// LeetCode 207 - Course Schedule
// Difficulty: Medium
// Pattern: Topological sort (Kahn's BFS)
//
// Time: O(V + E)  Space: O(V + E)

namespace CodingPatterns.Graphs;

public class CourseSchedule
{
    public bool CanFinish(int numCourses, int[][] prerequisites)
    {
        var inDegree = new int[numCourses];
        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++) adj[i] = new List<int>();

        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0) queue.Enqueue(i);

        int completed = 0;
        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            completed++;
            foreach (var next in adj[course])
                if (--inDegree[next] == 0) queue.Enqueue(next);
        }
        return completed == numCourses;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new CourseSchedule();

        Console.WriteLine(sol.CanFinish(2, new[] { new[] { 1, 0 } }));                        // True
        Console.WriteLine(sol.CanFinish(2, new[] { new[] { 1, 0 }, new[] { 0, 1 } }));        // False (cycle)
        Console.WriteLine(sol.CanFinish(3, Array.Empty<int[]>()));                            // True
        Console.WriteLine(sol.CanFinish(4, new[] { new[] { 1, 0 }, new[] { 2, 1 }, new[] { 3, 2 } })); // True
    }
}
