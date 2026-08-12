// Serialize and Deserialize Dictionary Trie
// Difficulty: Medium
// Pattern: Preorder DFS with an explicit "close" marker; stack-based decode
//
// Serialize(words) -> one string.  Deserialize(data) -> the same words, sorted.
//
// THE INSIGHT. A trie is an n-ary tree, and the classic n-ary trick applies: a
// preorder walk on its own is ambiguous ("ab" could be a->b or two siblings), so
// emit ONE extra token per node meaning "this subtree is finished". With that
// token the preorder string is uniquely decodable by a stack -- no lookahead, no
// separators between letters, no recursion in the decoder.
//
// THE GRAMMAR (three token kinds, one character each):
//
//     a..z   descend: create this child of the current node and move into it
//     '$'    the current node ends a word
//     ')'    close the current node and return to its parent
//
// ["app", "apple", "bat"]  ->  "app$le$)))))bat$)))"
//
//     root
//      +- a          "a"
//      |   +- p      "p"
//      |       +- p* "p$"          <- app
//      |           +- l      "l"
//      |               +- e* "e$"  <- apple
//      +- b
//          +- a
//              +- t* "t$"          <- bat
//
// WHY CHILDREN ARE EMITTED IN SORTED ORDER. Two payoffs for one ordering
// decision. The encoding becomes CANONICAL -- insertion order cannot change the
// string, so two dictionaries holding the same words serialize byte-identically
// and can be compared or deduped without decoding. And the rebuilt trie is
// already lexicographic, so collecting the words is a plain preorder walk with no
// final Sort.
//
// A prefix word needs no special case: a node is emitted before its children, so
// "app" comes out before "apple" automatically.
//
// SortedDictionary<char, TrieNode> uses Comparer<char>.Default, which compares
// code points -- ordinal, not culture-aware. That matters here: the ordinal trap
// that bites `List<string>.Sort()` does not apply to char keys, so this ordering
// is the lexicographic one the problem asks for. A char[26]-indexed array is the
// faster alternative (see the follow-ups) and is sorted by construction.
//
//   Time:  O(N) both ways, N = trie nodes <= sum of word lengths
//   Space: O(N) for the trie; the string is 2N + (#words) characters
//
// WHAT TO ASK BEFORE WRITING ANYTHING:
//
//   1. Is the alphabet really lowercase a-z? That is what lets '$' and ')' be
//      unescaped literals. Any wider alphabet needs escaping or binary framing.
//   2. Is "" a possible word? The format handles it (a leading '$' flags the
//      root), but the stated limits say length >= 1.
//   3. Must the format BE the trie, or is any round-trip allowed? string.Join
//      also passes; the trie encoding is what pays off on shared prefixes.

using System.Text;

namespace CodingPatterns.Trees;

public class SerializeDeserializeTrie
{
    private const char WordEnd = '$';   // the node I am in terminates a word
    private const char NodeEnd = ')';   // pop back to the parent

    private sealed class TrieNode
    {
        public readonly SortedDictionary<char, TrieNode> Children = new();
        public bool IsWord;
    }

    public string Serialize(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        Encode(Build(words), sb);
        return sb.ToString();
    }

    public IList<string> Deserialize(string data)
    {
        var words = new List<string>();
        Collect(Decode(data), new StringBuilder(), words);
        return words;
    }

    // ---- trie construction ----

    private static TrieNode Build(IEnumerable<string> words)
    {
        var root = new TrieNode();
        foreach (var word in words)
        {
            var node = root;
            foreach (var ch in word)
            {
                if (!node.Children.TryGetValue(ch, out var next))
                    node.Children[ch] = next = new TrieNode();
                node = next;
            }
            node.IsWord = true;
        }
        return root;
    }

    // The inverse of Encode: ')' pops, a letter pushes, '$' flags. Iterative, so
    // it is immune to however deep the encoded trie happens to be.
    private static TrieNode Decode(string data)
    {
        var root = new TrieNode();
        var stack = new Stack<TrieNode>();
        stack.Push(root);

        foreach (var ch in data)
        {
            if (ch == NodeEnd)
            {
                if (stack.Count == 1)
                    throw new FormatException("malformed encoding: unmatched ')'");
                stack.Pop();
            }
            else if (ch == WordEnd)
            {
                stack.Peek().IsWord = true;
            }
            else
            {
                var child = new TrieNode();
                stack.Peek().Children[ch] = child;
                stack.Push(child);
            }
        }

        if (stack.Count != 1)
            throw new FormatException("malformed encoding: unclosed node");
        return root;
    }

    // ---- the two walks ----

    // Depth is bounded by the longest word (<= 50 here), so recursion is safe.
    private static void Encode(TrieNode node, StringBuilder sb)
    {
        if (node.IsWord) sb.Append(WordEnd);        // at the root this encodes ""
        foreach (var (ch, child) in node.Children)  // SortedDictionary: already sorted
        {
            sb.Append(ch);
            Encode(child, sb);
            sb.Append(NodeEnd);                     // the root is never closed
        }
    }

    private static void Collect(TrieNode node, StringBuilder prefix, List<string> words)
    {
        // Emit before descending, so a prefix precedes what extends it.
        if (node.IsWord) words.Add(prefix.ToString());
        foreach (var (ch, child) in node.Children)
        {
            prefix.Append(ch);
            Collect(child, prefix, words);
            prefix.Length--;                        // pop, no reallocation
        }
    }

