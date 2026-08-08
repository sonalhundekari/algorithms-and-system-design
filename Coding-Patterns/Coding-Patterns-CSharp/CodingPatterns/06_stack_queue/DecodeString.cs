// LeetCode 394 - Decode String
// Difficulty: Medium
// Pattern: Stack of pending frames
//
// Problem: Decode a string of the form k[encoded_string], where the substring
// inside the brackets repeats k times. k may have multiple digits and the
// encodings nest. Input is guaranteed valid; the plain text contains no digits.
//
//   3[a]2[bc]      -> aaabcbc
//   3[a2[c]]       -> accaccacc
//   2[abc]3[cd]ef  -> abcabccdcdcdef
//
// Approach: One left-to-right pass with two stacks. `current` accumulates the
// segment being built at the present nesting depth.
//   digit -> fold into k (k = k * 10 + d handles multi-digit counts)
//   '['   -> a new depth begins: park k and `current`, start a fresh buffer
//   ']'   -> the depth ends: pop the parent buffer and append the just-finished
//            segment to it k times
// The stacks hold exactly one entry per open bracket, so they are O(depth).
//
// Time: O(n + m)  Space: O(m)   [n = input length, m = decoded length]
//
// Follow-up (decoded string too large to hold in memory): see DecodeStreaming.

namespace CodingPatterns.StackQueue;

public class DecodeString
{
    public string Decode(string s)
    {
        var counts = new Stack<int>();       // repetition count per open bracket
        var parents = new Stack<System.Text.StringBuilder>();
        var current = new System.Text.StringBuilder();
        int k = 0;

        foreach (char c in s)
        {
            if (char.IsDigit(c))
            {
                k = k * 10 + (c - '0');
            }
            else if (c == '[')
            {
                counts.Push(k);
                parents.Push(current);
                k = 0;
                current = new System.Text.StringBuilder();
            }
            else if (c == ']')
            {
                var segment = current;
                current = parents.Pop();
                int times = counts.Pop();
                for (int i = 0; i < times; i++)
                    current.Append(segment);
            }
            else
            {
                current.Append(c);
            }
        }

        return current.ToString();
    }

    // ---- Follow-up: bounded extra memory ----
    //
    // Decode() costs O(m) because it materializes the answer, and worse, an inner
    // segment gets copied again at every enclosing level. If the caller only needs
    // to consume the output once (write to a socket, hash it, scan for a pattern),
    // it never has to exist in memory at all.
    //
    // Instead of building text, re-walk the input: keep a stack of
    // (contentStart, remaining) and, on ']', jump the cursor back to contentStart
    // until the count is exhausted. The input string is the only buffer, so extra
    // memory is O(depth) -- independent of the decoded length.
    //
    // Time: O(m)  Space: O(depth)
    public static IEnumerable<char> DecodeStreaming(string s)
    {
        var frames = new Stack<(int Start, int Remaining)>();
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];

            if (char.IsDigit(c))
            {
                int k = 0;
                while (char.IsDigit(s[i]))
                    k = k * 10 + (s[i++] - '0');

                // s[i] is now '[', so the repeated content starts at i + 1.
                frames.Push((i + 1, k));
                i++;
            }
            else if (c == ']')
            {
                var (start, remaining) = frames.Pop();
                remaining--;

                if (remaining > 0)
                {
                    // Another pass over the same span: rewind rather than copy.
                    frames.Push((start, remaining));
                    i = start;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                yield return c;
                i++;
            }
        }
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new DecodeString();

        Console.WriteLine(sol.Decode("3[a]2[bc]"));       // aaabcbc
        Console.WriteLine(sol.Decode("3[a2[c]]"));        // accaccacc
        Console.WriteLine(sol.Decode("2[abc]3[cd]ef"));   // abcabccdcdcdef

        Console.WriteLine(sol.Decode("abc"));             // abc   (no encoding at all)
        Console.WriteLine(sol.Decode("12[a]"));           // 12 a's (multi-digit count)
        Console.WriteLine(sol.Decode("2[2[2[ab]]]"));     // ab x 8
        Console.WriteLine(sol.Decode("x2[y3[z]]w"));      // xyzzzyzzzw

        // The streaming decoder must agree with the materializing one.
        foreach (var input in new[] { "3[a]2[bc]", "3[a2[c]]", "2[abc]3[cd]ef", "x2[y3[z]]w" })
        {
            var streamed = new string(DecodeStreaming(input).ToArray());
            Console.WriteLine($"{input,-16} streamed == decoded: {streamed == sol.Decode(input)}");
        }

        // ...but it never holds the output: this expands to 10^9 characters, and
        // taking the first 10 touches only ~30 bytes of stack.
        var huge = "1000000[10000[ab]]";
        Console.WriteLine(new string(DecodeStreaming(huge).Take(10).ToArray()));  // ababababab
    }
}
