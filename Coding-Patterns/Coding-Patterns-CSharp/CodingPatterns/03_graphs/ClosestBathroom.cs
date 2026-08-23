// Closest Bathroom / Desk on a Grid  (+ the 1-D "cake" family)
// Difficulty: Medium base case, Hard follow-up
// Pattern: multi-source BFS on a grid; two-pass sweep on a line; DP matching
//
// This one is usually asked WITHOUT a signature and WITHOUT example I/O, so the
// first move is to pin the contract down. The questions that actually change the
// code:
//
//   1. What is in a cell? Only B / D / _, or are there walls? -> walls turn the
//      answer from "Manhattan distance" into "BFS step distance", and make
//      "unreachable" a real case that needs a sentinel.
//   2. Can there be zero bathrooms? Zero desks? An empty grid? All are legal
//      inputs and all are where people drop the ball.
//   3. What is the output shape -- a distance per desk, or a full distance grid?
//   4. Movement: 4-directional or 8? Diagonals only change the neighbour table.
//   5. Is one bathroom allowed to serve many desks? (Base case: yes. The Global
//      Assignment follow-up is exactly the version where it may not.)
//
// The pieces below, and their time complexities, are all that is needed to solve the:
//
//   DistanceField / DeskDistances   2-D multi-source BFS                 O(R*C)
//   ShortestPath                    point-to-point BFS on a 0/1 grid     O(R*C)
//   MinPersonCakeDistance           1-D one-pass sweep                   O(n)
//   DistanceToNearest               1-D two-pass sweep, per index        O(n)
//   NearestCakeFrom                 "Task 1": nearest target from start  O(n)
//   NearestTargetStream             the streaming contract               O(1) amortized
//   TryAssignCakes / CakeForPerson  Global Assignment DP                 O(P*C)
//
// Why multi-source BFS and not "BFS once per desk": a per-desk BFS is
// O(D * R * C). Seeding the queue with EVERY bathroom at distance 0 makes the
// frontier expand from all of them at once, so the first time the wave touches a
// cell it arrived along a shortest path from the closest bathroom. One pass,
// O(R * C) time and space, independent of how many desks there are.
//
// Why BFS and not DFS: BFS pops cells in
// non-decreasing distance order, so the first arrival at a cell is final and can
// never be improved later. DFS commits to one branch to its end and can reach a
// cell by a long route before the short one exists, so every DFS answer stays
// provisional -- you would have to keep revisiting cells whenever a cheaper path
// shows up, which degenerates into exhaustive search. BFS's ordering is what
// makes "first touch = shortest" true, and that only holds because every edge
// costs the same 1. Weighted edges break it and you move to Dijkstra.

namespace CodingPatterns.Graphs;

public readonly record struct DeskDistance(int Row, int Col, int Distance);

public readonly record struct CakeAssignment(int PersonIndex, int CakeIndex, int Distance);

public static class ClosestBathroom
{
    public const char Bathroom = 'B';
    public const char Desk = 'D';
    public const char Empty = '_';
    public const char Wall = '#';   // extension: the base case has none

    /// <summary>Unreachable, or "no such thing in this input".</summary>
    public const int Unreachable = -1;

    private static readonly int[] DR = { -1, 1, 0, 0 };
    private static readonly int[] DC = { 0, 0, -1, 1 };

    // ---------------------------------------------------------------- 2-D BFS

    /// <summary>
    /// Steps from every cell to its nearest bathroom, <see cref="Unreachable"/>
    /// where no bathroom is reachable (and on walls, which are never entered).
    ///
    /// The distance grid IS the visited set: -1 means "not yet reached", so no
    /// second bool[,] is needed. That works only because every cell's first
    /// assignment is its final one, which is the BFS ordering property above.
    /// </summary>
    public static int[][] DistanceField(string[] grid)
    {
        if (grid is not null && grid.Length != 0)
        {
            int width = grid[0].Length;
            for (int r = 0; r < grid.Length; r++)
            {
                if (grid[r] is null || grid[r].Length != width)
                    throw new ArgumentException($"row {r} is ragged; every row must be {width} wide", nameof(grid));

                foreach (char ch in grid[r])
                    if (ch is not (Bathroom or Desk or Empty or Wall))
                        throw new ArgumentException($"unexpected cell '{ch}' in row {r}", nameof(grid));
            }
        }

        int rows = grid?.Length ?? 0;
        if (rows == 0)
            return Array.Empty<int[]>();

        int cols = grid[0].Length;
        var dist = new int[rows][];
        var frontier = new Queue<(int Row, int Col)>();

        for (int r = 0; r < rows; r++)
        {
            dist[r] = new int[cols];
            for (int c = 0; c < cols; c++)
            {
                if (grid[r][c] == Bathroom)
                {
                    dist[r][c] = 0;
                    frontier.Enqueue((r, c));      // multi-source: every B seeds the wave
                }
                else
                {
                    dist[r][c] = Unreachable;
                }
            }
        }

        while (frontier.Count > 0)
        {
            var (r, c) = frontier.Dequeue();
            int next = dist[r][c] + 1;

            for (int d = 0; d < 4; d++)
            {
                int nr = r + DR[d], nc = c + DC[d];

                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                    continue;
                if (grid[nr][nc] == Wall || dist[nr][nc] != Unreachable)
                    continue;

                dist[nr][nc] = next;
                frontier.Enqueue((nr, nc));
            }
        }

        return dist;
    }

