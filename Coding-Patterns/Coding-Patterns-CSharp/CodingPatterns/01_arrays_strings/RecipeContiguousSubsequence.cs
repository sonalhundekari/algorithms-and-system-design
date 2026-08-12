/*
Recipe as a Contiguous Ingredient Subsequence

An ordered list of `ingredients` of length N, and a set of `recipes`, each itself
an ordered list of ingredients. For each recipe, does it occur as a CONTIGUOUS,
in-order run inside the ingredient list?

    ingredients = [ flour, sugar, egg, butter, sugar, egg, salt ]

    [sugar, egg]           -> true   (indices 1..2, and again at 4..5)
    [flour, sugar, egg]    -> true   (indices 0..2)
    [flour, egg]           -> false  in order, but not adjacent
    [egg, sugar]           -> false  adjacent, but not in that order
    [sugar, egg, salt]     -> true   (indices 4..6)

THE ONE SENTENCE THAT SPECIFIES THE WHOLE PROBLEM

    recipe matches  <=>  some i with ingredients[i .. i+L-1] == recipe

Which is to say: this is not a subsequence problem, and it is not a subset
problem. It is EXACT SUBSTRING MATCHING over a list of tokens instead of a list
of characters. Say that out loud first, because it settles three things the
statement leaves open:

  1. CONTIGUOUS AND ORDERED means the answer is a *substring* search, so every
     classical string algorithm applies unchanged -- KMP, rolling hash,
     Aho-Corasick -- with `char` swapped for `string`. Nothing about the
     algorithms cares that the alphabet is now unbounded; only the constant
     factor of a comparison changes (an interned string compare, not a byte).
  2. MULTI-PATTERN. There is one text and MANY patterns. That is the axis the
     whole problem turns on: the cheap answers are per-recipe (scan the text
     once per recipe), and the good answers are per-text (touch each ingredient
     once, regardless of how many recipes there are).
  3. DUPLICATES IN THE TEXT ARE THE POINT. `sugar` and `egg` appear twice above.
     Any solution that reaches for a HashSet of ingredients, or for "does the
     recipe's multiset fit inside the list's multiset", has silently answered a
     different (easier, wrong) question.

THE FIVE VERSIONS, AND WHAT EACH ONE BUYS

    Part 0  the definition       compare every window outright   O(R*N*L)
    Part 1  KMP per recipe       one text scan per recipe        O(R*(N+L))
    Part 2  rolling hash         hash all windows, look up       O(N*D + total)
    Part 3  two pointers, O(1)   no failure array at all         O(R*N), O(1) sp
    Part 4  streaming            one match-length int per recipe O(R) / element
    Part 5  Aho-Corasick         ONE automaton over all recipes  O(1) / element

    N = |ingredients|, L = |recipe|, R = |recipes|, D = distinct recipe lengths,
    total = sum of recipe lengths.

PART 1 -- KMP. The only thing worth memorizing about KMP is what it refuses to
do: it never moves the TEXT pointer backwards. On a mismatch at pattern position
j, instead of restarting the window one to the right and re-reading text you have
already read, it slides the pattern to the longest border of `recipe[0..j-1]` --
`fail[j-1]` -- and re-tests the same text element against a shorter prefix.
Each text element is consumed once, so the scan is O(N) and the preprocessing is
O(L). That is the whole algorithm.

PART 2 -- ROLLING HASH. Reduce "is this window equal to this recipe" to "is this
64-bit number equal to that 64-bit number". Precompute prefix hashes over the
ingredient list so ANY window's hash is O(1):

    pre[0] = 0,  pre[i+1] = pre[i]*B + id(ingredients[i])
    hash(i, L)  = pre[i+L] - pre[i] * B^L

Then for each distinct recipe LENGTH, sweep the N windows of that length into a
dictionary and look every recipe of that length up. Hash equality is not proof,
so a hit is verified with a real element-by-element compare -- with a random base
mod 2^61-1, a false candidate is a ~N*R/2^61 event, and the verification makes a
collision a slowdown rather than a wrong answer.

Honest complexity: O(N*D + total), not O(N + total). One sweep per distinct
length is the price of this approach, and it is exactly what Part 5 removes.

PART 3 -- O(1) EXTRA SPACE. KMP's failure array is O(L) extra space, so if the
follow-up bans that, KMP is out. What survives is the naive two-pointer scan --
and the trap is what happens on a mismatch:

    i = i - j + 1;  j = 0;      // correct: back the TEXT up to start+1
    i++;            j = 0;      // WRONG: silently skips overlapping starts

The second form is the bug that looks like an optimization. On text
[a, a, a, b] with recipe [a, a, b] it misses the match at index 1: two elements
matched, the third failed, and restarting at index 3 skips the window at index 1
that was still alive. Everything KMP does is a smarter version of that backup;
without the failure array you simply pay for it, O(R*N) worst case.

PART 4 -- STREAMING. Ingredients arrive one at a time and cannot be stored. What
must be remembered is astonishingly small: for each recipe, ONE INTEGER -- how
many of its leading ingredients the stream currently ends with. On each arrival,
advance every recipe's integer by one on a match; on a mismatch, fall back
through the failure function. When an integer reaches its recipe's length, that
recipe just completed.

Two resets that look right and are not:

    on mismatch, j = 0        loses shorter prefixes that are still alive.
                              Recipe [a, a, b], stream a a a: after the third
                              `a` the state must be 2, not 0.
    on a match,  j = 0        loses OVERLAPPING occurrences. Recipe [a, b, a],
                              stream a b a b a matches at 0 AND at 2; only
                              j = fail[L-1] finds the second.

Both are `j = fail[j-1]`, which is the same line as KMP -- because this IS KMP,
with the text arriving late and the state made explicit.

    memory  O(total) for the failure arrays + O(R) live state; ingredients: zero
    latency O(1) -- a recipe is reported on the element that completes it

PART 5 -- AHO-CORASICK. Part 4 costs O(R) per arriving ingredient: R separate
automata all being stepped. Merge them. Build a trie of all recipes; give every
node a SUFFIX LINK to the node spelling its longest proper suffix that is still a
trie prefix; then one pointer walks the whole structure and the per-element cost
is amortized O(1) no matter how many recipes there are.

    the trie      states = "which recipe prefixes could still be completing"
    suffix link   the failure function, generalized across all patterns at once
    output link   the nearest terminal ancestor-by-link, so one node can report
                  [sugar, egg] and [egg] completing at the same position

    build O(total)   stream O(N) + O(matches reported)   memory O(total)

This is the right answer when R is large or the stream is long, and it is the
answer the streaming follow-up is fishing for: Part 4 is "Aho-Corasick with the
trie flattened into R independent chains", and saying so is most of the credit.

WHAT BREAKS NAIVE SOLUTIONS

    empty recipe          vacuously true (matches at position 0). Every indexing
                          loop below must be guarded or it reads pattern[0].
    recipe longer than N  false, and the guard matters: without it the window
                          loop computes a negative bound.
    repeated ingredients  [a, a, a] contains [a, a] twice, overlapping. This is
                          what kills "restart at i+1" and "reset j to 0".
    self-overlapping      recipe [a, b, a] is its own suffix-prefix; the failure
                          function exists precisely for this case.
    duplicate recipes     two identical recipes must both be answered; in the
                          trie they land on the SAME terminal node, so a node
                          owns a LIST of recipe indices, not one.
    string comparison     `==` on string in C# is ordinal, but Compare/IndexOf
                          are culture-sensitive. Pin StringComparison.Ordinal or
                          "ﬂour" and "flour" start being the same ingredient
                          under some cultures.
*/

