/*
Document Store with Boolean Predicate Queries (web-server design)

A tiny web server reads one command per line from stdin and prints exactly one
line per command:

    INSERT_DOC <filename> <word1> <word2> ...    -> OK | ERROR
    CHECK_CONTAINS <filename> <predicate>        -> true | false | ERROR
    GET_ALL_FILES <predicate>                    -> f1,f2,... | None | ERROR

The problem arrives in three parts, and each part changes exactly one thing:

    Part 1   store documents; CHECK_CONTAINS over an OR-chain (a || b || c)
    Part 2   CHECK_CONTAINS over mixed && and || (a && b || c)
    Part 3   GET_ALL_FILES over the whole corpus -> inverted index

THE GRAMMAR IS THE WHOLE OF PART 2.

    term       a bare word of [A-Za-z0-9_], no quotes
    operators  && and ||, each surrounded by whitespace
    precedence && binds tighter than ||, as in C#/Java/Python
    no parentheses, no NOT

With no parentheses and && binding tighter, EVERY predicate is a sum of
products -- an OR of AND-groups:

    a && b || c && d     ==     (a && b) || (c && d)

So the "parser" is one pass over whitespace tokens where && appends to the
current group and || starts a new one. No precedence climbing, no AST, no
recursion -- the shape of the grammar already did that work:

    [[a, b], [c, d]]     any group fully satisfied -> true

THREE PARSING TRAPS, ALL OF THEM CHEAP TO AVOID

  1. Do not split on && first. Splitting on "&&" and then splitting each piece
     on "||" silently computes (a) && (b || c) && (d) -- the wrong precedence,
     and it agrees with the right answer on enough small inputs to survive a
     quick manual test. Split on the LOOSER operator first, always.

  2. Do not split on the operator strings at all. Split("||") accepts `a b` as a
     single "term" and `a ||` as a term plus an empty one. Tokenize on
     whitespace and alternate term / operator / term instead: malformed input
     then fails structurally rather than turning into a term that can never
     match. The statement says the operators are whitespace-surrounded, which is
     the hint that whitespace tokenizing is what was intended.

  3. `a || a` and repeated terms are legal. Nothing needs deduping; evaluation
     is idempotent.

THE TWO STORAGE DECISIONS

    _docs[filename]  -> HashSet<string> of words       CHECK_CONTAINS in O(terms)
    _index[term]     -> HashSet<string> of filenames   GET_ALL_FILES without a scan

CHECK_CONTAINS names one document, so it wants the forward map: membership is a
hash lookup per term and the corpus size never enters. GET_ALL_FILES names none,
so the naive answer scans every document (O(D * P) for D docs and a P-term
predicate) -- fine at interview scale, and exactly the thing the interviewer
wants improved. The inverted index inverts the map: each AND-group becomes an
intersection of posting sets and each || a union of the results, so the cost
tracks the number of documents actually holding those terms rather than the size
of the corpus. This is how a search engine evaluates a boolean query, and saying
so out loud is most of the follow-up.

THE INTERSECTION BUG WORTH NAMING BEFORE YOU WRITE IT. A term nobody indexed has
an EMPTY posting set, not a missing one. Code shaped like

    if (_index.TryGetValue(term, out var posting)) postings.Add(posting);

treats an unknown term as no constraint at all, so `apple && zzz` returns every
file containing apple. An unknown term in an AND-group kills the whole group.
Order the intersection smallest-set-first while you are there, so the work is
proportional to the rarest term rather than the most common one.

Time:  InsertDoc      O(W) for W words
       CheckContains  O(P) for a P-term predicate, independent of corpus size
       GetAllFiles    O(sum of posting-list sizes touched)   [indexed]
                      O(D * P)                               [naive scan]
Space: O(total words) for the forward map + the same again for the index
*/

using System.Text.RegularExpressions;

namespace CodingPatterns.ArraysStrings;

/// <summary>A predicate that does not parse. Surfaces to the protocol layer as ERROR.</summary>
public sealed class PredicateException : FormatException
{
    public PredicateException(string message) : base(message) { }
}

// =============================================================================
// Part 2 -- the predicate, as a pure function of (text, word set)
// =============================================================================
//
// Kept out of DocumentStore on purpose. Parsing and evaluation have nothing to
// do with storage, and pulling them out is what makes Part 3 a two-line change
// instead of a rewrite: GET_ALL_FILES reuses Evaluate untouched.