    /// <summary>
    /// One entry per desk, in row-major order. Distance is
    /// <see cref="Unreachable"/> when the desk is walled off from every
    /// bathroom -- or when the grid simply has no bathrooms at all.
    /// </summary>
    public static List<DeskDistance> DeskDistances(string[] grid)
    {
        var dist = DistanceField(grid);
        var desks = new List<DeskDistance>();

        for (int r = 0; r < dist.Length; r++)
            for (int c = 0; c < dist[r].Length; c++)
                if (grid[r][c] == Desk)
                    desks.Add(new DeskDistance(r, c, dist[r][c]));

        return desks;
    }

    // ------------------------------------------------- point-to-point variant

    /// <summary>
    /// Fewest steps from <paramref name="start"/> to <paramref name="target"/>
    /// on a 0/1 grid where 1 is blocked, or <see cref="Unreachable"/>. Plain
    /// single-source BFS; a blocked endpoint is unreachable by definition.
    /// </summary>
    public static int ShortestPath(int[][] grid, (int Row, int Col) start, (int Row, int Col) target)
    {
        int rows = grid?.Length ?? 0;
        if (rows == 0 || grid[0].Length == 0)
            return Unreachable;

        int cols = grid[0].Length;

        static bool Inside(int r, int c, int rows, int cols) =>
            r >= 0 && r < rows && c >= 0 && c < cols;

        if (!Inside(start.Row, start.Col, rows, cols) || !Inside(target.Row, target.Col, rows, cols))
            throw new ArgumentOutOfRangeException(nameof(start), "start/target must lie inside the grid");

        if (grid[start.Row][start.Col] == 1 || grid[target.Row][target.Col] == 1)
            return Unreachable;
        if (start == target)
            return 0;

        var dist = new int[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                dist[r, c] = Unreachable;

        dist[start.Row, start.Col] = 0;
        var frontier = new Queue<(int Row, int Col)>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            var (r, c) = frontier.Dequeue();

            for (int d = 0; d < 4; d++)
            {
                int nr = r + DR[d], nc = c + DC[d];

                if (!Inside(nr, nc, rows, cols) || grid[nr][nc] == 1 || dist[nr, nc] != Unreachable)
                    continue;

                dist[nr, nc] = dist[r, c] + 1;
                if ((nr, nc) == target)
                    return dist[nr, nc];           // first touch is already shortest

                frontier.Enqueue((nr, nc));
            }
        }

        return Unreachable;
    }

    // -------------------------------------------------------- 1-D {0, 1, 2}

    public const int EmptyCell = 0;
    public const int Person = 1;
    public const int Cake = 2;

    /// <summary>
    /// Smallest gap between any person (1) and any cake (2), or
    /// <see cref="Unreachable"/> when either kind is missing.
    ///
    /// One pass. Carry the most recent index of each kind; every time a cell of
    /// one kind is scanned, the only candidate worth checking is the latest cell
    /// of the OTHER kind, because anything earlier is strictly farther away.
    ///
    /// Induction on the prefix: assume `best` is the optimum over all pairs that
    /// both lie in a[0..i-1]. A new element at i creates new pairs only with
    /// earlier elements of the opposite kind, and among those the nearest is the
    /// most recent one -- so min(best, i - lastOpposite) is the optimum over
    /// a[0..i]. The base case (empty prefix, best = infinity) holds trivially,
    /// so the invariant holds at i = n.
    ///
    /// A cell holds exactly one value, so "person and cake at the same index" is
    /// not representable here and there is no zero-distance edge case -- worth
    /// saying out loud, because the character-array variant below CAN encode it
    /// and there the answer is 0, not 1.
    /// </summary>
    public static int MinPersonCakeDistance(int[] a)
    {
        int lastPerson = Unreachable, lastCake = Unreachable, best = int.MaxValue;

        for (int i = 0; i < (a?.Length ?? 0); i++)
        {
            if (a[i] == Person)
            {
                lastPerson = i;
                if (lastCake >= 0)
                    best = Math.Min(best, i - lastCake);
            }
            else if (a[i] == Cake)
            {
                lastCake = i;
                if (lastPerson >= 0)
                    best = Math.Min(best, i - lastPerson);
            }
        }

        return best == int.MaxValue ? Unreachable : best;
    }

