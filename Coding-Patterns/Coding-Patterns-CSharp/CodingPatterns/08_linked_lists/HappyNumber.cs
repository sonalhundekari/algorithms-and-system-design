// LeetCode 202 - Happy Number
// Difficulty: Easy
// Pattern: Fast/slow pointers (Floyd's cycle detection) on an implicit list
//
// Repeatedly replace n with the sum of the squares of its digits. n is
// "happy" if that reaches 1; otherwise it falls into a cycle that never
// contains 1.
//
// Key insight: NextNumber is a function, so every n has exactly one
// successor -- the sequence is a linked list whose nodes are numbers and
// whose "pointer" is NextNumber(). It is also bounded (anything below 1000
// maps below 250), so it must eventually repeat. That makes this plain
// cycle detection: meet at 1 -> happy, meet anywhere else -> unhappy.
//
// Every unhappy number lands in the same cycle:
// 4 -> 16 -> 37 -> 58 -> 89 -> 145 -> 42 -> 20 -> 4
//
// Time: O(log n)  Space: O(1)

namespace CodingPatterns.LinkedLists;

public class HappyNumber
{
    public bool IsHappy(int n)
    {
        // Sum of the squares of n's digits.
        static int NextNumber(int n)
        {
            var total = 0;
            while (n > 0)
            {
                var digit = n % 10;
                total += digit * digit;
                n /= 10;
            }
            return total;
        }

        int slow = n, fast = NextNumber(n);
        // Stop when fast reaches 1 (happy) or the pointers meet (cycle).
        while (fast != 1 && slow != fast)
        {
            slow = NextNumber(slow);
            fast = NextNumber(NextNumber(fast));
        }
        return fast == 1;
    }

    // Hash-set version (bonus): easier to reach for in an interview, but it
    // stores every number seen instead of running in constant space.
    public bool IsHappySeen(int n)
    {
        // Sum of the squares of n's digits.
        static int NextNumber(int n)
        {
            var total = 0;
            while (n > 0)
            {
                var digit = n % 10;
                total += digit * digit;
                n /= 10;
            }
            return total;
        }

        var seen = new HashSet<int>();
        while (n != 1 && seen.Add(n))
            n = NextNumber(n);
        return n == 1;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new HappyNumber();

        // The 20 happy numbers in [1, 100].
        var happyUnder100 = new HashSet<int>
        {
            1, 7, 10, 13, 19, 23, 28, 31, 32, 44,
            49, 68, 70, 79, 82, 86, 91, 94, 97, 100
        };

        Check(sol.IsHappy(19) == true, "19 is happy: 19 -> 82 -> 68 -> 100 -> 1");
        Check(sol.IsHappy(2) == false, "2 is not happy: falls into the 4 -> 16 -> ... cycle");
        Check(sol.IsHappy(1) == true, "1 is happy");
        Check(sol.IsHappy(116) == false, "116 is not happy: 116 -> 38 -> 73 -> 58 -> (cycle)");

        for (var i = 1; i <= 100; i++)
        {
            var expected = happyUnder100.Contains(i);
            Check(sol.IsHappy(i) == expected, $"IsHappy({i}) == {expected}");
            Check(sol.IsHappySeen(i) == expected, $"IsHappySeen({i}) == {expected}");
        }

        // Both approaches agree on a wider range.
        for (var i = 1; i < 5000; i++)
            Check(sol.IsHappy(i) == sol.IsHappySeen(i), $"both agree on {i}");

        Console.WriteLine($"19 -> {sol.IsHappy(19)}");
        Console.WriteLine($" 2 -> {sol.IsHappy(2)}");
        Console.WriteLine("All tests passed.");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
            throw new Exception($"FAILED: {label}");
    }
}
