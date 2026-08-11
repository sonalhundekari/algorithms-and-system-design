// Min Coins to Pay When Change Is Allowed (Coin Change with overpayment)
// Difficulty: Medium (Hard if you don't spot the overpayment branch)
// Pattern: 1D unbounded-knapsack DP + a bounded search over how much to overpay
//
// Denominations {1, 5, 10, 50, 100, 200}, unlimited supply. Pay an exact amount
// n -- except you are allowed to OVERPAY, and the recipient hands back exact
// change out of the same denominations. Minimize the total number of coins that
// change hands: coins you paid PLUS coins you got back.
//
//     n = 41  ->  3     pay 50 + 1 = 51, get a 10 back.  2 paid + 1 returned
//
// This is the whole trap. Plain LeetCode 322 on 41 gives 5 (10+10+10+10+1), and
// candidates who pattern-match to 322 and stop there fail the round. The coins
// coming back are coins too, but they are cheap: one 50 buys you four 10s.
//
// THE REDUCTION. A transaction is fully described by what you hand over. If you
// hand over `paid >= n`, the change is forced -- it is exactly `paid - n`, and
// both sides will use the fewest coins they can. So:
//
//     answer = min over paid >= n of  Coins(paid) + Coins(paid - n)
//
// where Coins(x) is ordinary 322: fewest coins summing to exactly x. The two
// halves are independent -- the payer's pile and the recipient's pile are
// separate -- which is why this decomposes so cleanly.
//
// HOW FAR UP DOES `paid` GO? Unbounded, `paid` is infinite; we need a cutoff.
// Write paid = n + c, so c >= 0 is the change amount, and minimize
// Coins(n + c) + Coins(c). For this denomination set the largest coin D = 200
// satisfies
//
//     Coins(x + 200) = Coins(x) + 1        (one more 200, same remainder)
//
// so if c >= 200 then
//
//     Coins(n + c) + Coins(c) = Coins(n + c - 200) + Coins(c - 200) + 2
//
// -- strictly worse by 2 coins. Handing over an extra 200 and getting a 200
// straight back is never anything but two wasted coins. So c in [0, 199] is
// enough; the loop below runs c in [0, 200] because the extra iteration costs
// nothing and makes the bound obviously safe. State this bound out loud; "I'll
// try a few hundred over" is the part interviewers push on.
//
// WHY GREEDY IS SAFE HERE (and only here). {1, 5, 10, 50, 100, 200} is a
// divisibility chain: 1|5, 5|10, 10|50, 50|100, 100|200. When each coin divides
// the next, no optimal solution can hold d[i+1]/d[i] coins of d[i] -- swap them
// for one d[i+1] and you saved coins. So everything below d[j] sums to less
// than d[j], which forces the count of d[j] to be exactly floor(rest / d[j]):
// greedy. Say this before using greedy. For a non-canonical set ({1,3,4}, 6:
// greedy 4+1+1 = 3 coins, optimal 3+3 = 2) you must use the DP.
//
// Time:  O((n + D) * denoms.Length) to build the table, O(D) for the sweep
// Space: O(n + D)

namespace CodingPatterns.DynamicProgramming;

public class MinCoinsWithChange
{
    private static readonly int[] Denoms = { 1, 5, 10, 50, 100, 200 };

    /// <summary>Fewest coins exchanged in total (paid + returned) to settle n.</summary>
    public int Solve(int n, int[]? denoms = null)
    {
        denoms ??= Denoms;
        if (n <= 0) return 0;

        int maxDenom = denoms.Max();
        var dp = CoinsTable(n + maxDenom, denoms);

        // c is the change handed back; paid = n + c.
        int best = int.MaxValue;
        for (int c = 0; c <= maxDenom; c++)
            if (dp[n + c] != int.MaxValue && dp[c] != int.MaxValue)
                best = Math.Min(best, dp[n + c] + dp[c]);

        return best;
    }