    /// <summary>
    /// Distance from every index to the nearest occurrence of
    /// <paramref name="target"/>, <see cref="Unreachable"/> if there is none.
    /// Two sweeps: left-to-right carries the best target seen behind, then
    /// right-to-left folds in the best target ahead. This is the 1-D shadow of
    /// the multi-source BFS -- same "expand from all sources at once" idea, but
    /// on a line the wave is just two linear passes.
    /// </summary>
    public static int[] DistanceToNearest(int[] a, int target)
    {
        int n = a?.Length ?? 0;
        var dist = new int[n];

        int seen = int.MinValue / 2;               // far enough left to never win
        for (int i = 0; i < n; i++)
        {
            if (a[i] == target)
                seen = i;
            dist[i] = i - seen;
        }

        seen = int.MaxValue / 2;
        for (int i = n - 1; i >= 0; i--)
        {
            if (a[i] == target)
                seen = i;
            dist[i] = Math.Min(dist[i], seen - i);
        }

        for (int i = 0; i < n; i++)
            if (dist[i] >= int.MaxValue / 4)
                dist[i] = Unreachable;             // target never appears

        return dist;
    }

    /// <summary>Character flavour of the same sweep: nearest <paramref name="target"/> per index.</summary>
    public static int[] DistanceToNearest(char[] a, char target)
    {
        int n = a?.Length ?? 0;
        var codes = new int[n];
        for (int i = 0; i < n; i++)
            codes[i] = a[i] == target ? 1 : 0;
        return DistanceToNearest(codes, 1);
    }

    /// <summary>
    /// Per-cell nearest-opposite distance: people get their distance to the
    /// nearest cake, cakes theirs to the nearest person, empties get
    /// <see cref="Unreachable"/>. Both sweeps together are still O(n).
    /// </summary>
    public static int[] NearestOppositeDistances(int[] a)
    {
        int n = a?.Length ?? 0;
        var toCake = DistanceToNearest(a, Cake);
        var toPerson = DistanceToNearest(a, Person);
        var result = new int[n];

        for (int i = 0; i < n; i++)
            result[i] = a[i] switch
            {
                Person => toCake[i],
                Cake => toPerson[i],
                _ => Unreachable,
            };

        return result;
    }

    /// <summary>
    /// "Task 1": <paramref name="a"/> marks cakes with 1; return the distance
    /// from <paramref name="start"/> to the nearest cake, or
    /// <see cref="Unreachable"/> when the array holds none. An out-of-range
    /// start is rejected rather than silently clamped.
    ///
    /// Expanding outward from <paramref name="start"/> stops at the answer
    /// instead of scanning the whole array -- same O(n) worst case, but it is
    /// the version that generalizes to "answer many queries" and to the stream.
    /// </summary>
    public static int NearestCakeFrom(int[] a, int start)
    {
        int n = a?.Length ?? 0;
        if (start < 0 || start >= n)
            throw new ArgumentOutOfRangeException(nameof(start), $"start {start} outside 0..{n - 1}");

        for (int d = 0; d < n; d++)
        {
            if (start - d >= 0 && a[start - d] == 1)
                return d;
            if (start + d < n && a[start + d] == 1)
                return d;
        }

        return Unreachable;
    }

    // ------------------------------------------------------------- streaming

    /// <summary>
    /// The streaming contract, which is the real content of the follow-up. Two
    /// questions decide everything, and both must be asked before coding:
    ///
    ///   "May an answer already emitted be revised when a later target arrives?"
    ///   "How long may an answer be withheld?"
    ///
    /// They trade off directly:
    ///
    ///   Causal / emit-now  -- answer index i using only targets in a[0..i].
    ///     O(1) latency, O(1) memory, but the answers are not the batch answers:
    ///     a target at i+1 never improves index i. That is
    ///     <see cref="PushCausal"/>, i.e. just the left-to-right sweep.
    ///
    ///   Final / buffered   -- match the batch answers exactly. An index cannot
    ///     be settled until the next target arrives (or the stream ends), so
    ///     answers come out in bursts. This class. Memory is the size of the
    ///     current gap between targets, which is unbounded if targets are rare;
    ///     if that matters, cap the buffer and downgrade to the causal answer
    ///     for anything evicted.
    ///
    ///   (A third contract -- emit immediately, then publish corrections -- is
    ///    the causal pass plus a retraction channel. Only viable if downstream
    ///    can absorb updates.)
    ///
    /// Both are O(1) amortized per element; the difference is entirely in when
    /// the answer is allowed to be wrong.
    /// </summary>
    public sealed class NearestTargetStream
    {
        private readonly char _target;
        private readonly List<int> _pending = new();   // seen since the last target
        private int _index;
        private int _lastTarget = Unreachable;
        private bool _closed;

        public NearestTargetStream(char target) => _target = target;

