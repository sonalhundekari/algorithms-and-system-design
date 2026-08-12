// LeetCode 2060 - Check if an Original String Exists Given Two Encoded Strings
// Difficulty: Hard
// Pattern: Top-down DP over (i, j, diff) -- two pointers plus a "how far ahead
//          is one side" counter that absorbs wildcard characters.
//
// An original string is encoded by replacing some non-empty substrings with
// their lengths: "internationalization" -> "i18n". Given two encodings s1 and
// s2 (lowercase letters and digits 1-9, length <= 40, at most 3 digits in a
// row), decide whether SOME original string could produce both.
//
// Two things make this harder than it looks:
//   1. A run of digits is ambiguous. "123" can be one hidden segment of length
//      123, or 12 + 3, or 1 + 23, or 1 + 2 + 3 -- because adjacent hidden
//      segments concatenate in the encoding, there is no way to tell them apart.
//      A run of k digits has 2^(k-1) readings; k <= 3, so at most 4.
//   2. Hidden segments are WILDCARDS. A length-5 segment matches any 5
//      characters, so it can swallow literal letters on the other side without
//      any equality test. That is why "l123e" and "44" agree: 4 + 4 = 8 hidden
//      characters on the right absorb 'l', 'e' and the 6 hidden ones on the left.
//
// The state
// ---------
// Walk both encodings left to right and track ONE number:
//
//   diff = (characters of the original that s1 has already committed to)
//        - (characters of the original that s2 has already committed to)
//
//   diff > 0  ->  s1 is ahead by `diff` UNMATCHED WILDCARD slots; s2 owes that
//                 many characters, and s1 does not care what they are.
//   diff < 0  ->  mirror image; s1 owes -diff characters.
//   diff == 0 ->  both sides are at the same position of the original, so the
//                 next characters must literally agree.
//
// Only the side that is BEHIND may advance -- that is what keeps the walk in
// step, and it is the whole algorithm:
//
//   s1[i] is a digit  -> take any prefix of that digit run as one number v,
//                        advance i past it, diff += v          (s1 moves ahead)
//   else s2[j] digit  -> same on the other side,   diff -= v
//   else diff == 0    -> two literals face each other: they must be EQUAL,
//                        advance both, diff stays 0
//   else diff > 0     -> s2 must produce one character; whatever letter s2[j]
//                        is, an s1 wildcard covers it: j++, diff--
//   else (diff < 0)   -> mirror: i++, diff++
//
// Answer: i == s1.Length && j == s2.Length && diff == 0. Running out on one
// side with diff != 0 means one encoding still owes characters -- a fail.
//
// Why this terminates in polynomial time: |diff| can never usefully exceed 999.
// A digit run is at most 3 characters, so one run contributes at most 999; and
// a run is only ever entered from a state with diff <= 0 (for s1) or diff >= 0
// (for s2), because reaching a digit means the previous step consumed a literal,
// which moves diff toward 0. States: 41 * 41 * 1999, transitions O(3) each.
//
// Worked example -- s1 = "l123e", s2 = "44":
//   'l' vs '4'   : s2 is a digit -> read 4,     diff = -4   (s2 ahead by 4)
//   'l'          : diff < 0      -> s1 eats 'l', diff = -3
//   '123'        : s1 is a digit -> read 1,      diff = -2
//                                   read 2,      diff = 0
//                                   read 3,      diff = +3
//   'e' vs '4'   : s2 is a digit -> read 4,      diff = -1
//   'e'          : diff < 0      -> s1 eats 'e', diff = 0
//   both exhausted, diff == 0 -> TRUE (original length 8, e.g. "labcdefe")
//
// Traps:
//   - Compare letters ONLY when diff == 0. Comparing s1[i] to s2[j] while one
//     side is ahead is the classic wrong answer: those letters are being eaten
//     by a wildcard, not matched against each other.
//   - Don't match numbers against numbers. A number is not a token to pair up;
//     it is a length to add to a running balance.
//   - Enumerate the digit-run splits by extending the value one digit at a time
//     inside the recursion (v = v * 10 + d, recurse after each step). Writing a
//     separate "all partitions of the run" helper is where off-by-ones live.
//   - Digits are 1-9, so every hidden segment has length >= 1 and there are no
//     leading-zero readings to worry about.
//   - Memoise on all THREE coordinates. (i, j) alone is wrong -- the same pair
//     of positions behaves differently depending on the outstanding balance.
//
// Time: O(n1 * n2 * D * d) with D = 2000 the diff range and d <= 3 the digits
//       per run -- about 10^7 in the worst case, and the reachable set is far
//       smaller. Space: O(n1 * n2 * D) for the memo.

namespace CodingPatterns.DynamicProgramming;

public class OriginalStringExists
{
    // |diff| is bounded by the largest readable number, 999; 1000 gives slack.
    private const int MaxDiff = 1000;

    public bool PossiblyEquals(string s1, string s2)
    {
        // memo[i, j, diff + MaxDiff]: 0 = unknown, 1 = true, 2 = false.
        var memo = new byte[s1.Length + 1, s2.Length + 1, 2 * MaxDiff + 1];
        return Solve(s1, s2, 0, 0, 0, memo);
    }

