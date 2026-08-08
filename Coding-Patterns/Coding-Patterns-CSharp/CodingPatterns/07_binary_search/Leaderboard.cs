// Design a Leaderboard
// Difficulty: Hard (design)
// Pattern: Fenwick tree over the score domain + binary lifting descent
//
// Problem: Design a leaderboard supporting score updates, the rank of an
// ARBITRARY player, and top-K / "players near me" windows.
//
// The two clarifying answers that pick this design:
//   1. Rank of an arbitrary player is required -> a top-K heap is not enough.
//   2. The score domain is bounded integers   -> we can index by SCORE rather
//      than by player, which is the whole trick here.
//
// Approach: The naive read is "keep players sorted", which costs O(log P) per
// op with P = player count, and forces a balanced BST / skip list (a Redis
// ZSET). But rank does not actually need the players ordered -- it only needs a
// COUNT of players scoring higher. So index the score axis instead:
//
//   _tree     Fenwick tree of counts, one slot per possible score.
//             Prefix(s) = how many players score <= s.
//   _score    player -> current score
//   _bucket   score  -> players at that score, in arrival order
//
// Rank(p) = (players scoring strictly more than p) + 1
//         = _total - Prefix(_score[p]) + 1
//
// Every cost is now O(log MaxScore) and INDEPENDENT of the player count -- 100
// players and 100 million players pay the same ~20 array touches. Memory is
// O(MaxScore) for the tree plus O(P) for the maps.
//
// Going the other way (rank -> player) is a binary lifting descent over the
// same tree: walk the Fenwick nodes high bit to low, greedily taking any step
// that keeps the running prefix below the target. That is the "binary search on
// the answer space" pattern, executed against a structure that is already
// shaped like the search tree.
//
// Time:  Submit / Remove / Rank / CountInScoreRange   O(log MaxScore)
//        TopK(k) / Around(p, r)                       O(k log MaxScore)
// Space: O(MaxScore + P)
//
// Ties: competition ranking (1, 2, 2, 4) -- tied players share a rank. Within a
// tie group the order is earliest-to-reach-the-score first. See the note at the
// bottom for making the tiebreak part of the rank itself.

namespace CodingPatterns.BinarySearch;

public readonly record struct LeaderboardEntry(int Rank, string Player, int Score);

public class Leaderboard
{
    private readonly int _maxScore;
    private readonly int _size;      // scores 0.._maxScore
    private readonly int[] _tree;    // Fenwick is 1-based: score s -> index s + 1
    private readonly int _span;      // largest power of two <= _size

    private readonly Dictionary<string, int> _score = new();
    private readonly Dictionary<int, LinkedList<string>> _bucket = new();

    // Lets a player leave their bucket in O(1) while the list keeps arrival order.
    private readonly Dictionary<string, LinkedListNode<string>> _node = new();

    private int _total;

    public Leaderboard(int maxScore = 1_000_000)
    {
        if (maxScore < 0)
            throw new ArgumentOutOfRangeException(nameof(maxScore));

        _maxScore = maxScore;
        _size = maxScore + 1;
        _tree = new int[_size + 1];

        _span = 1;
        while (_span << 1 <= _size)
            _span <<= 1;
    }

    // ---- Fenwick primitives ----

    private void Add(int score, int delta)
    {
        for (int i = score + 1; i <= _size; i += i & -i)
            _tree[i] += delta;
    }

    /// <summary>How many players score &lt;= <paramref name="score"/>.</summary>
    private int Prefix(int score)
    {
        if (score < 0)
            return 0;

        int total = 0;
        for (int i = Math.Min(score, _maxScore) + 1; i > 0; i -= i & -i)
            total += _tree[i];
        return total;
    }

    /// <summary>
    /// The score of the j-th player counting up from the bottom (1-based).
    /// Binary lifting: <c>pos</c> tracks the largest Fenwick index whose prefix
    /// is still &lt; j. Each candidate step is a power of two, so this is a
    /// binary search that happens to reuse the tree's own node layout.
    /// </summary>
    private int ScoreAtAscendingIndex(int j)
    {
        int pos = 0;
        int remaining = j;

        for (int step = _span; step > 0; step >>= 1)
        {
            int next = pos + step;
            if (next <= _size && _tree[next] < remaining)
            {
                pos = next;
                remaining -= _tree[pos];
            }
        }

        // pos is the last index with prefix < j, so index pos + 1 is the answer,
        // and index pos + 1 maps back to score pos.
        return pos;
    }

    /// <summary>The score sitting at 1-based descending rank (rank 1 = highest score).</summary>
    private int ScoreAtRank(int rank) => ScoreAtAscendingIndex(_total - rank + 1);

    // ---- Writes ----