        /// <summary>Indices still waiting on a future target to be settled.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Feed one element; get back every (index, distance) that just became
        /// final, in index order. Usually empty.
        /// </summary>
        public List<(int Index, int Distance)> Push(char value)
        {
            if (_closed)
                throw new InvalidOperationException("stream is closed");

            int i = _index++;
            var settled = new List<(int Index, int Distance)>();

            if (value != _target)
            {
                _pending.Add(i);
                return settled;
            }

            // A target at i settles everything behind it: each pending index now
            // knows both its nearest target on the left and on the right.
            foreach (int p in _pending)
                settled.Add((p, _lastTarget == Unreachable ? i - p : Math.Min(p - _lastTarget, i - p)));

            _pending.Clear();
            _lastTarget = i;
            settled.Add((i, 0));
            return settled;
        }

        /// <summary>
        /// End of stream: no future target can arrive, so whatever is buffered
        /// resolves against the last target seen -- or <see cref="Unreachable"/>
        /// if the stream never contained one.
        /// </summary>
        public List<(int Index, int Distance)> Close()
        {
            _closed = true;
            var settled = new List<(int Index, int Distance)>();

            foreach (int p in _pending)
                settled.Add((p, _lastTarget == Unreachable ? Unreachable : p - _lastTarget));

            _pending.Clear();
            return settled;
        }

        /// <summary>
        /// The other contract: answer immediately using only what has been seen.
        /// Never revised, never buffered, and deliberately not the same numbers.
        /// </summary>
        public static List<(int Index, int Distance)> PushCausal(IEnumerable<char> stream, char target)
        {
            var answers = new List<(int Index, int Distance)>();
            int last = Unreachable, i = 0;

            foreach (char value in stream)
            {
                if (value == target)
                    last = i;
                answers.Add((i, last == Unreachable ? Unreachable : i - last));
                i++;
            }

            return answers;
        }
    }

    // ----------------------------------------------- Global Assignment (DP)

    /// <summary>
    /// Follow-up: give every person their OWN cake, minimizing the total walk --
    /// which is a different problem from giving each person their nearest cake
    /// (two people can share a nearest cake; here they cannot). Impossible when
    /// people outnumber cakes.
    ///
    /// Key structural fact on a line: some optimal assignment is non-crossing.
    /// If person p1 &lt; p2 take cakes c1 &gt; c2, swapping them never increases
    /// the total (case-check the six orderings of the four points -- each swap
    /// either keeps the sum or drops it by twice an overlap). So the i-th person
    /// in sorted order takes the i-th chosen cake in sorted order, and the only
    /// decision left is WHICH cakes get used.
    ///
    /// That decision is a clean DP over the two sorted lists:
    ///
    ///     dp[i][j] = min total cost to serve the first i people
    ///                using only the first j cakes
    ///     dp[i][j] = min( dp[i][j-1],                          // skip cake j
    ///                     dp[i-1][j-1] + |p[i-1] - c[j-1]| )   // person i takes it
    ///
    /// with dp[0][j] = 0 and dp[i][0] = infinity for i &gt; 0. The answer is
    /// dp[P][C]; walking the table backwards recovers who got what.
    /// O(P * C) time and space, and the space collapses to two rows if the
    /// pairing itself is not needed.
    ///
    /// A greedy "repeatedly take the globally closest free pair" is the usual
    /// wrong turn -- it is a genuinely different objective and it is not optimal
    /// here. Counterexample in the tests below. Clarify which one is wanted
    /// before writing anything.
    /// </summary>
    public static bool TryAssignCakes(int[] a, out List<CakeAssignment> pairs, out long totalDistance)
    {
        var people = new List<int>();
        var cakes = new List<int>();
        for (int i = 0; i < (a?.Length ?? 0); i++)          // both already ascending
        {
            if (a[i] == Person) people.Add(i);
            else if (a[i] == Cake) cakes.Add(i);
        }

        pairs = new List<CakeAssignment>();
        totalDistance = 0;

        int n = people.Count, m = cakes.Count;
        if (n > m)
            return false;                          // more people than cakes: impossible
        if (n == 0)
            return true;

        const long Inf = long.MaxValue / 4;
        var dp = new long[n + 1][];
        for (int i = 0; i <= n; i++)
        {
            dp[i] = new long[m + 1];
            for (int j = 0; j <= m; j++)
                dp[i][j] = i == 0 ? 0 : Inf;       // nobody left to serve costs nothing
        }

        for (int i = 1; i <= n; i++)
            for (int j = i; j <= m; j++)           // j < i cannot serve i people
                dp[i][j] = Math.Min(
                    dp[i][j - 1],
                    dp[i - 1][j - 1] + Math.Abs(people[i - 1] - cakes[j - 1]));

        totalDistance = dp[n][m];

        // Backtrack: at (i, j) the cake was skipped iff skipping matched the cost.
        for (int i = n, j = m; i > 0;)
        {
            if (j > i && dp[i][j] == dp[i][j - 1])
            {
                j--;
                continue;
            }

            pairs.Add(new CakeAssignment(people[i - 1], cakes[j - 1], Math.Abs(people[i - 1] - cakes[j - 1])));
            i--;
            j--;
        }

        pairs.Reverse();                            // backtracking walked right to left
        return true;
    }