    private static bool Solve(string s1, string s2, int i, int j, int diff, byte[,,] memo)
    {
        // Both encodings consumed: they describe the same original iff neither
        // still owes the other any characters.
        if (i == s1.Length && j == s2.Length) return diff == 0;
        if (Math.Abs(diff) > MaxDiff) return false;   // unreachable in practice

        var slot = diff + MaxDiff;
        if (memo[i, j, slot] != 0) return memo[i, j, slot] == 1;

        var ok = false;

        if (i < s1.Length && char.IsDigit(s1[i]))
        {
            // Every reading of the digit run starting at i: consume one more
            // digit into the number each time and recurse. s1 moves ahead by v.
            var value = 0;
            for (var k = i; k < s1.Length && k < i + 3 && char.IsDigit(s1[k]); k++)
            {
                value = value * 10 + (s1[k] - '0');
                if (Solve(s1, s2, k + 1, j, diff + value, memo)) { ok = true; break; }
            }
        }
        else if (j < s2.Length && char.IsDigit(s2[j]))
        {
            // Mirror image; s2 moves ahead, so diff goes down.
            var value = 0;
            for (var k = j; k < s2.Length && k < j + 3 && char.IsDigit(s2[k]); k++)
            {
                value = value * 10 + (s2[k] - '0');
                if (Solve(s1, s2, i, k + 1, diff - value, memo)) { ok = true; break; }
            }
        }
        else if (diff == 0)
        {
            // Neither side has an outstanding balance, so these two literals sit
            // at the SAME index of the original and must be identical.
            if (i < s1.Length && j < s2.Length && s1[i] == s2[j])
                ok = Solve(s1, s2, i + 1, j + 1, 0, memo);
        }
        else if (diff > 0)
        {
            // s1 is ahead: one of its wildcard slots swallows s2's next letter,
            // whatever that letter is. No comparison happens here.
            if (j < s2.Length) ok = Solve(s1, s2, i, j + 1, diff - 1, memo);
        }
        else
        {
            if (i < s1.Length) ok = Solve(s1, s2, i + 1, j, diff + 1, memo);
        }

        memo[i, j, slot] = (byte)(ok ? 1 : 2);
        return ok;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new OriginalStringExists();

        void Check(string s1, string s2, bool expected, string note = "")
        {
            var got = sol.PossiblyEquals(s1, s2);
            // The relation is symmetric, so the mirrored call must agree.
            var mirrored = sol.PossiblyEquals(s2, s1);
            var ok = got == expected && mirrored == expected;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {$"\"{s1}\" vs \"{s2}\"",-34} {got,-5}" +
                              (ok ? $"  {note}" : $"  expected {expected}  mirrored {mirrored}"));
        }

        Check("internationalization", "i18n", true, "i + 18 hidden + n");
        Check("l123e", "44", true, "4+4 hidden absorbs 'l' and 'e'");
        Check("a5b", "c5b", false, "literal a vs c at index 0");
        Check("112s", "g841", true, "LC #4");
        Check("ab", "a2", false, "lengths 2 vs 3 are forced");

        Check("a", "a", true, "no digits at all");
        Check("a", "b", false, "no digits, mismatch");
        Check("abc", "12", true, "12 reads as 1+2 = 3 wildcards");
        Check("abc", "1c", false, "'1c' is exactly 2 characters long");
        Check("aaa", "3", true, "one 3-wide wildcard");
        Check("aaa", "4", false, "3 letters cannot fill 4 slots");
        Check("1", "2", false, "fixed lengths 1 and 2");
        Check("11", "2", true, "1+1 == 2");
        Check("1a", "b1", true, "wildcards cross: original \"ba\"");
        Check("a1", "b1", false, "both start with a literal, a != b");
        Check("ab", "1b", true, "the wildcard covers 'a'");
        Check("999", "999", true, "same run, same reading");
        Check("999", "998", false, "reachable lengths are disjoint");
        Check("v", "v", true, "single letter");
        Check("9", "18", true, "18 also reads as 1 + 8 == 9");

        // The last two are worth re-deriving by hand in an interview, because
        // both sides are pure wildcards and the whole question collapses to
        // "do the two runs share a reachable total length?":
        //   "999" -> {999, 9+99 = 108, 99+9 = 108, 9+9+9 = 27}
        //   "998" -> {998, 9+98 = 107, 99+8 = 107, 9+9+8 = 26}   disjoint -> false
        //   "9"   -> {9}      "18" -> {18, 1+8 = 9}              overlap  -> true

        // Worst case for the state space: 40 characters of digits on both sides.
        var wide = new string('9', 40);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var big = sol.PossiblyEquals(wide, wide);
        timer.Stop();
        Console.WriteLine($"{(big ? "PASS" : "FAIL")}  {"40 nines vs 40 nines",-34} {big,-5}" +
                          $"  {timer.ElapsedMilliseconds} ms");
    }
}