public static class BooleanPredicate
{
    // A term is a bare word. RegexOptions.ECMAScript keeps \w from quietly
    // admitting Unicode word characters, which would make the accepted grammar
    // depend on which alphabet the caller happens to be typing in.
    private static readonly Regex Term =
        new(@"^\w+$", RegexOptions.Compiled | RegexOptions.ECMAScript);

    private static readonly char[] Whitespace = { ' ', '\t' };

    /// <summary>
    /// <c>a &amp;&amp; b || c</c> -> <c>[[a, b], [c]]</c>: an OR of AND-groups
    /// (sum of products).
    /// </summary>
    /// <remarks>
    /// One pass, alternating term and operator. <c>&amp;&amp;</c> extends the
    /// current group and <c>||</c> opens a new one, which IS the precedence
    /// rule -- there is nothing else to encode, because the grammar has no
    /// parentheses.
    /// </remarks>
    public static List<List<string>> Parse(string text)
    {
        var tokens = (text ?? "").Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            throw new PredicateException("empty predicate");

        var groups = new List<List<string>> { new() };
        bool expectTerm = true;

        foreach (var token in tokens)
        {
            if (expectTerm)
            {
                if (!Term.IsMatch(token))
                    throw new PredicateException($"expected a term, got '{token}'");
                groups[^1].Add(token);
            }
            else if (token == "||")
            {
                groups.Add(new List<string>());
            }
            else if (token != "&&")     // "&&" stays inside the current AND-group
            {
                throw new PredicateException($"expected && or ||, got '{token}'");
            }

            expectTerm = !expectTerm;
        }

        if (expectTerm)                 // loop ended still waiting for a term
            throw new PredicateException("predicate ends with an operator");

        return groups;
    }

    /// <summary>True if <paramref name="words"/> satisfies the predicate. Short-circuits both levels.</summary>
    public static bool Evaluate(string text, HashSet<string> words) =>
        Matches(Parse(text), words);

    /// <summary>Evaluate an already-parsed predicate -- so a scan parses once, not once per document.</summary>
    public static bool Matches(List<List<string>> groups, HashSet<string> words) =>
        groups.Any(group => group.All(words.Contains));
}

// =============================================================================
// Parts 1 + 3 -- the server
// =============================================================================

/// <summary>
/// In-memory document store.
/// </summary>
/// <remarks>
/// The methods throw on bad input and return real values; the string protocol
/// (OK / ERROR / true / false / None) lives in <see cref="Handle"/>. Keeping the
/// two apart means the store is usable from code -- and testable -- without
/// going through a text format, and there is exactly one place that decides what
/// an error looks like on the wire.
/// </remarks>
public class DocumentStore
{
    private readonly Dictionary<string, HashSet<string>> _docs = new();   // filename -> words     (Part 1)
    private readonly Dictionary<string, HashSet<string>> _index = new();  // term -> filenames     (Part 3)

    // Insertion rank, so GetAllFiles can order its results without walking
    // _docs -- which would put the corpus size back into the cost and undo the
    // whole point of the index.
    private readonly Dictionary<string, int> _rank = new();

    public int Count => _docs.Count;

    // ---- Part 1: writes ----

    /// <summary>Store <paramref name="words"/> under <paramref name="filename"/>.</summary>
    /// <remarks>
    /// Throws on a duplicate filename or an empty word list. Insert is
    /// create-only by the spec -- no overwrite, no delete -- which is what lets
    /// the inverted index be pure append.
    /// </remarks>
    public void InsertDoc(string filename, IReadOnlyList<string> words)
    {
        if (_docs.ContainsKey(filename))
            throw new InvalidOperationException($"document already exists: {filename}");
        if (words is null || words.Count == 0)
            throw new ArgumentException("a document needs at least one word", nameof(words));

        var bag = new HashSet<string>(words);      // duplicates in the input are free
        _docs[filename] = bag;
        _rank[filename] = _rank.Count;

        foreach (var word in bag)                  // build the index on the write path
        {
            if (!_index.TryGetValue(word, out var posting))
                _index[word] = posting = new HashSet<string>();
            posting.Add(filename);
        }
    }

    // ---- Parts 1 + 2: reads against one named document ----