    /// <summary>Set a player's score absolutely. Inserts if unseen.</summary>
    public void Submit(string player, int score)
    {
        if (score < 0 || score > _maxScore)
            throw new ArgumentOutOfRangeException(nameof(score), $"score {score} outside 0..{_maxScore}");

        if (_score.TryGetValue(player, out int old))
        {
            if (old == score)
                return;
            Detach(player, old);
        }

        _score[player] = score;

        if (!_bucket.TryGetValue(score, out var group))
            _bucket[score] = group = new LinkedList<string>();
        _node[player] = group.AddLast(player);

        Add(score, 1);
        _total++;
    }

    /// <summary>Bump a score by <paramref name="delta"/>, clamped to the domain. Returns the new score.</summary>
    public int Increment(string player, int delta)
    {
        long current = _score.TryGetValue(player, out int existing) ? existing : 0;
        int next = (int)Math.Clamp(current + delta, 0, _maxScore);
        Submit(player, next);
        return next;
    }

    /// <summary>Drop a player. Returns whether they were present.</summary>
    public bool Remove(string player)
    {
        if (!_score.TryGetValue(player, out int old))
            return false;

        Detach(player, old);
        _score.Remove(player);
        return true;
    }

    /// <summary>Unhook a player from their current score bucket and the count tree.</summary>
    private void Detach(string player, int score)
    {
        var group = _bucket[score];
        group.Remove(_node[player]);
        _node.Remove(player);

        if (group.Count == 0)
            _bucket.Remove(score);

        Add(score, -1);
        _total--;
    }

    // ---- Reads ----

    public int Count => _total;

    public int? ScoreOf(string player) => _score.TryGetValue(player, out int s) ? s : null;

    /// <summary>Competition rank: 1 = best, tied players share a rank.</summary>
    public int Rank(string player)
    {
        if (!_score.TryGetValue(player, out int score))
            throw new KeyNotFoundException($"unknown player '{player}'");
        return _total - Prefix(score) + 1;
    }

    /// <summary>Players whose score falls in [low, high].</summary>
    public int CountInScoreRange(int low, int high) =>
        high < low ? 0 : Prefix(high) - Prefix(low - 1);

    /// <summary>Fraction of the field this player is ahead of, in [0, 1].</summary>
    public double PercentileOf(string player)
    {
        if (_total <= 1)
            return 1.0;
        return Prefix(_score[player] - 1) / (double)(_total - 1);
    }

    /// <summary>
    /// <paramref name="count"/> entries starting at 1-based rank
    /// <paramref name="startRank"/>, best first. One Fenwick descent per
    /// distinct score in the window, so O(count log MaxScore) -- not
    /// O(count) descents when players are tied.
    /// </summary>
    public List<LeaderboardEntry> Slice(int startRank, int count)
    {
        var results = new List<LeaderboardEntry>();
        int rank = Math.Max(1, startRank);

        while (results.Count < count && rank <= _total)
        {
            int score = ScoreAtRank(rank);
            var group = _bucket[score];
            int groupRank = _total - Prefix(score) + 1;   // rank shared by this tie group

            // `rank` can land mid-group when the window starts inside a tie.
            int offset = rank - groupRank;
            int need = count - results.Count;

            foreach (var player in group.Skip(offset).Take(need))
                results.Add(new LeaderboardEntry(groupRank, player, score));

            rank = groupRank + group.Count;   // first slot past the whole group
        }

        return results;
    }

    public List<LeaderboardEntry> TopK(int k) => Slice(1, k);

    /// <summary>The player plus up to <paramref name="radius"/> neighbours on each side.</summary>
    public List<LeaderboardEntry> Around(string player, int radius) =>
        Slice(Rank(player) - radius, 2 * radius + 1);

