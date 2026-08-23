// LeetCode 875 - Koko Eating Bananas
// Difficulty: Medium
// Pattern: Binary search on answer space
//
// Time: O(n log m)  Space: O(1)

namespace CodingPatterns.BinarySearch;

public class KokoEatingBananas
{
    public int MinEatingSpeed(int[] piles, int h)
    {
        int lo = 1, hi = piles.Max();
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            long hoursNeeded = piles.Sum(p => (long)((p + mid - 1) / mid)); // ceil(p/k) without floating point
            if (hoursNeeded <= h)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }

    public static void Run()
    {
        var sol = new KokoEatingBananas();
        Console.WriteLine(sol.MinEatingSpeed(new[] { 3,6,7,11 }, 8));      // 4
        Console.WriteLine(sol.MinEatingSpeed(new[] { 30,11,23,4,20 }, 5)); // 30
        Console.WriteLine(sol.MinEatingSpeed(new[] { 30,11,23,4,20 }, 6)); // 23
    }
}
