// LeetCode 212 - Word Search II
// Difficulty: Hard
// Pattern: DFS/backtracking on a grid, driven by a Trie of the dictionary
//
/*
Contract: m x n grid of letters, a word list, return every word that can be spelled
by walking 4-directionally between adjacent cells without reusing a cell within one
word. Cells CAN be reused across different words. Order of the output does not matter,
and each word appears at most once even if the list contains duplicates.

Questions worth asking before writing code:
  1. Alphabet -- lowercase a-z only? (LC says yes; that is what lets the trie use a
     26-slot array instead of a dictionary. If it were full Unicode, swap in a
     Dictionary<char, TrieNode> and everything else stands.)
  2. May I mutate the board? Marking visited in place is the cheapest visited-set
     there is. If the board is shared/read-only, use a bool[,] -- O(m*n) extra space.
  3. Duplicates in `words`? Trie insertion collapses them for free.
  4. Board size vs word count -- the whole reason this problem exists.

WHY A TRIE (the actual point of the problem):
The naive answer is "run Word Search I (LC 79) once per word": for each word, DFS the
whole board looking for it. That is O(W * m * n * 4^L) -- with W = 30,000 words this is
hopeless, and it redoes the same prefix walk over and over. Ten thousand words starting
with "ba" each re-walk every "ba" path on the board independently.

Flip the loop: put the DICTIONARY in a trie and walk the BOARD once. Now a single DFS
carries a trie pointer alongside the (r, c) position. Every board path is explored at
most once, and the instant the current path's prefix leaves the trie the branch dies.
That prefix pruning is what makes 12x12 with tens of thousands of words fast: the search
tree is bounded by the shape of the dictionary, not by 4^L.

THREE OPTIMIZATIONS THAT MATTER IN AN INTERVIEW (all implemented below):
  a) Terminal-node harvesting -- store the word STRING on its terminal node instead of
     tracking a StringBuilder path. Found a node with a word? Emit it, then null it out
     so the same word is never emitted twice.
  b) Leaf pruning -- when a DFS call returns and its node has no children left and no
     word, unlink it from its parent. Words already found stop costing anything, and the
     trie shrinks as the scan proceeds. This is the difference between "passes" and
     "TLE" on adversarial inputs (e.g. the classic 'a'*n board with a*n-style words).
  c) Board-frequency prefilter + reversed insertion -- skip words the board's letter
     multiset cannot even supply, and insert a word backwards when its last letter is
     rarer on the board than its first. Same paths (adjacency is symmetric) but the DFS
     starts from the rarer end, so far fewer roots survive step one.
     The trap in (c): once some words are stored backwards, two DIFFERENT words can share
     a terminal node -- "ba" inserted forwards and "ab" inserted backwards both end at the
     path b-a. A one-string-per-node payload loses whichever was inserted second, and the
     bug is invisible on the LeetCode samples. Hence List<string> on the node. If asked to
     keep it simple, drop the reversal and a single string is safe again.

Complexity:

Build: O(sum of word lengths) time and space for the trie.
Search: O(m * n * 4^L) worst case, L = longest word -- the standard bound, from starting
  a DFS at every cell and branching 4 ways (3 after the first step, since you cannot walk
  straight back). Tighter and more honest: the DFS can only follow paths that exist in the
  trie, so real cost tracks the number of distinct board-paths that spell a dictionary
  prefix, which is why this runs fast in practice.
Space: O(sum of word lengths) for the trie + O(L) recursion depth. Visited marking is
  in-place, so no extra grid.

Note the asymmetry the pruning buys: cost scales with dictionary SHAPE (shared prefixes
are walked once), not dictionary SIZE. 30,000 words over a 26-letter alphabet share
prefixes heavily, so the trie is much smaller than the raw character count suggests.
*/

using System.Diagnostics;
using System.Text;

namespace CodingPatterns.Graphs;

public class WordSearchII
{
    private const char Visited = '#';   // sentinel parked in the cell during recursion

    private sealed class TrieNode
    {
        public readonly TrieNode?[] Children = new TrieNode?[26];

        /// <summary>
        /// Non-null only on a terminal node; holds the word(s) in ORIGINAL orientation.
        /// A list, not a single string, because of the reversed-insertion trick below: "ba"
        /// stored forwards and "ab" stored backwards both terminate on the path b-a, and a
        /// single slot would let the second insert clobber the first -- silently dropping a
        /// word from the answer. At most two distinct words can collide here (a word and its
        /// reverse), so the list stays tiny.
        /// </summary>
        public List<string>? Words;

        /// <summary>Live child count, kept in sync so leaf pruning is O(1) instead of a 26-scan.</summary>
        public int ChildCount;
    }