    /// <summary>Evaluate <paramref name="predicate"/> against one document's words.</summary>
    /// <remarks>
    /// KeyNotFoundException for an unknown file -- deliberately NOT false. "No
    /// such document" and "that document does not match" are different answers
    /// and the protocol spells them differently (ERROR vs false); collapsing
    /// them is the most common wrong answer to Part 1.
    /// </remarks>
    public bool CheckContains(string filename, string predicate)
    {
        if (!_docs.TryGetValue(filename, out var words))
            throw new KeyNotFoundException($"no such document: {filename}");

        return BooleanPredicate.Evaluate(predicate, words);
    }

    // ---- Part 3: reads across the corpus ----

    /// <summary>Every filename matching <paramref name="predicate"/>, in insertion order. Index-driven.</summary>
    /// <remarks>
    /// Each AND-group is an intersection of posting sets; the groups union. Cost
    /// is proportional to the postings actually touched, so one rare term makes
    /// the whole query cheap no matter how large the corpus is.
    /// </remarks>
    public List<string> GetAllFiles(string predicate)
    {
        var hits = new HashSet<string>();

        foreach (var group in BooleanPredicate.Parse(predicate))
        {
            var postings = new List<HashSet<string>>(group.Count);
            bool dead = false;

            foreach (var term in group)
            {
                // An unknown term contributes an EMPTY posting set, which
                // annihilates its group. Skipping it instead would silently
                // weaken the AND into "ignore the terms I have never seen", and
                // `apple && nonsense` would match every apple document.
                if (!_index.TryGetValue(term, out var posting))
                {
                    dead = true;
                    break;
                }
                postings.Add(posting);
            }

            if (dead)
                continue;

            postings.Sort((a, b) => a.Count.CompareTo(b.Count));   // rarest term drives it
            var matched = new HashSet<string>(postings[0]);
            for (int i = 1; i < postings.Count && matched.Count > 0; i++)
                matched.IntersectWith(postings[i]);

            hits.UnionWith(matched);
        }

        var ordered = hits.ToList();
        ordered.Sort((a, b) => _rank[a].CompareTo(_rank[b]));
        return ordered;
    }

