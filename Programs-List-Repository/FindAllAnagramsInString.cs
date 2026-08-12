// LeetCode 438 - Find All Anagrams in a String
// Difficulty: Medium
// Pattern: FIXED-size sliding window + frequency map
//
// Problem: Given strings s and p, return the start indices of every substring
// of s that is an anagram of p.
//
// The window never changes size, so there is no shrink loop: on every step you
// add s[right] and drop s[right - |p|]. That is the whole difference from
// Minimum Window Substring, where the window is variable and the inner while
// loop does the shrinking.
//
// Keeping a `matches` counter (how many of the 26 letters have the right count)
// makes each step O(1) instead of comparing two 26-int arrays per position.
// Compare on the EDGES: a letter enters "matched" exactly when its diff hits 0
// and leaves it exactly when it moves off 0, so only the touched letter needs
// checking.
//
// Time: O(|s|)  Space: O(1) -- 26 counters
namespace CodingPatterns.ArraysStrings;

public class FindAllAnagramsInString
{
    // Single count array holding need[c] - window[c].
    // All 26 diffs are zero  <=>  the window is an anagram of p.
    public IList<int> FindAnagrams(string s, string p)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(p) || p.Length > s.Length)
            return result;

        Span<int> diff = stackalloc int[26];
        foreach (var c in p)
            diff[c - 'a']++;

        // matches = letters whose diff is already 0 (i.e. correct count in window)
        int matches = 0;
        for (int i = 0; i < 26; i++)
            if (diff[i] == 0) matches++;

        for (int right = 0; right < s.Length; right++)
        {
            int inIdx = s[right] - 'a';
            if (diff[inIdx] == 0) matches--;   // was correct, about to break
            diff[inIdx]--;
            if (diff[inIdx] == 0) matches++;   // just became correct

            int left = right - p.Length;
            if (left >= 0)                     // window overgrew -- evict s[left]
            {
                int outIdx = s[left] - 'a';
                if (diff[outIdx] == 0) matches--;
                diff[outIdx]++;
                if (diff[outIdx] == 0) matches++;
            }

            if (matches == 26)
                result.Add(right - p.Length + 1);
        }
        return result;
    }

    // Reference version: two count arrays compared outright.
    // O(26 * |s|) -- same asymptotics but ~26x the constant. Easy to state in an
    // interview first, then optimize into the counter version above.
    public IList<int> FindAnagramsBruteCompare(string s, string p)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(p) || p.Length > s.Length)
            return result;

        var need = new int[26];
        var window = new int[26];
        foreach (var c in p)
            need[c - 'a']++;

        for (int right = 0; right < s.Length; right++)
        {
            window[s[right] - 'a']++;
            int left = right - p.Length;
            if (left >= 0)
                window[s[left] - 'a']--;

            if (right >= p.Length - 1 && need.AsSpan().SequenceEqual(window))
                result.Add(right - p.Length + 1);
        }
        return result;
    }

    public static void Main()
    {
        var sol = new FindAllAnagramsInString();

        void Show(string s, string p)
        {
            var fast = sol.FindAnagrams(s, p);
            var slow = sol.FindAnagramsBruteCompare(s, p);
            var agree = fast.SequenceEqual(slow) ? "" : "  <-- MISMATCH";
            Console.WriteLine($"s=\"{s}\", p=\"{p}\" -> [{string.Join(", ", fast)}]{agree}");
        }

        Show("cbaebabacd", "abc");   // [0, 6]
        Show("abab", "ab");          // [0, 1, 2]
        Show("aa", "bb");            // []
        Show("a", "ab");             // []      p longer than s
        Show("baa", "aa");           // [1]
        Show("aaaaa", "a");          // [0, 1, 2, 3, 4]
    }
}