    // ---- Tests ----
    public static void Run()
    {
        // Reference implementation: competition rank by direct comparison.
        static int BruteRank(Dictionary<string, int> truth, string player)
        {
            int score = truth[player];
            return truth.Values.Count(s => s > score) + 1;
        }

        static string Format(List<LeaderboardEntry> entries) =>
            string.Join(", ", entries.Select(e => $"#{e.Rank} {e.Player}({e.Score})"));

        // -- basics --
        var lb = new Leaderboard(maxScore: 1000);
        foreach (var (name, score) in new[]
                 { ("ana", 500), ("bo", 900), ("cy", 500), ("dee", 100), ("eli", 750) })
        {
            lb.Submit(name, score);
        }

        Console.WriteLine($"count: {lb.Count}");
        Console.WriteLine($"bo   -> rank {lb.Rank("bo")}   (expect 1)");
        Console.WriteLine($"eli  -> rank {lb.Rank("eli")}  (expect 2)");
        Console.WriteLine($"ana  -> rank {lb.Rank("ana")}  (expect 3, tied with cy)");
        Console.WriteLine($"cy   -> rank {lb.Rank("cy")}   (expect 3, competition ranking)");
        Console.WriteLine($"dee  -> rank {lb.Rank("dee")}  (expect 5, nobody is 4th)");
        Console.WriteLine($"unknown player score: {(lb.ScoreOf("nobody")?.ToString() ?? "null")}");

        // Within the tie, arrival order decides: ana submitted before cy.
        Console.WriteLine($"top 4: {Format(lb.TopK(4))}");
        Console.WriteLine($"top 99 returns {lb.TopK(99).Count} (asking past the end is fine)");

        // -- windows --
        Console.WriteLine($"around eli r=1: {Format(lb.Around("eli", 1))}");
        Console.WriteLine($"around bo  r=2: {Format(lb.Around("bo", 2))}   (clamps at the top)");
        Console.WriteLine($"slice(4, 2):    {Format(lb.Slice(4, 2))}   (starts mid-tie)");

        // -- range counts --
        Console.WriteLine($"in [500,900]: {lb.CountInScoreRange(500, 900)} (expect 4)");
        Console.WriteLine($"in [0,99]:    {lb.CountInScoreRange(0, 99)} (expect 0)");

        // -- updates move a player without disturbing anyone else --
        lb.Submit("dee", 1000);
        Console.WriteLine($"after dee -> 1000: dee rank {lb.Rank("dee")}, bo rank {lb.Rank("bo")}, count {lb.Count}");
        Console.WriteLine($"dee increment -600 -> {lb.Increment("dee", -600)}, rank {lb.Rank("dee")}");
        Console.WriteLine($"dee increment -99999 -> {lb.Increment("dee", -99999)} (clamps at the floor)");
        Console.WriteLine($"remove dee: {lb.Remove("dee")}, again: {lb.Remove("dee")}, count {lb.Count}");

        // -- randomized cross-check against brute force --
        var rng = new Random(7);
        lb = new Leaderboard(maxScore: 200);
        var truth = new Dictionary<string, int>();
        bool agreed = true;

        for (int step = 0; step < 3000; step++)
        {
            if (truth.Count > 0 && rng.NextDouble() < 0.15)
            {
                var victim = truth.Keys.ElementAt(rng.Next(truth.Count));
                lb.Remove(victim);
                truth.Remove(victim);
            }
            else
            {
                var player = $"p{rng.Next(60)}";
                int score = rng.Next(201);
                lb.Submit(player, score);
                truth[player] = score;
            }

            if (step % 100 != 0 || truth.Count == 0)
                continue;

            foreach (var who in truth.Keys)
                agreed &= lb.Rank(who) == BruteRank(truth, who);

            // Tie order is arrival-based rather than name-based, so compare the
            // score sequence (which is fully determined) and the rank labels.
            var expected = truth.OrderByDescending(kv => kv.Value).Take(10).Select(kv => kv.Value);
            var got = lb.TopK(10);
            agreed &= got.Select(e => e.Score).SequenceEqual(expected);
            agreed &= got.All(e => e.Rank == BruteRank(truth, e.Player));

            int lo = rng.Next(201), hi = rng.Next(201);
            if (lo > hi) (lo, hi) = (hi, lo);
            agreed &= lb.CountInScoreRange(lo, hi) == truth.Values.Count(s => lo <= s && s <= hi);
        }

        Console.WriteLine($"3,000 randomized ops agree with brute force: {agreed}");

        // -- the point of the design: cost does not track player count --
        var big = new Leaderboard(maxScore: 1_000_000);
        rng = new Random(11);
        for (int i = 0; i < 200_000; i++)
            big.Submit($"player{i}", rng.Next(1_000_001));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200_000; i += 200)
            big.Rank($"player{i}");
        clock.Stop();

        Console.WriteLine($"1,000 rank queries over 200,000 players: {clock.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"top 3: {Format(big.TopK(3))}");
        Console.WriteLine($"player0 sits at rank {big.Rank("player0")}, percentile {big.PercentileOf("player0"):P1}");
    }
}

// ---- Notes for the follow-up questions ----
//
// "What if scores were unbounded 64-bit?"
//     The O(MaxScore) tree becomes impossible. Switch to an order-statistic
//     tree -- a skip list or balanced BST with subtree counts -- which gives the
//     same API at O(log P) instead of O(log MaxScore). That is exactly what a
//     Redis ZSET is. Or keep this structure over COMPRESSED coordinates if the
//     set of distinct scores is known up front.
//
// "I want tied players to have distinct ranks."
//     Fold the tiebreak into the key: key = score * 2^k + (T_MAX - reachedAt).
//     Every operation here works unchanged, but the domain grows by 2^k, so this
//     only stays affordable while the widened domain is still small enough to
//     allocate. Beyond that, the order-statistic tree is the answer.
//
// "Make it survive a restart / scale past one box."
//     Writes are a commutative +1/-1 on a score slot, which shards cleanly:
//     give each shard its own tree, then Rank(p) = 1 + sum over shards of
//     (shardTotal - shardPrefix(score)). That is one fan-out read per query,
//     and shards never need to agree on player placement -- only on counts.
//
// "Rolling window (last 7 days) instead of all-time."
//     Scores now expire, so pair this with a queue of (player, score, timestamp)
//     and apply the inverse update as entries age out. The tree handles removal
//     at the same O(log MaxScore), so expiry is just a background drain.