    /// <summary>The Part 3 answer before the index: evaluate every document. O(D * P).</summary>
    /// <remarks>
    /// Worth writing first in the interview -- it is four lines, it reuses the
    /// predicate evaluator verbatim, and it is the oracle the indexed version
    /// gets cross-checked against (see the randomized test in the demo).
    /// </remarks>
    public List<string> GetAllFilesScan(string predicate)
    {
        var groups = BooleanPredicate.Parse(predicate);   // parse once, not per document

        return _docs
            .Where(entry => BooleanPredicate.Matches(groups, entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(filename => _rank[filename])
            .ToList();
    }

    // ---- The wire protocol ----

    /// <summary>One command in, one output line out. Every failure becomes ERROR.</summary>
    /// <remarks>
    /// The catch-all is the point: the spec has a single error token, so this
    /// layer flattens unknown command, wrong arity, unknown file, duplicate
    /// insert and malformed predicate into it. Everything below gets to throw
    /// something specific and debuggable.
    /// </remarks>
    public string Handle(string line)
    {
        var parts = (line ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;                        // blank lines are not commands

        var command = parts[0];
        var args = parts.Skip(1).ToArray();

        try
        {
            switch (command)
            {
                case "INSERT_DOC":
                    if (args.Length == 0)
                        return "ERROR";
                    InsertDoc(args[0], args.Skip(1).ToList());
                    return "OK";

                case "CHECK_CONTAINS":
                    if (args.Length < 2)
                        return "ERROR";
                    // Rejoin: the predicate is the rest of the line, and its own
                    // spacing does not matter because Parse re-tokenizes it.
                    return CheckContains(args[0], string.Join(" ", args.Skip(1))) ? "true" : "false";

                case "GET_ALL_FILES":
                    if (args.Length == 0)
                        return "ERROR";
                    var matches = GetAllFiles(string.Join(" ", args));
                    return matches.Count > 0 ? string.Join(",", matches) : "None";

                default:
                    return "ERROR";             // unknown command
            }
        }
        catch (Exception e) when (
            e is PredicateException ||
            e is KeyNotFoundException ||
            e is InvalidOperationException ||
            e is ArgumentException)
        {
            return "ERROR";
        }
    }

    /// <summary>Drive the server from a stream of command lines.</summary>
    public void Serve(TextReader input, TextWriter output)
    {
        for (var line = input.ReadLine(); line is not null; line = input.ReadLine())
        {
            var response = Handle(line);
            if (response is not null)
                output.WriteLine(response);
        }
    }
}

// =============================================================================
// Demo / tests
// =============================================================================

public static class DocumentStoreDemo
{
    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new Exception($"FAILED: {what}");
    }

    private static void CheckRejects(string predicate)
    {
        try
        {
            BooleanPredicate.Parse(predicate);
            throw new Exception($"FAILED: should have rejected '{predicate}'");
        }
        catch (PredicateException)
        {
            // expected
        }
    }

    public static void Main()
    {
        // -- Part 1: the worked example from the statement, through the protocol --

        var script = new[]
        {
            "INSERT_DOC notes.txt apple banana cherry",
            "INSERT_DOC notes.txt fig",
            "INSERT_DOC empty.txt",
            "CHECK_CONTAINS notes.txt apple",
            "CHECK_CONTAINS notes.txt grape",
            "CHECK_CONTAINS notes.txt grape || cherry",
            "CHECK_CONTAINS missing.txt apple",
        };
        var expected = new[] { "OK", "ERROR", "ERROR", "true", "false", "true", "ERROR" };

        var captured = new StringWriter();
        new DocumentStore().Serve(new StringReader(string.Join("\n", script)), captured);

        var produced = captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
        Check(produced.SequenceEqual(expected), "statement example: " + string.Join(" ", produced));

        Console.WriteLine("== Part 1: the statement's example ==");
        for (int i = 0; i < script.Length; i++)
            Console.WriteLine($"  {script[i],-45} -> {expected[i]}");

        // -- Part 2: precedence, the only thing that can actually be wrong --

        Check(Flatten(BooleanPredicate.Parse("a")) == "a", "single term");
        Check(Flatten(BooleanPredicate.Parse("a || b || c")) == "a | b | c", "OR chain");
        Check(Flatten(BooleanPredicate.Parse("a && b || c && d")) == "a,b | c,d", "sum of products");
        Check(Flatten(BooleanPredicate.Parse("  a   &&   b  ")) == "a,b", "spacing is irrelevant");

        foreach (var bad in new[] { "", "   ", "a &&", "|| a", "a b", "a && && b", "a || | b", "a & b", "a!" })
            CheckRejects(bad);

        var store = new DocumentStore();
        store.InsertDoc("doc1", new[] { "apple", "banana" });
        store.InsertDoc("doc2", new[] { "cherry" });
        store.InsertDoc("doc3", new[] { "apple", "cherry", "date" });

        Check(store.CheckContains("doc1", "apple && banana"), "doc1 AND");
        Check(!store.CheckContains("doc1", "apple && cherry"), "doc1 missing cherry");
        Check(store.CheckContains("doc1", "apple && cherry || banana"), "(a&&c) || b on doc1");
        Check(store.CheckContains("doc2", "apple && banana || cherry"), "OR group carries doc2");
        Check(store.CheckContains("doc3", "apple && cherry && date"), "three-way AND");
        Check(!store.CheckContains("doc3", "apple && cherry && missing"), "unknown term kills the AND");

        // The precedence trap, as one assertion. Splitting on && first would
        // compute apple && (banana || cherry), which is TRUE for doc4 -- and
        // this predicate is false only under the correct reading.
        store.InsertDoc("doc4", new[] { "banana" });
        Check(!store.CheckContains("doc4", "apple && banana || cherry"), "&& binds tighter than ||");
        Check(store.CheckContains("doc4", "banana"), "doc4 sanity");

        Console.WriteLine();
        Console.WriteLine("== Part 2: && binds tighter than || ==");
        foreach (var predicate in new[] { "apple", "apple || cherry", "apple && banana", "apple && banana || cherry" })
        {
            var row = string.Join("  ", new[] { "doc1", "doc2", "doc3", "doc4" }
                .Select(f => $"{f}={store.CheckContains(f, predicate).ToString().ToLower(),-5}"));
            Console.WriteLine($"  {predicate,-28} {row}");
        }

        // -- Part 3: GET_ALL_FILES, indexed and naive, must agree --

        Check(Join(store.GetAllFiles("apple")) == "doc1,doc3", "insertion order preserved");
        Check(Join(store.GetAllFiles("apple && cherry")) == "doc3", "intersection");
        Check(Join(store.GetAllFiles("apple || cherry")) == "doc1,doc2,doc3", "union");
        Check(Join(store.GetAllFiles("banana || date")) == "doc1,doc3,doc4", "union, order by insertion");
        Check(store.GetAllFiles("nonexistent").Count == 0, "unknown term matches nothing");
        Check(store.GetAllFiles("apple && nonexistent").Count == 0, "unknown term annihilates its group");
        Check(Join(store.GetAllFiles("apple && nonexistent || cherry")) == "doc2,doc3", "dead group, live group");

        foreach (var predicate in new[] { "apple", "apple && cherry", "apple && nonexistent || cherry", "date" })
            Check(Join(store.GetAllFiles(predicate)) == Join(store.GetAllFilesScan(predicate)),
                  $"index == scan for '{predicate}'");

        Check(store.Handle("GET_ALL_FILES apple && cherry") == "doc3", "protocol: matches");
        Check(store.Handle("GET_ALL_FILES nonexistent") == "None", "protocol: no match -> None");
        Check(store.Handle("GET_ALL_FILES apple &&") == "ERROR", "protocol: malformed predicate");
        Check(store.Handle("GET_ALL_FILES") == "ERROR", "protocol: missing predicate");
        Check(store.Handle("DELETE_DOC doc1") == "ERROR", "protocol: unknown command");
        Check(store.Handle("") is null, "protocol: blank line is not a command");

        Console.WriteLine();
        Console.WriteLine("== Part 3: GET_ALL_FILES ==");
        foreach (var predicate in new[] { "apple", "apple && cherry", "apple && nonexistent", "banana || date" })
            Console.WriteLine($"  {predicate,-28} -> {store.Handle("GET_ALL_FILES " + predicate)}");

        // -- Randomized cross-check: the index must equal the scan, always --

        var rng = new Random(13);
        var vocab = Enumerable.Range(0, 12).Select(i => $"w{i}").ToArray();
        var fuzz = new DocumentStore();

        for (int d = 0; d < 200; d++)
            fuzz.InsertDoc($"f{d}", Sample(rng, vocab, rng.Next(1, 5)));

        for (int q = 0; q < 500; q++)
        {
            var groups = Enumerable
                .Range(0, rng.Next(1, 4))
                .Select(_ => string.Join(" && ", Sample(rng, vocab, rng.Next(1, 4))));
            var predicate = string.Join(" || ", groups);

            Check(Join(fuzz.GetAllFiles(predicate)) == Join(fuzz.GetAllFilesScan(predicate)),
                  $"index == scan for '{predicate}'");
        }

        // -- The point of Part 3: the index decouples query cost from corpus size --

        var big = new DocumentStore();
        var common = Enumerable.Range(0, 20).Select(i => $"c{i}").ToArray();
        rng = new Random(3);

        const int Corpus = 50_000;
        for (int d = 0; d < Corpus; d++)
        {
            var words = Sample(rng, common, 3);
            if (d == 40_000)
                words.Add("rare_term");             // exactly one document has it
            big.InsertDoc($"doc{d}", words);
        }

        const string Query = "rare_term && c0 || rare_term";

        var indexed = big.GetAllFiles(Query);       // warm the JIT before timing
        var scanned = big.GetAllFilesScan(Query);
        Check(Join(indexed) == "doc40000" && Join(scanned) == "doc40000", "both find the rare document");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
            big.GetAllFiles(Query);
        double indexMs = clock.Elapsed.TotalMilliseconds / 200;

        clock.Restart();
        for (int i = 0; i < 5; i++)
            big.GetAllFilesScan(Query);
        double scanMs = clock.Elapsed.TotalMilliseconds / 5;

        Console.WriteLine();
        Console.WriteLine($"== {Corpus:N0} documents, predicate \"{Query}\" ==");
        Console.WriteLine($"  inverted index: {indexMs,8:F4} ms   (touches 1 posting list)");
        Console.WriteLine($"  naive scan:     {scanMs,8:F4} ms   (touches every document)");
        Console.WriteLine($"  speedup:        {scanMs / indexMs,8:F0}x");

        Console.WriteLine();
        Console.WriteLine("All tests passed.");
    }

    private static string Join(IEnumerable<string> files) => string.Join(",", files);

    /// <summary>Renders a parsed predicate as "a,b | c,d" so groupings are assertable.</summary>
    private static string Flatten(List<List<string>> groups) =>
        string.Join(" | ", groups.Select(g => string.Join(",", g)));

    /// <summary>`count` distinct items, drawn without replacement.</summary>
    private static List<string> Sample(Random rng, string[] pool, int count)
    {
        var picked = new List<string>(count);
        var taken = new HashSet<int>();
        while (picked.Count < count)
        {
            int i = rng.Next(pool.Length);
            if (taken.Add(i))
                picked.Add(pool[i]);
        }
        return picked;
    }
}

/*
---- Notes for the follow-up questions ----

"Support NOT, or parentheses."
    Both break the sum-of-products shortcut, and they break it differently.
    Parentheses need a real parser -- precedence climbing or shunting-yard over
    the same token stream -- producing an AST that Evaluate walks recursively;
    the storage does not change at all. NOT is the harder one for Part 3: `!a`
    has no posting list, so it cannot drive a lookup. Evaluate it as a FILTER
    instead -- compute the positive part of each group first, then drop
    candidates containing the negated terms -- and reject a predicate that is
    nothing but negations, since `!a` alone means "scan everything" and the
    index has nothing to offer.

"Make INSERT_DOC overwrite, and add DELETE_DOC."
    The forward map is trivial; the index is where the cost shows up. Removal
    has to visit _index[w] for every w in the old document, and posting sets
    that empty out should be dropped or they leak. Search engines usually refuse
    to pay that on the write path: they append a tombstone, let queries filter
    deleted ids out, and compact in the background. Say which one you are
    building -- an interactive store wants immediate removal, an append-only
    log-structured one wants the tombstone.

"Phrase queries -- the words adjacent, in order."
    A HashSet per document throws away position, so this needs a positional
    index: term -> (filename -> positions). A phrase match is then an
    intersection where the position lists must also be consecutive. It costs
    roughly the size of the corpus again in space, which is why it is normally a
    separate index rather than an upgrade of this one.

"Rank the results instead of returning them all."
    GetAllFiles currently returns every match in insertion order. Ranking wants
    a score (TF-IDF / BM25) computed from the posting lists, term frequency
    stored alongside each posting, and a heap for the top K. Note the API change
    it forces: the caller now needs a limit, because "all matches, ranked" is a
    full sort of the result set.

"It no longer fits on one machine."   <- the distributed follow-up

    SHARDING. Two genuinely different partitionings, and choosing between them
    is the whole discussion:

      Document-partitioned (shard by hash(filename)). Each shard owns a subset
      of the documents and builds a complete index over just those.
      CHECK_CONTAINS routes to exactly ONE shard -- the filename hashes straight
      to it -- so it stays as cheap as it is here. GET_ALL_FILES scatters to
      every shard and gathers, so its latency is the SLOWEST shard, not the
      average. Every shard does useful local work, and adding a shard adds
      capacity for both reads and writes. This is what real search engines do.

      Term-partitioned (shard by hash(term)). Each shard owns whole posting
      lists. GET_ALL_FILES for a single term touches one shard, but an AND-group
      whose terms live on different shards has to ship entire posting lists
      across the network to intersect them -- and popular terms make their shard
      a hotspot. Better only when queries are overwhelmingly single-term and
      posting lists stay small.

    The routing layer is stateless either way, so it scales trivially. Use
    consistent hashing (or explicit shard ranges in a metadata service) so
    adding a shard moves 1/N of the documents instead of reshuffling everything.

    REPLICATION. Documents are immutable once inserted, which makes this far
    easier than the general case. Give each shard R replicas behind a leader:
    the leader takes INSERT_DOC and replicates it, and any replica can serve
    CHECK_CONTAINS / GET_ALL_FILES, so read throughput scales with R while write
    throughput does not.

      Consistency. An insert acknowledged by the leader may not be on a replica
      yet, so a client that inserts and immediately checks can get ERROR -- the
      classic read-your-writes violation, and the failure a user actually
      notices. Fixes, cheapest first: pin a client's reads to the leader for a
      few seconds after its own write; or return the write's version and have
      replicas wait for it; or quorum (W + R > N) if you want it unconditional.
      Search engines mostly do not bother: they accept a bounded index lag and
      tell you documents appear "within a few seconds".

      Failure. The index is DERIVED state -- it can always be rebuilt from the
      documents -- so a replica that falls behind is repaired by replaying the
      insert log rather than by anything clever. Durability belongs to that log;
      the in-memory maps are a cache of it, and treating them that way is what
      makes a restart boring.

    WHAT TO SAY FIRST. Ask about the read/write mix and the query mix before
    proposing anything. A workload that is 99% CHECK_CONTAINS is a routing
    problem and shards perfectly; a workload dominated by GET_ALL_FILES over
    common terms is a fan-out problem whose answer is caching and result limits,
    not more shards.
*/
