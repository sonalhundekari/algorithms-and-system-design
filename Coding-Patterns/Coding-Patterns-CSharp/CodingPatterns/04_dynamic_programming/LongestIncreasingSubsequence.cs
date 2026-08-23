// LeetCode 300 - Longest Increasing Subsequence
// Difficulty: Medium
// Pattern: Patience sort with binary search
//
// Time: O(n log n)  Space: O(n)

namespace CodingPatterns.DynamicProgramming;

public class LongestIncreasingSubsequence
{
    public int LengthOfLIS(int[] nums)
    {
        var tails = new List<int>();
        foreach (var num in nums)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (tails[mid] < num) lo = mid + 1;
                else hi = mid;
            }

            int pos = lo;
            if (pos == tails.Count) tails.Add(num);
            else tails[pos] = num;
        }
        return tails.Count;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new LongestIncreasingSubsequence();

        Console.WriteLine(sol.LengthOfLIS(new[] { 10, 9, 2, 5, 3, 7, 101, 18 }));  // 4
        Console.WriteLine(sol.LengthOfLIS(new[] { 0, 1, 0, 3, 2, 3 }));            // 4
        Console.WriteLine(sol.LengthOfLIS(new[] { 7, 7, 7, 7 }));                  // 1
        Console.WriteLine(sol.LengthOfLIS(Array.Empty<int>()));                    // 0
    }
}
