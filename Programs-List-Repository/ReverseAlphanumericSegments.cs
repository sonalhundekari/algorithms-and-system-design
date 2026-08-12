/*
Reverse Alphanumeric Segments (LeetCode 917 variant)

Reverse each maximal run of alphanumeric characters ([A-Za-z0-9]) in place.
Every other character -- spaces, punctuation, apostrophes -- keeps its original
index.

  "Hello! 2026!!! Let's dance!!!"  ->  "olleH! 6202!!! teL's ecnad!!!"

The apostrophe is the whole problem in miniature. It is not alphanumeric, so it
*splits* the run: "Let's" is two segments, "Let" and "s", reversed
independently -> "teL" + "'" + "s". It is NOT "s'teL".

That split is the difference from LeetCode 917 (Reverse Only Letters), which
looks nearly identical but is a different operation. There, the non-letters are
holes that letters *pass through* -- the letter sequence is reversed as one
global list, so "ab-cd" -> "dc-ba" and the 'a' teleports across the dash. Here
non-alphanumerics are walls, not holes: "ab-cd" -> "ba-dc", and no character
ever crosses one. Reading a two-pointer solution for 917 and adapting it is the
usual way to get this wrong -- 917 runs a left/right pair over the whole string
skipping punctuation, which is exactly the crossing behaviour we must forbid.

So the shape is segment-local, not string-global:

  i = 0
  while i < n:
    if s[i] is not alphanumeric: i++            # fixed point, scan past it
    else:
      j = i;  advance j while s[j] is alphanumeric   # [i, j) is the run
      reverse s[i .. j-1]                            # two pointers, inward
      i = j                                          # resume past the run

Because segments are disjoint and each reverse is confined to its own [i, j),
no write can land outside the run that produced it -- which is precisely the
"non-alphanumerics stay at their original index" requirement, held structurally
rather than checked afterwards.

Complexity
  Time:  O(n) -- the outer scan and the inner reverses together touch each index
         a constant number of times (found once by the scan, swapped at most once).
  Space: O(n) in C# only because strings are immutable and we must materialize a
         char[]; the algorithm itself is O(1) extra.

Definition note: "alphanumeric" here is ASCII letters and digits, matching the
[A-Za-z0-9] in the statement. `char.IsLetterOrDigit` is Unicode-aware and would
also accept 'e', Arabic-Indic digits, etc. -- a superset. IsAsciiLetterOrDigit
keeps us honest about the stated contract; swap it if Unicode runs should count.

Edge cases the loop already absorbs:
  - empty string / all punctuation      -> the else-branch never fires
  - no punctuation at all               -> one segment, a plain full reverse
  - leading and trailing runs           -> nothing special, i starts and ends anywhere
  - single-character runs ("s" in Let's)-> reverse of length 1 is a no-op
  - adjacent separators ("!!!")         -> each is skipped on its own iteration
*/

using System.Text;

namespace CodingPatterns.ArraysStrings;

public class ReverseAlphanumericSegments
{
    public static string ReverseSegments(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        char[] chars = s.ToCharArray();
        int n = chars.Length;
        int i = 0;

        while (i < n)
        {
            if (!IsAlphanumeric(chars[i]))
            {
                i++; // fixed point: stays exactly where it is
                continue;
            }

            // [i, j) is the maximal alphanumeric run starting at i.
            int j = i;
            while (j < n && IsAlphanumeric(chars[j]))
                j++;

            // Two pointers inward, confined to this run.
            for (int left = i, right = j - 1; left < right; left++, right--)
                (chars[left], chars[right]) = (chars[right], chars[left]);

            i = j; // resume past the run, never re-entering it
        }

        return new string(chars);
    }

    private static bool IsAlphanumeric(char c) => char.IsAsciiLetterOrDigit(c);

    public static void Main()
    {
        var cases = new (string Input, string Expected, string Note)[]
        {
            ("Hello! 2026!!! Let's dance!!!", "olleH! 6202!!! teL's ecnad!!!",
                "the worked example -- note teL's, not s'teL"),
            ("ab-cd", "ba-dc",
                "LeetCode 917 would say dc-ba; separators are walls, not holes"),
            ("Let's", "teL's",
                "apostrophe splits the run into 'Let' and 's'"),
            ("2026", "6202",
                "digits are alphanumeric too"),
            ("abc123", "321cba",
                "letters and digits in one run reverse together"),
            ("", "",
                "empty string"),
            ("!!!", "!!!",
                "all separators -- nothing to reverse"),
            ("racecar", "racecar",
                "single run, palindrome"),
            ("a", "a",
                "single character"),
            ("  hi  ", "  ih  ",
                "leading/trailing separators keep their indices"),
            ("a1!b2?c3", "1a!2b?3c",
                "many tiny runs"),
            ("...abc", "...cba",
                "run at the very end"),
            ("abc...", "cba...",
                "run at the very start"),
        };

        Console.WriteLine("Reverse Alphanumeric Segments");
        Console.WriteLine("=============================");
        Console.WriteLine();

        int passed = 0;
        foreach (var (input, expected, note) in cases)
        {
            string actual = ReverseSegments(input);
            bool ok = actual == expected;
            if (ok) passed++;

            Console.WriteLine($"[{(ok ? "OK" : "MISMATCH!")}] \"{input}\" -> \"{actual}\"");
            Console.WriteLine($"       {note}");
            if (!ok)
                Console.WriteLine($"       expected: \"{expected}\"");
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{cases.Length} passed.");

        // Non-alphanumerics must be fixed points -- verify positionally, not by eye.
        Console.WriteLine();
        Console.WriteLine("Separator positions preserved:");
        foreach (var (input, _, _) in cases)
        {
            string actual = ReverseSegments(input);
            var drift = new StringBuilder();
            for (int k = 0; k < input.Length; k++)
                if (!IsAlphanumeric(input[k]) && input[k] != actual[k])
                    drift.Append($" index {k}: '{input[k]}' -> '{actual[k]}'");

            string verdict = drift.Length == 0 ? "all fixed" : drift.ToString();
            Console.WriteLine($"  \"{input}\": {verdict}");
        }
    }
}