    /// <summary>
    /// The queried form: which cake does the person standing at
    /// <paramref name="personIndex"/> receive in a globally optimal assignment?
    /// Returns the cake's array index, or <see cref="Unreachable"/> when the
    /// assignment is impossible.
    ///
    /// Ties are real -- several assignments can share the minimum total -- so
    /// this returns *an* optimal answer, the one the backtracking above lands
    /// on. Worth flagging to the interviewer rather than pretending it is unique.
    /// </summary>
    public static int CakeForPerson(int[] a, int personIndex)
    {
        int n = a?.Length ?? 0;
        if (personIndex < 0 || personIndex >= n)
            throw new ArgumentOutOfRangeException(nameof(personIndex), $"index {personIndex} outside 0..{n - 1}");
        if (a[personIndex] != Person)
            throw new ArgumentException($"index {personIndex} holds {a[personIndex]}, not a person", nameof(personIndex));

        if (!TryAssignCakes(a, out var pairs, out _))
            return Unreachable;

        foreach (var pair in pairs)
            if (pair.PersonIndex == personIndex)
                return pair.CakeIndex;

        return Unreachable;                         // unreachable in practice
    }

    // ------------------------------------------------------------------ tests

    private static List<int> IndicesOf(int[] a, int value)
    {
        var found = new List<int>();
        for (int i = 0; i < (a?.Length ?? 0); i++)
            if (a[i] == value)
                found.Add(i);
        return found;                               // already ascending
    }