    public IList<string> FindWords(char[][] board, string[] words)
    {
        var results = new List<string>();
        if (board is null || board.Length == 0 || board[0] is null || board[0].Length == 0)
            return results;
        if (words is null || words.Length == 0)
            return results;

        int rows = board.Length;
        int cols = board[0].Length;

        var boardCounts = CountBoardLetters(board);
        var root = BuildTrie(words, boardCounts);
        if (root.ChildCount == 0)   // every word was filtered out before we touched the grid
            return results;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Dfs(board, r, c, rows, cols, root, results);

                // The trie emptied out -- every word has been found, nothing left to match.
                if (root.ChildCount == 0)
                    return results;
            }
        }

        return results;
    }

    private void Dfs(char[][] board, int r, int c, int rows, int cols, TrieNode parent, List<string> results)
    {
        char ch = board[r][c];
        int idx = Index(ch);            // also rejects the Visited sentinel
        if (idx < 0)
            return;

        var node = parent.Children[idx];
        if (node is null)               // prefix pruning: this path spells nothing in the dictionary
            return;

        if (node.Words is not null)
        {
            // Reaching this node proves a board path spells it. Every word parked here is
            // findable: the ones stored forwards spell that path, the ones stored backwards
            // spell it in reverse -- and the reversed path is just as walkable.
            results.AddRange(node.Words);
            node.Words = null;          // dedupe: never emit the same word twice
        }

        board[r][c] = Visited;          // claim the cell for the duration of this path

        if (r > 0) Dfs(board, r - 1, c, rows, cols, node, results);
        if (r + 1 < rows) Dfs(board, r + 1, c, rows, cols, node, results);
        if (c > 0) Dfs(board, r, c - 1, rows, cols, node, results);
        if (c + 1 < cols) Dfs(board, r, c + 1, rows, cols, node, results);

        board[r][c] = ch;               // backtrack: the cell is free for other words

        // Leaf pruning -- this branch is spent, unlink it so future DFS calls die sooner.
        if (node.ChildCount == 0 && node.Words is null)
        {
            parent.Children[idx] = null;
            parent.ChildCount--;
        }
    }

    // ---- Trie construction (with the two cheap prefilters) ----

    private static TrieNode BuildTrie(string[] words, int[] boardCounts)
    {
        var root = new TrieNode();

        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(word) || !Formable(word, boardCounts))
                continue;

            // Start from whichever end is rarer on the board. Adjacency is symmetric, so a
            // path spelling the reversal exists exactly when a path spelling the word does --
            // but seeding the DFS on the rare letter kills most starting cells immediately.
            bool reversed = boardCounts[Index(word[^1])] < boardCounts[Index(word[0])];

            var node = root;
            for (int i = 0; i < word.Length; i++)
            {
                int idx = Index(reversed ? word[word.Length - 1 - i] : word[i]);
                var child = node.Children[idx];
                if (child is null)
                {
                    child = new TrieNode();
                    node.Children[idx] = child;
                    node.ChildCount++;
                }
                node = child;
            }

            // Original orientation, so the caller gets back exactly what they asked for.
            // The Contains guard collapses duplicate entries in `words` (the list holds at
            // most two items, so this is free).
            node.Words ??= new List<string>(1);
            if (!node.Words.Contains(word, StringComparer.Ordinal))
                node.Words.Add(word);
        }

        return root;
    }

    /// <summary>Cheap reject: the board simply does not hold enough of some letter.</summary>
    private static bool Formable(string word, int[] boardCounts)
    {
        Span<int> need = stackalloc int[26];
        foreach (char ch in word)
        {
            int idx = Index(ch);
            if (idx < 0)
                return false;           // letter outside the board's alphabet
            if (++need[idx] > boardCounts[idx])
                return false;
        }
        return true;
    }

    private static int[] CountBoardLetters(char[][] board)
    {
        var counts = new int[26];
        foreach (var row in board)
        {
            foreach (char ch in row)
            {
                int idx = Index(ch);
                if (idx >= 0)
                    counts[idx]++;
            }
        }
        return counts;
    }

    private static int Index(char ch) => ch is >= 'a' and <= 'z' ? ch - 'a' : -1;

    // ---- Tests ----
    public static void Run()
    {
        var sol = new WordSearchII();

        // LeetCode example 1
        var board1 = new[]
        {
            "oaan".ToCharArray(),
            "etae".ToCharArray(),
            "ihkr".ToCharArray(),
            "iflv".ToCharArray(),
        };
        Print("example 1", sol.FindWords(board1, new[] { "oath", "pea", "eat", "rain" }));
        // expected: eat, oath

        // LeetCode example 2 -- no word is formable
        var board2 = new[]
        {
            "ab".ToCharArray(),
            "cd".ToCharArray(),
        };
        Print("example 2", sol.FindWords(board2, new[] { "abcb" }));   // expected: (none)

        // Cells may be reused ACROSS words but not within one: "aa" needs two cells.
        var board3 = new[] { "a".ToCharArray() };
        Print("single cell", sol.FindWords(board3, new[] { "a", "aa" }));   // expected: a

        // Duplicates in the input must not duplicate the output.
        Print("duplicate words", sol.FindWords(board3, new[] { "a", "a", "a" }));   // expected: a

        // A word that snakes through the whole board, plus its own prefixes.
        var board4 = new[]
        {
            "abc".ToCharArray(),
            "hid".ToCharArray(),
            "gfe".ToCharArray(),
        };
        Print("spiral", sol.FindWords(board4, new[] { "abcde", "abcdefghi", "abcdefghij", "hi", "ih" }));
        // expected: abcde, abcdefghi, hi, ih

        // Degenerate inputs.
        Print("empty words", sol.FindWords(board1, Array.Empty<string>()));
        Print("null board", sol.FindWords(null!, new[] { "a" }));

        // The adversarial case the leaf pruning exists for: a near-uniform board where every
        // cell continues every prefix, so the trie -- not the branching factor -- has to be
        // what stops the search. The lone 'b' in the corner is deliberate: it keeps the
        // frequency prefilter from rejecting the "aaa...b" words for free, forcing the DFS to
        // actually chase each long run of 'a's to its end. Without leaf pruning this is the
        // classic TLE case.
        var boardA = new char[10][];
        for (int i = 0; i < 10; i++)
            boardA[i] = new string('a', 10).ToCharArray();
        boardA[9][9] = 'b';

        var aWords = new List<string>();
        for (int k = 1; k <= 50; k++)
        {
            aWords.Add(new string('a', k));         // all findable (99 'a' cells)
            aWords.Add(new string('a', k) + "b");   // findable only if the run ends beside the corner
        }
        var adversarial = Stopwatch.StartNew();
        var found = sol.FindWords(boardA, aWords.ToArray());
        adversarial.Stop();
        Console.WriteLine($"adversarial 'aaa...' board: {found.Count}/{aWords.Count} found in {adversarial.ElapsedMilliseconds} ms");

        StressTest(sol);
    }

    /// <summary>12x12 board, 30k words -- the scale the problem statement cares about.</summary>
    private static void StressTest(WordSearchII sol)
    {
        var rng = new Random(42);
        const int size = 12;

        var board = new char[size][];
        for (int r = 0; r < size; r++)
        {
            board[r] = new char[size];
            for (int c = 0; c < size; c++)
                board[r][c] = (char)('a' + rng.Next(26));
        }

        // Mostly junk words, salted with ~200 words genuinely walkable on this board so the
        // run exercises the hit path too, not just the reject path.
        var words = new List<string>(30_000);
        for (int i = 0; i < 29_800; i++)
        {
            int len = rng.Next(3, 11);
            var sb = new StringBuilder(len);
            for (int k = 0; k < len; k++)
                sb.Append((char)('a' + rng.Next(26)));
            words.Add(sb.ToString());
        }
        for (int i = 0; i < 200; i++)
            words.Add(RandomWalkWord(board, size, rng.Next(3, 9), rng));

        var sw = Stopwatch.StartNew();
        var found = sol.FindWords(board, words.ToArray());
        sw.Stop();

        Console.WriteLine($"stress: {size}x{size} board, {words.Count} words -> {found.Count} found in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>Walk the board without revisiting a cell, so the spelled word is guaranteed findable.</summary>
    private static string RandomWalkWord(char[][] board, int size, int length, Random rng)
    {
        int r = rng.Next(size), c = rng.Next(size);
        var seen = new HashSet<(int, int)> { (r, c) };
        var sb = new StringBuilder().Append(board[r][c]);
        int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };

        while (sb.Length < length)
        {
            var options = new List<(int, int)>(4);
            for (int d = 0; d < 4; d++)
            {
                int nr = r + dr[d], nc = c + dc[d];
                if (nr >= 0 && nr < size && nc >= 0 && nc < size && !seen.Contains((nr, nc)))
                    options.Add((nr, nc));
            }
            if (options.Count == 0)
                break;

            (r, c) = options[rng.Next(options.Count)];
            seen.Add((r, c));
            sb.Append(board[r][c]);
        }

        return sb.ToString();
    }

    private static void Print(string label, IList<string> words)
    {
        var body = words.Count == 0 ? "(none)" : string.Join(", ", words.OrderBy(w => w, StringComparer.Ordinal));
        Console.WriteLine($"{label}: [{body}]");
    }
}