    /// <summary>
    /// dp[x] = fewest coins summing to exactly x, for every x in [0, limit].
    /// Plain LeetCode 322, tabulated once for all amounts instead of re-solved
    /// per amount -- that reuse is what keeps the outer sweep cheap.
    /// </summary>
    public static int[] CoinsTable(int limit, int[] denoms)
    {
        var dp = new int[limit + 1];
        Array.Fill(dp, int.MaxValue);
        dp[0] = 0;

        for (int x = 1; x <= limit; x++)
            foreach (var coin in denoms)
                if (coin <= x && dp[x - coin] != int.MaxValue)
                    dp[x] = Math.Min(dp[x], dp[x - coin] + 1);

        return dp;
    }

    /// <summary>
    /// Same value as CoinsTable(x, ...)[x], but O(denoms.Length) and no table.
    /// Correct ONLY because this denomination set is a divisibility chain --
    /// see the header. The answer to "can you do it without the DP?".
    /// </summary>
    public static int CoinsToMakeGreedy(int x, int[]? denoms = null)
    {
        denoms ??= Denoms;
        int count = 0;
        foreach (var coin in denoms.OrderByDescending(c => c))
        {
            count += x / coin;
            x %= coin;
        }
        return count;
    }

    /// <summary>
    /// The same sweep, but it keeps the winning `paid` and reconstructs both
    /// piles, so you can show the actual transaction and not just a number.
    /// </summary>
    public (int Total, int Paid, Dictionary<int, int> Out, Dictionary<int, int> Back) SolveExplained(
        int n, int[]? denoms = null)
    {
        denoms ??= Denoms;
        if (n <= 0) return (0, 0, new Dictionary<int, int>(), new Dictionary<int, int>());

        int maxDenom = denoms.Max();
        var dp = CoinsTable(n + maxDenom, denoms);

        int bestTotal = dp[n], bestPaid = n;
        for (int c = 1; c <= maxDenom; c++)
        {
            if (dp[n + c] == int.MaxValue || dp[c] == int.MaxValue) continue;
            if (dp[n + c] + dp[c] < bestTotal)
            {
                bestTotal = dp[n + c] + dp[c];
                bestPaid = n + c;
            }
        }

        return (bestTotal, bestPaid, Breakdown(bestPaid, denoms), Breakdown(bestPaid - n, denoms));
    }

    /// <summary>Which coins make up x, greedily (valid on a divisibility chain).</summary>
    private static Dictionary<int, int> Breakdown(int x, int[] denoms)
    {
        var outCoins = new Dictionary<int, int>();
        foreach (var coin in denoms.OrderByDescending(c => c))
        {
            if (x < coin) continue;
            outCoins[coin] = x / coin;
            x %= coin;
        }
        return outCoins;
    }

    public static void Run()
    {
        var sol = new MinCoinsWithChange();

        // The interview example, worked by hand in the header.
        Console.WriteLine(sol.Solve(41));                    // 3  (pay 50+1, get 10 back)
        Console.WriteLine(CoinsTable(41, Denoms)[41]);       // 5  <- what plain LC 322 answers

        // Edge cases.
        Console.WriteLine(sol.Solve(0));                     // 0  nothing changes hands
        Console.WriteLine(sol.Solve(199));                   // 2  pay 200, get 1 back
        Console.WriteLine(sol.Solve(200));                   // 1  multiple of the largest coin
        Console.WriteLine(sol.Solve(3));                     // 3  overpaying can't help

        // Greedy agrees with the DP on this set, and is wrong on a non-canonical one.
        var dp = CoinsTable(3000, Denoms);
        Console.WriteLine(Enumerable.Range(0, 3001).All(x => CoinsToMakeGreedy(x) == dp[x])); // True
        Console.WriteLine(CoinsToMakeGreedy(6, new[] { 1, 3, 4 }));                           // 3  (4+1+1)
        Console.WriteLine(CoinsTable(6, new[] { 1, 3, 4 })[6]);                               // 2  (3+3)

        // Show the actual transactions.
        foreach (var n in new[] { 0, 1, 3, 41, 199, 200, 244, 999 })
        {
            var (total, paid, outCoins, back) = sol.SolveExplained(n);
            Console.WriteLine($"n={n,4}  pay {paid,4} as {Fmt(outCoins)}, get back {Fmt(back)}  ->  {total} coins");
        }
    }

    private static string Fmt(Dictionary<int, int> coins) =>
        coins.Count == 0 ? "{}" : "{" + string.Join(", ", coins.Select(kv => $"{kv.Key}x{kv.Value}")) + "}";
}