    public static void Run()
    {
        Console.WriteLine("== 2-D multi-source BFS ==");

        var office = new[]
        {
            "B__D_",
            "_____",
            "__D__",
            "_B___",
            "D___D",
        };
        Console.WriteLine("grid:");
        foreach (var row in office)
            Console.WriteLine($"  {row}");
        foreach (var desk in DeskDistances(office))
            Console.WriteLine($"  desk ({desk.Row},{desk.Col}) -> {desk.Distance}");
        Console.WriteLine("  expect (0,3)=3, (2,2)=2, (4,0)=2, (4,4)=4  <- the far desk is served by the SECOND bathroom");

        // Walls make it a step distance rather than a Manhattan distance, and
        // make "no route" possible.
        var walled = new[]
        {
            "B#D",
            "_#_",
            "_#_",
        };
        foreach (var desk in DeskDistances(walled))
            Console.WriteLine($"  walled-off desk ({desk.Row},{desk.Col}) -> {desk.Distance}  (expect -1)");

        var detour = new[]
        {
            "B#D",
            "_#_",
            "___",
        };
        foreach (var desk in DeskDistances(detour))
            Console.WriteLine($"  detour desk ({desk.Row},{desk.Col}) -> {desk.Distance}  (expect 6, not the Manhattan 2)");

        // The edge cases people forget.
        Console.WriteLine($"  empty grid           -> {DeskDistances(Array.Empty<string>()).Count} desks");
        Console.WriteLine($"  null grid            -> {DeskDistances(null).Count} desks");
        Console.WriteLine($"  no desks (\"B__\")      -> {DeskDistances(new[] { "B__" }).Count} desks");
        Console.WriteLine($"  no bathrooms (\"_D_\")  -> {DeskDistances(new[] { "_D_" })[0].Distance} (expect -1)");
        Console.WriteLine($"  desk on a bathroom is not a thing; a B cell is its own 0: {DistanceField(new[] { "B" })[0][0]}");

        try
        {
            DeskDistances(new[] { "B_", "B" });
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  ragged grid rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        // Cross-check against the O(D * R * C) per-desk BFS on random grids.
        var rng = new Random(31);
        bool agreed = true;

        for (int trial = 0; trial < 400; trial++)
        {
            int rows = rng.Next(1, 9), cols = rng.Next(1, 9);
            var grid = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                var row = new char[cols];
                for (int c = 0; c < cols; c++)
                    row[c] = rng.Next(10) switch
                    {
                        0 or 1 => Bathroom,
                        2 or 3 => Desk,
                        4 => Wall,
                        _ => Empty,
                    };
                grid[r] = new string(row);
            }

            var fast = DeskDistances(grid);
            foreach (var desk in fast)
                agreed &= desk.Distance == BruteForceDeskDistance(grid, desk.Row, desk.Col);
        }

        Console.WriteLine($"  400 random grids agree with per-desk BFS: {agreed}");

        Console.WriteLine();
        Console.WriteLine("== point-to-point BFS (0 = free, 1 = blocked) ==");

        var maze = new[]
        {
            new[] { 0, 0, 1, 0 },
            new[] { 1, 0, 1, 0 },
            new[] { 0, 0, 0, 0 },
        };
        Console.WriteLine($"  (0,0) -> (0,3): {ShortestPath(maze, (0, 0), (0, 3))} (expect 7: down the left, across the bottom, back up)");
        Console.WriteLine($"  (0,0) -> (0,0): {ShortestPath(maze, (0, 0), (0, 0))} (expect 0)");
        Console.WriteLine($"  (0,0) -> (1,0): {ShortestPath(maze, (0, 0), (1, 0))} (expect -1, target is a wall)");
        Console.WriteLine($"  sealed room:    {ShortestPath(new[] { new[] { 0, 1, 0 } }, (0, 0), (0, 2))} (expect -1)");

        Console.WriteLine();
        Console.WriteLine("== 1-D {0 empty, 1 person, 2 cake} ==");

        var line = new[] { 0, 1, 0, 0, 2, 0, 1 };
        Console.WriteLine($"  [{string.Join(", ", line)}] -> {MinPersonCakeDistance(line)} (expect 2: person 6, cake 4)");
        Console.WriteLine($"  adjacent [1, 2]      -> {MinPersonCakeDistance(new[] { 1, 2 })} (expect 1)");
        Console.WriteLine($"  no cake  [1, 0, 1]   -> {MinPersonCakeDistance(new[] { 1, 0, 1 })} (expect -1)");
        Console.WriteLine($"  no person [2, 0, 2]  -> {MinPersonCakeDistance(new[] { 2, 0, 2 })} (expect -1)");
        Console.WriteLine($"  empty []             -> {MinPersonCakeDistance(Array.Empty<int>())} (expect -1)");
        Console.WriteLine($"  null                 -> {MinPersonCakeDistance(null)} (expect -1)");
        Console.WriteLine($"  cake-first [2, 0, 0, 1, 2] -> {MinPersonCakeDistance(new[] { 2, 0, 0, 1, 2 })} (expect 1)");

        Console.WriteLine($"  per-cell nearest-opposite: [{string.Join(", ", NearestOppositeDistances(line))}]");
        Console.WriteLine("    expect [-1, 3, -1, -1, 2, -1, 2]");

        // The sweep's minimum must equal the minimum of the per-cell answers.
        agreed = true;
        for (int trial = 0; trial < 2000; trial++)
        {
            var a = new int[rng.Next(0, 12)];
            for (int i = 0; i < a.Length; i++)
                a[i] = rng.Next(3);

            int sweep = MinPersonCakeDistance(a);
            var perCell = NearestOppositeDistances(a).Where(d => d != Unreachable).ToList();
            int expected = perCell.Count == 0 ? Unreachable : perCell.Min();
            agreed &= sweep == expected;
        }

        Console.WriteLine($"  2,000 random arrays: one-pass sweep == min of two-pass sweep: {agreed}");

        Console.WriteLine();
        Console.WriteLine("== Task 1: nearest cake from a start index (1 marks a cake) ==");

        var cakesOnly = new[] { 0, 0, 1, 0, 0, 0, 1 };
        Console.WriteLine($"  start 0 -> {NearestCakeFrom(cakesOnly, 0)} (expect 2)");
        Console.WriteLine($"  start 2 -> {NearestCakeFrom(cakesOnly, 2)} (expect 0, standing on one)");
        Console.WriteLine($"  start 4 -> {NearestCakeFrom(cakesOnly, 4)} (expect 2, tie left/right)");
        Console.WriteLine($"  no cake -> {NearestCakeFrom(new[] { 0, 0, 0 }, 1)} (expect -1)");

        try
        {
            NearestCakeFrom(cakesOnly, 99);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine($"  out-of-range start rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== character array: nearest 'c' ==");

        var chars = "abcaabcc".ToCharArray();
        Console.WriteLine($"  \"{new string(chars)}\"");
        Console.WriteLine($"  batch : [{string.Join(", ", DistanceToNearest(chars, 'c'))}]");
        Console.WriteLine("  expect  [2, 1, 0, 1, 2, 1, 0, 0]");
        Console.WriteLine($"  causal: [{string.Join(", ", NearestTargetStream.PushCausal(chars, 'c').Select(x => x.Distance))}]");
        Console.WriteLine("  expect  [-1, -1, 0, 1, 2, 3, 0, 0]   <- different contract, not a bug");

        Console.WriteLine();
        Console.WriteLine("== streaming (final-answer contract) ==");

        var stream = new NearestTargetStream('c');
        foreach (char ch in chars)
        {
            var settled = stream.Push(ch);
            string emitted = settled.Count == 0
                ? "-"
                : string.Join(" ", settled.Select(s => $"[{s.Index}]={s.Distance}"));
            Console.WriteLine($"  push '{ch}' -> settles {emitted,-28} pending={stream.PendingCount}");
        }
        var tail = stream.Close();
        Console.WriteLine($"  close    -> settles {(tail.Count == 0 ? "-" : string.Join(" ", tail.Select(s => $"[{s.Index}]={s.Distance}")))}");

        // A stream with no target at all: everything resolves to -1 at Close.
        var barren = new NearestTargetStream('c');
        foreach (char ch in "aab")
            barren.Push(ch);
        Console.WriteLine($"  \"aab\" with no 'c' -> [{string.Join(", ", barren.Close().Select(s => s.Distance))}] (expect -1, -1, -1)");

        // Streamed answers must reproduce the batch answers exactly.
        agreed = true;
        for (int trial = 0; trial < 2000; trial++)
        {
            var buffer = new char[rng.Next(0, 15)];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = "abc"[rng.Next(3)];

            var live = new NearestTargetStream('c');
            var got = new int[buffer.Length];

            // Drain eagerly: Close() must not run until every Push() has.
            foreach (char ch in buffer)
                foreach (var (index, distance) in live.Push(ch))
                    got[index] = distance;

            foreach (var (index, distance) in live.Close())
                got[index] = distance;

            agreed &= got.SequenceEqual(DistanceToNearest(buffer, 'c'));
        }

        Console.WriteLine($"  2,000 random streams reproduce the batch answers: {agreed}");

        Console.WriteLine();
        Console.WriteLine("== Global Assignment ==");

        var party = new[] { 1, 0, 2, 0, 1, 2, 0, 2 };
        Console.WriteLine($"  [{string.Join(", ", party)}]  people at 0,4  cakes at 2,5,7");
        if (TryAssignCakes(party, out var assignment, out long total))
        {
            foreach (var pair in assignment)
                Console.WriteLine($"    person {pair.PersonIndex} -> cake {pair.CakeIndex} (walk {pair.Distance})");
            Console.WriteLine($"    total {total} (expect 3: 0->2 and 4->5)");
        }
        Console.WriteLine($"  queried: person 4 gets cake {CakeForPerson(party, 4)}");

        var crowded = new[] { 1, 1, 2 };
        Console.WriteLine($"  more people than cakes [1, 1, 2]: possible = {TryAssignCakes(crowded, out _, out _)} (expect False)");
        Console.WriteLine($"  CakeForPerson on that input -> {CakeForPerson(crowded, 0)} (expect -1)");
        Console.WriteLine($"  nobody to feed [0, 2, 0]: possible = {TryAssignCakes(new[] { 0, 2, 0 }, out var none, out long zero)}, pairs {none.Count}, total {zero}");

        try
        {
            CakeForPerson(party, 1);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  querying a non-person rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        // Why "each person takes their nearest cake" is a different answer.
        var shared = new[] { 2, 1, 1, 0, 0, 2 };
        var perPersonNearest = DistanceToNearest(shared, Cake);
        Console.WriteLine($"  [{string.Join(", ", shared)}] nearest-cake per person: 1->{perPersonNearest[1]}, 2->{perPersonNearest[2]} (both want cake 0)");
        TryAssignCakes(shared, out var forced, out long forcedTotal);
        Console.WriteLine($"    unique assignment: {string.Join(", ", forced.Select(p => $"{p.PersonIndex}->{p.CakeIndex}"))}, total {forcedTotal} (expect 4)");

        // Why greedy nearest-pair-first is not the same objective. Cakes at 0
        // and 3, people at 2 and 5: the tightest pair in the whole input is
        // (2 <-> 3), and taking it forces person 5 to walk all the way to 0.
        var greedyTrap = new[] { 2, 0, 1, 2, 0, 1 };
        TryAssignCakes(greedyTrap, out var optimal, out long optimalTotal);
        Console.WriteLine($"  [{string.Join(", ", greedyTrap)}]  cakes at 0,3  people at 2,5");
        Console.WriteLine($"    greedy grabs 2->3 (cost 1), then 5->0 (cost 5): total {GreedyTotal(greedyTrap)}");
        Console.WriteLine($"    DP:  {string.Join(", ", optimal.Select(p => $"{p.PersonIndex}->{p.CakeIndex}"))}, total {optimalTotal} (expect 4 -- greedy is 50% worse)");

        // Cross-check the DP against brute-force permutations on small inputs.
        agreed = true;
        int checkedCases = 0;

        for (int trial = 0; trial < 1500; trial++)
        {
            var a = new int[rng.Next(0, 11)];
            for (int i = 0; i < a.Length; i++)
                a[i] = rng.Next(4) switch { 0 => Person, 1 => Cake, _ => EmptyCell };

            var people = IndicesOf(a, Person);
            var cakes = IndicesOf(a, Cake);
            if (people.Count > 6 || cakes.Count > 7)
                continue;

            bool possible = TryAssignCakes(a, out var pairs, out long got);
            long want = BruteForceAssignment(people, cakes);

            if (!possible)
            {
                agreed &= want == long.MaxValue;
                continue;
            }

            checkedCases++;
            agreed &= got == want;
            agreed &= pairs.Count == people.Count;
            agreed &= pairs.Select(p => p.CakeIndex).Distinct().Count() == pairs.Count;   // one cake each
            agreed &= pairs.Sum(p => (long)p.Distance) == got;                            // pairs match the cost
        }

        Console.WriteLine($"  {checkedCases} random assignments match brute force over all permutations: {agreed}");
    }

    /// <summary>Reference: BFS from a single desk out to the nearest bathroom.</summary>
    private static int BruteForceDeskDistance(string[] grid, int row, int col)
    {
        int rows = grid.Length, cols = grid[0].Length;
        var seen = new bool[rows, cols];
        var frontier = new Queue<(int Row, int Col, int Dist)>();

        seen[row, col] = true;
        frontier.Enqueue((row, col, 0));

        while (frontier.Count > 0)
        {
            var (r, c, d) = frontier.Dequeue();
            if (grid[r][c] == Bathroom)
                return d;

            for (int k = 0; k < 4; k++)
            {
                int nr = r + DR[k], nc = c + DC[k];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                    continue;
                if (grid[nr][nc] == Wall || seen[nr, nc])
                    continue;

                seen[nr, nc] = true;
                frontier.Enqueue((nr, nc, d + 1));
            }
        }

        return Unreachable;
    }

    /// <summary>Reference: try every way of choosing and ordering cakes.</summary>
    private static long BruteForceAssignment(List<int> people, List<int> cakes)
    {
        if (people.Count > cakes.Count)
            return long.MaxValue;

        long best = long.MaxValue;
        var used = new bool[cakes.Count];

        void Recurse(int person, long cost)
        {
            if (cost >= best)
                return;
            if (person == people.Count)
            {
                best = cost;
                return;
            }

            for (int c = 0; c < cakes.Count; c++)
            {
                if (used[c])
                    continue;

                used[c] = true;
                Recurse(person + 1, cost + Math.Abs(people[person] - cakes[c]));
                used[c] = false;
            }
        }

        Recurse(0, 0);
        return best;
    }

    /// <summary>
    /// The tempting wrong answer: repeatedly commit the globally closest free
    /// (person, cake) pair. Kept only to show it losing.
    /// </summary>
    private static long GreedyTotal(int[] a)
    {
        var people = IndicesOf(a, Person);
        var cakes = IndicesOf(a, Cake);
        if (people.Count > cakes.Count)
            return long.MaxValue;

        long total = 0;
        var takenPerson = new bool[people.Count];
        var takenCake = new bool[cakes.Count];

        for (int round = 0; round < people.Count; round++)
        {
            int bestP = -1, bestC = -1, bestD = int.MaxValue;

            for (int p = 0; p < people.Count; p++)
            {
                if (takenPerson[p])
                    continue;

                for (int c = 0; c < cakes.Count; c++)
                {
                    if (takenCake[c])
                        continue;

                    int d = Math.Abs(people[p] - cakes[c]);
                    if (d < bestD)
                        (bestD, bestP, bestC) = (d, p, c);
                }
            }

            takenPerson[bestP] = takenCake[bestC] = true;
            total += bestD;
        }

        return total;
    }
}

// ---- Notes for the follow-up questions ----
//
// "Diagonal movement too."
//     Extend DR/DC to eight entries. The BFS is unchanged because every move
//     still costs 1. Note that the answer is now Chebyshev, not Manhattan, on an
//     open grid.
//
// "Bathrooms have capacity K -- only K desks may use each one."
//     This stops being a BFS answer. It becomes min-cost max-flow: desks on the
//     left, bathrooms on the right with capacity K, edge cost = BFS distance
//     (which you still compute with the multi-source pass, once per bathroom).
//     The 1-D Global Assignment DP above is the K = 1, everything-on-a-line
//     special case where the non-crossing property collapses the flow problem
//     into a table.
//
// "The grid is huge and mostly empty, and desks are sparse."
//     Multi-source BFS still touches every cell. If bathrooms are also sparse
//     and there are no walls, skip the grid entirely: the answer per desk is a
//     nearest-neighbour query under L1, so build a k-d tree over the bathroom
//     coordinates and query per desk -- O((B + D) log B) and no R * C term.
//     Walls kill this immediately, because then step distance != L1.
//
// "Desks and bathrooms change over time."
//     Adding a bathroom can only shrink distances, so re-run the BFS seeded from
//     the new bathroom alone and keep the pointwise minimum -- O(R * C) but with
//     early pruning wherever the existing distance already beats the wave.
//     Removing one has no such shortcut and needs a full recompute.
//
// "Which bathroom, not just how far?"
//     Carry the source id alongside the distance in the queue and write it into
//     a parallel grid on first touch. Same pass, one extra array. That also
//     gives the Voronoi partition of the floor for free.