    // ---- Tests ----
    public static void Run()
    {
        var codec = new SerializeDeserializeTrie();

        Console.WriteLine(codec.Serialize(new[] { "app", "apple", "bat" }));
        // app$le$)))))bat$)))
        Console.WriteLine(string.Join(",", codec.Deserialize(
            codec.Serialize(new[] { "app", "apple", "bat" }))));   // app,apple,bat
        Console.WriteLine(string.Join(",", codec.Deserialize(
            codec.Serialize(new[] { "dog", "deer", "deal" }))));   // deal,deer,dog

        var cases = new[]
        {
            Array.Empty<string>(),                          // empty dictionary
            new[] { "a" },                                  // single letter
            new[] { "a", "ab", "abc", "abcd" },             // every node is a word
            new[] { "abcd" },                               // no node on the chain is
            new[] { "cat", "car", "card", "care", "dog" },
            Enumerable.Range(0, 26)
                .Select(i => ((char)('a' + i)).ToString())
                .ToArray(),                                 // 26-way fan-out at the root
            new[] { new string('z', 50), new string('z', 49) },   // max depth
        };

        var roundTripped = true;
        foreach (var words in cases)
        {
            var expected = words.OrderBy(w => w, StringComparer.Ordinal).ToList();
            var actual = codec.Deserialize(codec.Serialize(words));
            roundTripped &= actual.SequenceEqual(expected);
        }
        Console.WriteLine(roundTripped);                                    // True

        Console.WriteLine(codec.Serialize(Array.Empty<string>()) == "");     // True
        Console.WriteLine(codec.Deserialize("").Count == 0);                 // True

        // The format can represent the empty word even though the limits exclude it.
        Console.WriteLine(string.Join("|", codec.Deserialize(
            codec.Serialize(new[] { "", "a" }))));                           // |a

        // Malformed input is rejected instead of silently decoding.
        foreach (var bad in new[] { "ab))))", "abc" })
        {
            try { codec.Deserialize(bad); Console.WriteLine($"MISSED: {bad}"); }
            catch (FormatException) { }
        }

        // Randomized round-trip, plus the canonical-encoding property: the string
        // must depend on the SET of words, never on their insertion order.
        var rng = new Random(208);
        var stable = true;
        for (int trial = 0; trial < 500 && stable; trial++)
        {
            var pool = new HashSet<string>();
            for (int i = rng.Next(0, 40); i > 0; i--)
            {
                var len = rng.Next(1, 7);
                var sb = new StringBuilder();
                for (int k = 0; k < len; k++) sb.Append((char)('a' + rng.Next(5)));
                pool.Add(sb.ToString());
            }

            var words = pool.ToList();
            var encoded = codec.Serialize(words);
            stable &= codec.Deserialize(encoded)
                .SequenceEqual(words.OrderBy(w => w, StringComparer.Ordinal));

            for (int i = words.Count - 1; i > 0; i--)      // Fisher-Yates shuffle
            {
                int j = rng.Next(i + 1);
                (words[i], words[j]) = (words[j], words[i]);
            }
            stable &= codec.Serialize(words) == encoded;
        }
        Console.WriteLine(stable);                                           // True

        // Shared prefixes are where the trie encoding beats string.Join.
        var dense = Enumerable.Range(0, 500)
            .Select(i => $"internationalization{i:D4}")
            .ToArray();
        Console.WriteLine(codec.Serialize(dense).Length
                          < string.Join("\n", dense).Length);                // True
    }
}

// ---- Follow-ups ----
//
// "Why not just join the words with newlines?"  For a pure round-trip, nothing --
// it is simpler and, for a dictionary with little prefix overlap, SHORTER. The
// trie encoding costs 2 chars per node while the word list costs 1 char per
// character, so the trie wins exactly when nodes < total characters, i.e. when
// prefixes are genuinely shared. Say that out loud rather than pretending the
// trie is unconditionally better.
//
// "Make it faster / less allocation-heavy."  Replace SortedDictionary with
// `TrieNode[] children = new TrieNode[26]`, indexed by ch - 'a'. Children are then
// sorted by construction, lookup is an array index instead of a red-black descent,
// and Encode just skips nulls. Costs 26 references per node -- worth it for a
// fixed alphabet, wasteful for a sparse one.
//
// "The alphabet is arbitrary (unicode, digits, punctuation)."  '$' and ')' stop
// being safe sentinels. Either escape them (a reserved prefix char, plus escaping
// the escape) or switch to length-prefixed/binary framing: per node write the
// child count, then each child's code point and subtree.
//
// "Ten million words and the file has to be small."  A DAWG (minimized DFA) also
// shares SUFFIXES -- "-ing", "-tion" -- collapsing the trie into a graph, often an
// order of magnitude smaller; build it by hashing subtrees bottom-up and interning
// duplicates. A succinct/LOUDS encoding stores the same trie in ~2 bits + 1 label
// per node and stays searchable without decompressing.
//
// "Query the serialized form without rebuilding."  Children are sorted and each
// subtree is a contiguous span, so you can walk the string directly: to follow a
// letter, scan the current node's children and skip unwanted subtrees by counting
// ')' against letters. Prefix search over the bytes with O(1) memory -- the reason
// the closing marker earns its byte.
