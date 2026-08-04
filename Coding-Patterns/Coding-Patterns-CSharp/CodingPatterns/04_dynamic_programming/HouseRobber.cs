// LeetCode 198 - House Robber
// Difficulty: Medium
// Pattern: 1D DP (space-optimized)
//
// Time: O(n)  Space: O(1)

namespace CodingPatterns.DynamicProgramming;

public class HouseRobber
{
    public int Rob(int[] nums)
    {
        int prev2 = 0, prev1 = 0;
        foreach (var num in nums)
        {
            int curr = Math.Max(prev1, prev2 + num);
            prev2 = prev1;
            prev1 = curr;
        }
        return prev1;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new HouseRobber();

        Console.WriteLine(sol.Rob(new[] { 1, 2, 3, 1 }));       // 4  (1 + 3)
        Console.WriteLine(sol.Rob(new[] { 2, 7, 9, 3, 1 }));    // 12 (2 + 9 + 1)
        Console.WriteLine(sol.Rob(new[] { 5 }));                // 5
        Console.WriteLine(sol.Rob(Array.Empty<int>()));         // 0
    }
}