namespace CodingPatterns.ArraysStrings;

public class RecipeContiguousSubsequence
{
    // Ordinal on purpose -- ingredient names are identifiers, not prose, and
    // culture-aware comparison would make matching machine-dependent.
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    // =====================================================================
    // Part 0 -- the definition, written out. The reference every other
    // implementation is checked against in Run().
    // =====================================================================

    public static int IndexOfBrute(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
    {
        int n = ingredients.Count, m = recipe.Count;
        if (m == 0) return 0;                 // empty recipe: vacuously present
        if (m > n) return -1;                 // cannot fit; also guards the bound

        for (int start = 0; start + m <= n; start++)
        {
            int j = 0;
            while (j < m && Eq(ingredients[start + j], recipe[j])) j++;
            if (j == m) return start;
        }
        return -1;
    }

    public static bool ContainsBrute(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
        => IndexOfBrute(ingredients, recipe) >= 0;

    // Every start index, not just the first -- overlaps included. Used to check
    // the two streaming engines, which report occurrences rather than a bool.
    public static List<int> AllStartsBrute(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
    {
        var starts = new List<int>();
        int n = ingredients.Count, m = recipe.Count;
        if (m > n) return starts;

        // CONVENTION: the empty recipe occurs ONCE, at position 0. Read
        // literally it occurs at all n+1 positions, which is true and useless;
        // the streaming engines below report it once at construction, and the
        // cross-check needs one answer, not two defensible ones.
        if (m == 0) { starts.Add(0); return starts; }

        for (int start = 0; start + m <= n; start++)
        {
            int j = 0;
            while (j < m && Eq(ingredients[start + j], recipe[j])) j++;
            if (j == m) starts.Add(start);
        }
        return starts;
    }

    // =====================================================================
    // Part 1 -- KMP. One scan of the ingredient list per recipe, and the text
    // pointer never moves backwards.
    // =====================================================================

    // fail[i] = length of the longest proper prefix of recipe[0..i] that is also
    // a suffix of it. Built by running the pattern against ITSELF with the same
    // loop the search uses.
    public static int[] BuildFailure(IReadOnlyList<string> recipe)
    {
        var fail = new int[recipe.Count];
        int k = 0;                                   // length of the current border
        for (int i = 1; i < recipe.Count; i++)
        {
            while (k > 0 && !Eq(recipe[i], recipe[k]))
                k = fail[k - 1];                     // shrink to the next-longest border
            if (Eq(recipe[i], recipe[k])) k++;
            fail[i] = k;
        }
        return fail;
    }

    public static int IndexOfKmp(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
    {
        int n = ingredients.Count, m = recipe.Count;
        if (m == 0) return 0;
        if (m > n) return -1;

        var fail = BuildFailure(recipe);
        int j = 0;                                   // matched prefix length so far
        for (int i = 0; i < n; i++)
        {
            while (j > 0 && !Eq(ingredients[i], recipe[j]))
                j = fail[j - 1];                     // slide the recipe, NOT the text
            if (Eq(ingredients[i], recipe[j])) j++;
            if (j == m) return i - m + 1;
        }
        return -1;
    }

    public static bool ContainsKmp(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
        => IndexOfKmp(ingredients, recipe) >= 0;

    // =====================================================================
    // Part 2 -- rolling hash. Prefix hashes over the ingredient list make any
    // window's hash O(1); recipes are then dictionary lookups, verified.
    // =====================================================================

    private const ulong Mod = (1UL << 61) - 1;       // Mersenne prime: reduction is shifts

    private static ulong AddMod(ulong a, ulong b)
    {
        ulong s = a + b;
        return s >= Mod ? s - Mod : s;
    }

    private static ulong SubMod(ulong a, ulong b) => AddMod(a, Mod - b);

    // a*b mod (2^61-1) via the 128-bit product. Both inputs are < 2^61, so the
    // high half is < 2^58 and `hi << 3` cannot lose bits.
    private static ulong MulMod(ulong a, ulong b)
    {
        ulong hi = Math.BigMul(a, b, out ulong lo);
        ulong low61 = lo & Mod;
        ulong high = (hi << 3) | (lo >> 61);         // 2^61 == 1 (mod 2^61-1)
        return AddMod(low61, high);
    }

    /// <summary>
    /// Answers every recipe in one construction. Returns a bool per recipe, in
    /// the order given. O(N * distinct-lengths + total recipe length).
    /// </summary>
    public static bool[] MatchAllByHash(
        IReadOnlyList<string> ingredients,
        IReadOnlyList<IReadOnlyList<string>> recipes,
        int seed = 20260811)
    {
        int n = ingredients.Count;
        var answer = new bool[recipes.Count];

        // Ingredient name -> small int id. Ids start at 1 so that a token the
        // ingredient list never contains can be given 0 and stay distinguishable
        // from a real leading ingredient.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var text = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (!ids.TryGetValue(ingredients[i], out int id))
            {
                id = ids.Count + 1;
                ids[ingredients[i]] = id;
            }
            text[i] = id;
        }

        // Random base, chosen once. A FIXED base is the classic hash-attack hole:
        // an adversary who knows B can hand you colliding windows on purpose.
        var rng = new Random(seed);
        ulong b = (ulong)rng.NextInt64(256, (long)(Mod >> 2));

        var pre = new ulong[n + 1];                  // pre[i+1] = pre[i]*B + text[i]
        for (int i = 0; i < n; i++)
            pre[i + 1] = AddMod(MulMod(pre[i], b), (ulong)text[i]);

        // Bucket recipes by length: one window sweep serves every recipe of that
        // length, and a recipe longer than the list is answered without a sweep.
        var byLength = new Dictionary<int, List<int>>();
        foreach (var (recipe, index) in recipes.Select((r, i) => (r, i)))
        {
            if (recipe.Count == 0) { answer[index] = true; continue; }   // vacuous
            if (recipe.Count > n) continue;                              // stays false
            if (!byLength.TryGetValue(recipe.Count, out var bucket))
                byLength[recipe.Count] = bucket = new List<int>();
            bucket.Add(index);
        }

        foreach (var (length, bucket) in byLength)
        {
            // B^length, for peeling the prefix off a window hash.
            ulong bl = 1;
            for (int i = 0; i < length; i++) bl = MulMod(bl, b);

            // hash -> the starts that produced it. A list, not a single index:
            // on a collision the FIRST start would be a false negative, and the
            // whole point of verifying is that a collision only costs time.
            var windows = new Dictionary<ulong, List<int>>();
            for (int start = 0; start + length <= n; start++)
            {
                ulong h = SubMod(pre[start + length], MulMod(pre[start], bl));
                if (!windows.TryGetValue(h, out var starts))
                    windows[h] = starts = new List<int>();
                starts.Add(start);
            }

            foreach (int index in bucket)
            {
                var recipe = recipes[index];

                // Fold the recipe with the same recurrence. An ingredient the
                // list never contains gets id 0 -- unreachable in `text`, so the
                // recipe simply cannot match, and the verify below proves it.
                ulong rh = 0;
                foreach (string ingredient in recipe)
                {
                    ids.TryGetValue(ingredient, out int id);     // absent -> 0
                    rh = AddMod(MulMod(rh, b), (ulong)id);
                }

                if (!windows.TryGetValue(rh, out var candidates)) continue;
                foreach (int start in candidates)
                {
                    int j = 0;
                    while (j < length && Eq(ingredients[start + j], recipe[j])) j++;
                    if (j == length) { answer[index] = true; break; }
                }
            }
        }

        return answer;
    }

    // =====================================================================
    // Part 3 -- follow-up 1: O(1) extra space. No failure array, no prefix
    // hashes, no dictionary. Two indices and nothing else.
    // =====================================================================

    public static int IndexOfTwoPointer(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
    {
        int n = ingredients.Count, m = recipe.Count;
        if (m == 0) return 0;
        if (m > n) return -1;

        int i = 0;                                   // cursor into ingredients
        int j = 0;                                   // cursor into the recipe
        while (i < n)
        {
            if (Eq(ingredients[i], recipe[j]))
            {
                i++; j++;
                if (j == m) return i - m;
            }
            else
            {
                // THE LINE. Back the text up to one past where this attempt
                // started; `i++` here is the bug demonstrated in Run().
                i = i - j + 1;
                j = 0;
            }
            if (n - i < m - j) return -1;            // not enough list left
        }
        return -1;
    }

    public static bool ContainsTwoPointer(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
        => IndexOfTwoPointer(ingredients, recipe) >= 0;

    // The same loop with the backup removed. Kept only so Run() can show it
    // returning the wrong answer -- do not use it.
    public static int IndexOfTwoPointerBuggy(IReadOnlyList<string> ingredients, IReadOnlyList<string> recipe)
    {
        int n = ingredients.Count, m = recipe.Count;
        if (m == 0) return 0;
        if (m > n) return -1;

        int i = 0, j = 0;
        while (i < n)
        {
            if (Eq(ingredients[i], recipe[j]))
            {
                i++; j++;
                if (j == m) return i - m;
            }
            else
            {
                i++;                                 // <-- skips live candidates
                j = 0;
            }
        }
        return -1;
    }

    // =====================================================================
    // Part 4 -- follow-up 2: streaming, R independent match-length states.
    // Ingredients are never stored; each recipe carries one integer.
    // =====================================================================

    public sealed class StreamingRecipeMatcher
    {
        private readonly IReadOnlyList<string>[] _recipes;
        private readonly int[][] _fail;
        private readonly int[] _matched;             // THE state: prefix length per recipe
        private readonly Action<int, int> _onMatch;  // (recipe index, start position)
        private int _consumed;

        /// <param name="onMatch">Called the moment a recipe completes, with the
        /// index of the recipe and the position in the stream where it started.</param>
        public StreamingRecipeMatcher(IEnumerable<IReadOnlyList<string>> recipes, Action<int, int> onMatch)
        {
            _recipes = recipes.ToArray();
            _onMatch = onMatch;
            _fail = new int[_recipes.Length][];
            _matched = new int[_recipes.Length];

            for (int r = 0; r < _recipes.Length; r++)
            {
                _fail[r] = _recipes[r].Count == 0 ? Array.Empty<int>() : BuildFailure(_recipes[r]);
                // An empty recipe is complete before the stream starts, and stays
                // complete at every position. Reporting it once, at 0, keeps the
                // per-element loop from ever having to index recipe[0].
                if (_recipes[r].Count == 0) _onMatch(r, 0);
            }
        }

        public int Consumed => _consumed;

        /// <summary>State of recipe r: how many leading ingredients the stream currently ends with.</summary>
        public int MatchedPrefix(int recipe) => _matched[recipe];

        public void Accept(string ingredient)
        {
            for (int r = 0; r < _recipes.Length; r++)
            {
                var recipe = _recipes[r];
                if (recipe.Count == 0) continue;

                int j = _matched[r];
                while (j > 0 && !Eq(ingredient, recipe[j]))
                    j = _fail[r][j - 1];             // fall back, do NOT reset to 0
                if (Eq(ingredient, recipe[j])) j++;

                if (j == recipe.Count)
                {
                    _onMatch(r, _consumed - recipe.Count + 1);
                    // Fall back rather than reset, or overlapping occurrences of
                    // a self-overlapping recipe are lost.
                    j = _fail[r][j - 1];
                }
                _matched[r] = j;
            }
            _consumed++;
        }

        /// <summary>Convenience: feed a whole list through and report which recipes were seen.</summary>
        public static bool[] MatchAll(
            IReadOnlyList<string> ingredients,
            IReadOnlyList<IReadOnlyList<string>> recipes)
        {
            var seen = new bool[recipes.Count];
            var matcher = new StreamingRecipeMatcher(recipes, (r, _) => seen[r] = true);
            foreach (string ingredient in ingredients)
                matcher.Accept(ingredient);
            return seen;
        }
    }

    // =====================================================================
    // Part 5 -- one Aho-Corasick automaton over every recipe. Per-ingredient
    // cost stops depending on the number of recipes.
    // =====================================================================

    public sealed class AhoCorasickMatcher
    {
        private sealed class Node
        {
            public readonly Dictionary<string, int> Next = new(StringComparer.Ordinal);
            public int Link;                          // suffix link: longest proper suffix that is a prefix
            public int OutLink = -1;                  // nearest terminal reachable by suffix links
            public int Depth;
            public List<int> Recipes;                 // duplicate recipes share this node
        }

        private readonly List<Node> _nodes = new();
        private readonly Action<int, int> _onMatch;
        private int _state;
        private int _consumed;

        public AhoCorasickMatcher(IEnumerable<IReadOnlyList<string>> recipes, Action<int, int> onMatch)
        {
            _onMatch = onMatch;
            _nodes.Add(new Node());                   // root == state 0

            int index = 0;
            foreach (var recipe in recipes)
            {
                int node = 0;
                foreach (string ingredient in recipe)
                {
                    if (!_nodes[node].Next.TryGetValue(ingredient, out int next))
                    {
                        next = _nodes.Count;
                        _nodes.Add(new Node { Depth = _nodes[node].Depth + 1 });
                        _nodes[node].Next[ingredient] = next;
                    }
                    node = next;
                }
                (_nodes[node].Recipes ??= new List<int>()).Add(index);
                index++;
            }

            BuildLinks();

            // Empty recipes terminate at the root, which no arrival can re-enter.
            // Report them up front, exactly as the streaming matcher does.
            if (_nodes[0].Recipes is { } atRoot)
                foreach (int r in atRoot) _onMatch(r, 0);
        }

        // BFS by depth: a node's suffix link always points to a strictly
        // shallower node, so it is already final when the node is processed.
        private void BuildLinks()
        {
            var queue = new Queue<int>();
            foreach (int child in _nodes[0].Next.Values)
            {
                _nodes[child].Link = 0;               // depth 1 falls back to the root
                queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();

                // The output link is what lets one position report several
                // recipes: [sugar, egg] completing also completes [egg].
                int link = _nodes[node].Link;
                _nodes[node].OutLink = _nodes[link].Recipes != null ? link : _nodes[link].OutLink;

                foreach (var (ingredient, child) in _nodes[node].Next)
                {
                    int fallback = _nodes[node].Link;
                    while (fallback != 0 && !_nodes[fallback].Next.ContainsKey(ingredient))
                        fallback = _nodes[fallback].Link;
                    _nodes[child].Link = _nodes[fallback].Next.TryGetValue(ingredient, out int target) && target != child
                        ? target
                        : 0;
                    queue.Enqueue(child);
                }
            }
        }

        public int NodeCount => _nodes.Count;
        public int Consumed => _consumed;

        public void Accept(string ingredient)
        {
            // Amortized O(1): every arrival deepens the state by at most 1, and
            // this loop only ever moves it shallower.
            while (_state != 0 && !_nodes[_state].Next.ContainsKey(ingredient))
                _state = _nodes[_state].Link;
            if (_nodes[_state].Next.TryGetValue(ingredient, out int next))
                _state = next;

            _consumed++;

            for (int node = _nodes[_state].Recipes != null ? _state : _nodes[_state].OutLink;
                 node > 0;
                 node = _nodes[node].OutLink)
            {
                foreach (int r in _nodes[node].Recipes)
                    _onMatch(r, _consumed - _nodes[node].Depth);
            }
        }

        public static bool[] MatchAll(
            IReadOnlyList<string> ingredients,
            IReadOnlyList<IReadOnlyList<string>> recipes)
        {
            var seen = new bool[recipes.Count];
            var matcher = new AhoCorasickMatcher(recipes, (r, _) => seen[r] = true);
            foreach (string ingredient in ingredients)
                matcher.Accept(ingredient);
            return seen;
        }
    }

    // =====================================================================

    private static readonly string[] Pantry =
        { "flour", "sugar", "egg", "butter", "sugar", "egg", "salt" };

    private static string Show<T>(IReadOnlyList<T> items) => $"[{string.Join(", ", items)}]";

    public static void Run()
    {
        Console.WriteLine("== the worked example ==");
        Console.WriteLine("  ingredients: " + string.Join("  ", Pantry.Select((x, i) => $"{i}:{x}")));

        var samples = new IReadOnlyList<string>[]
        {
            new[] { "sugar", "egg" },
            new[] { "flour", "sugar", "egg" },
            new[] { "flour", "egg" },
            new[] { "egg", "sugar" },
            new[] { "sugar", "egg", "salt" },
            new[] { "vanilla" },
        };

        foreach (var recipe in samples)
        {
            int at = IndexOfKmp(Pantry, recipe);
            string where = at < 0 ? "no" : $"yes, starting at {at}";
            Console.WriteLine($"  {Show(recipe),-32} -> {where}");
        }
        Console.WriteLine("  [flour, egg] is in order but not adjacent; [egg, sugar] is adjacent but");
        Console.WriteLine("  reversed. Neither is a contiguous run, so both are no.");

        Console.WriteLine();
        Console.WriteLine("== all five implementations, same input, same answer ==");

        var hashAnswers = MatchAllByHash(Pantry, samples);
        var streamAnswers = StreamingRecipeMatcher.MatchAll(Pantry, samples);
        var acAnswers = AhoCorasickMatcher.MatchAll(Pantry, samples);
        Console.WriteLine($"  {"recipe",-32}{"brute",-8}{"kmp",-8}{"hash",-8}{"2ptr",-8}{"stream",-8}{"aho",-8}");
        for (int i = 0; i < samples.Length; i++)
        {
            Console.WriteLine($"  {Show(samples[i]),-32}"
                + $"{ContainsBrute(Pantry, samples[i]),-8}"
                + $"{ContainsKmp(Pantry, samples[i]),-8}"
                + $"{hashAnswers[i],-8}"
                + $"{ContainsTwoPointer(Pantry, samples[i]),-8}"
                + $"{streamAnswers[i],-8}"
                + $"{acAnswers[i],-8}");
        }

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var empty = Array.Empty<string>();
        var tooLong = new[] { "flour", "sugar", "egg", "butter", "sugar", "egg", "salt", "salt" };
        Console.WriteLine($"  {"empty recipe (vacuously true):",-46}{ContainsKmp(Pantry, empty)}");
        Console.WriteLine($"  {"recipe longer than the list:",-46}{ContainsKmp(Pantry, tooLong)}");
        Console.WriteLine($"  {"empty ingredient list, empty recipe:",-46}{ContainsKmp(empty, empty)}");
        Console.WriteLine($"  {"empty ingredient list, real recipe:",-46}{ContainsKmp(empty, new[] { "egg" })}");
        Console.WriteLine($"  {"whole list as the recipe:",-46}{ContainsKmp(Pantry, Pantry)}");
        Console.WriteLine($"  {"single ingredient at the end:",-46}{ContainsKmp(Pantry, new[] { "salt" })}");
        Console.WriteLine($"  {"repeated: [a,a] in [a,a,a]:",-46}"
            + $"{Show(AllStartsBrute(new[] { "a", "a", "a" }, new[] { "a", "a" }))}");
        Console.WriteLine($"  {"self-overlapping: [a,b,a] in [a,b,a,b,a]:",-46}"
            + $"{Show(AllStartsBrute(new[] { "a", "b", "a", "b", "a" }, new[] { "a", "b", "a" }))}");
        Console.WriteLine("  The last two are the cases every reset-to-zero bug fails: the occurrences");
        Console.WriteLine("  OVERLAP, so finding the second one requires the failure function.");

        Console.WriteLine();
        Console.WriteLine("== Part 3: the one line that separates the two-pointer scan from a bug ==");

        var aaab = new[] { "a", "a", "a", "b" };
        var aab = new[] { "a", "a", "b" };
        Console.WriteLine($"  ingredients {Show(aaab)}, recipe {Show(aab)}");
        Console.WriteLine($"  {"truth (brute force):",-42}index {IndexOfBrute(aaab, aab)}");
        Console.WriteLine($"  {"i = i - j + 1 (correct backup):",-42}index {IndexOfTwoPointer(aaab, aab)}");
        Console.WriteLine($"  {"i++ (no backup):",-42}index {IndexOfTwoPointerBuggy(aaab, aab)}   <-- misses it");
        Console.WriteLine("  Two elements matched from index 0, the third failed, and `i++` resumed at");
        Console.WriteLine("  index 3 -- skipping the window at index 1 that was still alive. KMP's");
        Console.WriteLine("  failure function is exactly this backup, precomputed instead of paid for.");

        Console.WriteLine();
        Console.WriteLine("== Part 4: the streaming trace -- state is one integer per recipe ==");

        var watched = new IReadOnlyList<string>[]
        {
            new[] { "sugar", "egg" },
            new[] { "egg", "butter", "sugar" },
            new[] { "salt" },
        };
        var completed = new List<string>();
        var trace = new StreamingRecipeMatcher(watched, (r, start) =>
            completed.Add($"recipe {r} {Show(watched[r])} started at {start}"));
        for (int i = 0; i < Pantry.Length; i++)
        {
            completed.Clear();
            trace.Accept(Pantry[i]);
            var state = string.Join(" ", Enumerable.Range(0, watched.Length)
                .Select(r => $"r{r}={trace.MatchedPrefix(r)}/{watched[r].Count}"));
            Console.WriteLine($"  [{i}] {Pantry[i],-8} state: {state}");
            foreach (string done in completed)
                Console.WriteLine($"        ** completed on this element: {done}");
        }
        Console.WriteLine("  Nothing but those three integers is retained -- the ingredients themselves");
        Console.WriteLine("  are dropped as they arrive, which is the whole point of the follow-up.");

        Console.WriteLine();
        Console.WriteLine("== Part 5: one automaton, overlapping recipes reported at the same position ==");

        var nested = new IReadOnlyList<string>[]
        {
            new[] { "sugar", "egg" },
            new[] { "egg" },
            new[] { "flour", "sugar", "egg" },
            new[] { "egg" },                          // duplicate: shares a trie node
        };
        var hits = new List<string>();
        var aho = new AhoCorasickMatcher(nested, (r, start) => hits.Add($"recipe {r} @ {start}"));
        foreach (string ingredient in Pantry) aho.Accept(ingredient);
        Console.WriteLine($"  recipes: {string.Join("  ", nested.Select(r => Show(r)))}");
        Console.WriteLine($"  trie nodes: {aho.NodeCount} (not {nested.Sum(r => r.Count) + 1} -- prefixes are shared)");
        Console.WriteLine($"  reported (@ is the START index): {string.Join(", ", hits)}");
        Console.WriteLine("  The `egg` at index 2 completes all four recipes on a single element -- they");
        Console.WriteLine("  start at 1, 2, 0 and 2 but end together. That is the output link doing its");
        Console.WriteLine("  job: without it the automaton reports only the deepest one. Recipes 1 and 3");
        Console.WriteLine("  are identical and share one terminal node, which is why a node owns a LIST");
        Console.WriteLine("  of recipe indices rather than one.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-check against the definition ==");
        Console.WriteLine("  Tiny alphabet on purpose: 3 ingredients means windows repeat constantly and");
        Console.WriteLine("  recipes self-overlap, which is where every fallback bug lives.");

        var rng = new Random(20260811);
        bool kmpAgrees = true, hashAgrees = true, twoPointerAgrees = true;
        bool streamAgrees = true, ahoAgrees = true, buggyEverWrong = false;
        bool kmpIndexAgrees = true, twoPointerIndexAgrees = true;
        bool streamOccurrencesAgree = true, ahoOccurrencesAgree = true;
        int trials = 0, recipesChecked = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            int n = rng.Next(0, 20);
            var ingredients = new string[n];
            for (int i = 0; i < n; i++)
                ingredients[i] = ((char)('a' + rng.Next(3))).ToString();

            int count = rng.Next(1, 6);
            var recipes = new IReadOnlyList<string>[count];
            for (int r = 0; r < count; r++)
            {
                int m = rng.Next(0, 5);               // 0 included: the empty recipe
                var recipe = new string[m];
                for (int i = 0; i < m; i++)
                    recipe[i] = ((char)('a' + rng.Next(3))).ToString();
                recipes[r] = recipe;
            }

            var expected = recipes.Select(r => ContainsBrute(ingredients, r)).ToArray();
            var hashed = MatchAllByHash(ingredients, recipes, seed: rng.Next());
            var streamed = StreamingRecipeMatcher.MatchAll(ingredients, recipes);
            var automaton = AhoCorasickMatcher.MatchAll(ingredients, recipes);

            // Occurrence-level check: the two streaming engines must report the
            // SAME (recipe, start) multiset the definition does -- a bool[] would
            // hide a dropped overlapping match.
            var streamHits = new List<(int, int)>();
            var streamer = new StreamingRecipeMatcher(recipes, (r, s) => streamHits.Add((r, s)));
            foreach (string ingredient in ingredients) streamer.Accept(ingredient);

            var ahoHits = new List<(int, int)>();
            var ac = new AhoCorasickMatcher(recipes, (r, s) => ahoHits.Add((r, s)));
            foreach (string ingredient in ingredients) ac.Accept(ingredient);

            var truthHits = new List<(int, int)>();
            for (int r = 0; r < count; r++)
                foreach (int start in AllStartsBrute(ingredients, recipes[r]))
                    truthHits.Add((r, start));

            for (int r = 0; r < count; r++)
            {
                kmpAgrees &= ContainsKmp(ingredients, recipes[r]) == expected[r];
                twoPointerAgrees &= ContainsTwoPointer(ingredients, recipes[r]) == expected[r];
                hashAgrees &= hashed[r] == expected[r];
                streamAgrees &= streamed[r] == expected[r];
                ahoAgrees &= automaton[r] == expected[r];

                // Not just "is it there" -- the FIRST occurrence must agree too.
                int truth = IndexOfBrute(ingredients, recipes[r]);
                kmpIndexAgrees &= IndexOfKmp(ingredients, recipes[r]) == truth;
                twoPointerIndexAgrees &= IndexOfTwoPointer(ingredients, recipes[r]) == truth;
                buggyEverWrong |= IndexOfTwoPointerBuggy(ingredients, recipes[r]) != truth;
                recipesChecked++;
            }

            var ordered = truthHits.OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();
            streamOccurrencesAgree &= streamHits.OrderBy(t => t.Item1).ThenBy(t => t.Item2).SequenceEqual(ordered);
            ahoOccurrencesAgree &= ahoHits.OrderBy(t => t.Item1).ThenBy(t => t.Item2).SequenceEqual(ordered);
            trials++;
        }

        Console.WriteLine($"  {$"{trials:n0} random lists, {recipesChecked:n0} recipe queries",-60}");
        Console.WriteLine($"  {"Part 1  KMP == definition:",-60}{kmpAgrees}");
        Console.WriteLine($"  {"Part 2  rolling hash (random base each trial) == definition:",-60}{hashAgrees}");
        Console.WriteLine($"  {"Part 3  two pointers, O(1) space == definition:",-60}{twoPointerAgrees}");
        Console.WriteLine($"  {"Part 4  streaming states == definition:",-60}{streamAgrees}");
        Console.WriteLine($"  {"Part 5  Aho-Corasick == definition:",-60}{ahoAgrees}");
        Console.WriteLine($"  {"KMP finds the FIRST occurrence, not just some:",-60}{kmpIndexAgrees}");
        Console.WriteLine($"  {"two pointers find the FIRST occurrence:",-60}{twoPointerIndexAgrees}");
        Console.WriteLine($"  {"streaming reports every (recipe, start), overlaps included:",-60}{streamOccurrencesAgree}");
        Console.WriteLine($"  {"Aho-Corasick reports every (recipe, start):",-60}{ahoOccurrencesAgree}");
        Console.WriteLine($"  {"the no-backup variant is genuinely wrong (expect True):",-60}{buggyEverWrong}");
    }
}

// INTERVIEW FOLLOW-UPS
//
// "Which one do you write first?"
//     Part 3, the two-pointer scan -- it is eight lines, it is obviously correct
//     if you get the backup right, and it is already the answer to the O(1)-space
//     follow-up. Then say "this is O(R*N) because the text pointer backs up, and
//     KMP is the fix" and write Part 1. Leading with Aho-Corasick and running out
//     of time is the classic way to fail this question.
//
// "Why is KMP not O(1) extra space?"
//     The failure array is O(L). Nothing else in the search allocates, so the
//     two follow-ups are genuinely in tension: O(1) space forces the text pointer
//     to back up, which costs time. Worth naming that the theoretical resolution
//     exists -- Galil-Seiferas and Two-Way (what glibc's memmem uses) match in
//     O(N) time and O(1) space -- and that nobody writes them in an interview.
//
// "The recipes change constantly; the ingredient list does not."
//     Now preprocessing the TEXT is what pays, and the answer is a suffix
//     automaton or suffix array over the ingredient list. A suffix automaton is
//     O(N) to build and answers "is this recipe a substring" by walking the recipe
//     through it in O(L) with NO dependence on N. Aho-Corasick is the opposite
//     trade -- preprocess the patterns, stream the text -- so which one is right
//     is decided entirely by which side is stable.
//
// "The ingredient list changes constantly; the recipes do not."
//     Aho-Corasick, built once, then every new list is a single O(N) pass. This
//     is also the streaming answer, which is not a coincidence: "the text arrives
//     once, left to right, and cannot be revisited" is the same constraint.
//
// "How many recipes before Part 4 stops being good enough?"
//     Part 4 is O(R) per ingredient, Part 5 is O(1). For R in the tens with short
//     recipes, R independent integers are faster in practice -- no dictionary
//     lookups, no pointer chasing, and the states fit in cache. Past a few hundred
//     recipes the automaton wins outright. Say the crossover exists and that you
//     would measure it rather than guessing at it.
//
// "Recipes can be added and removed while the stream is running."
//     Part 4 handles it trivially: append an integer and a failure array, or drop
//     one. Aho-Corasick does not -- adding a pattern invalidates suffix links
//     across the whole trie. The standard fix is a rebuild on a schedule, or the
//     logarithmic method (keep O(log R) automata of doubling size and merge), and
//     for a live stream the honest answer is usually "rebuild in the background
//     and swap, running both during the overlap".
//
// "What if an ingredient may be substituted -- butter OR margarine?"
//     A recipe position becomes a SET rather than an element. KMP survives with
//     the comparison swapped for set membership, and the failure function still
//     works if you are careful that "matches" is no longer transitive -- which is
//     the subtle break, because border computation assumes it. The clean answer
//     is to expand substitutions into the trie: an Aho-Corasick node gets one
//     edge per acceptable ingredient, which stays correct by construction.
//
// "Can you do it without the ids/dictionary in the hash version?"
//     Yes -- hash each ingredient string directly (a stable 64-bit hash of the
//     bytes) instead of interning to an int. The id map exists because it makes
//     the hash independent of the string hashing, and because equality checks on
//     ints are what make the verification cheap. In a distributed setting where
//     two machines must agree on window hashes, you WANT the direct content hash,
//     since ids depend on the order ingredients were first seen.
//
// "What about a bad rolling hash?"
//     Two failure modes worth naming: a fixed base makes collisions constructible
//     by an adversary (fixed here by drawing B at random), and a 32-bit modulus
//     collides by birthday around 2^16 windows, which is nothing. 2^61-1 with a
//     random base plus verification means collisions cost time, never correctness
//     -- the verification is what turns a probabilistic algorithm into a Las Vegas
//     one, and dropping it to "save a compare" is how people ship a wrong answer.
//
// "How would you test it?"
//     The cross-check in Run() is the answer, and its shape is the point: every
//     implementation is checked against the DEFINITION (compare every window
//     outright), never against another implementation, and the streaming engines
//     are checked at the OCCURRENCE level rather than the boolean level -- a
//     dropped overlapping match is invisible in a bool[] and is exactly the bug a
//     reset-to-zero introduces. A 3-ingredient alphabet with recipes up to length
//     4 makes self-overlap the common case rather than a corner, and asserting
//     the no-backup variant is genuinely wrong keeps that demonstration honest.
