// Robot Eats Candies
// Difficulty: Easy as a simulation, Hard once you are asked to plan the route
// Pattern: grid simulation + BFS shortest path + Held-Karp TSP over the candies
//
// The prompt is deliberately under-specified -- "implement a small game where a
// robot navigates a grid eating candies" -- so the first move is to nail the
// contract down, out loud, before writing anything. The questions that actually
// change the code:
//
//   1. What is in a cell? Floor / candy / wall, one robot. Can two candies stack
//      on one cell? (Here: no -- a cell holds at most one candy. If they can,
//      the grid stops being char[][] and becomes int[][] of counts.)
//   2. What happens on an illegal move -- walk into a wall or off the edge? The
//      three legal answers are ignore it, throw, or wrap around, and they are
//      NOT interchangeable: "ignore" is the only one where a command string can
//      always be replayed safely. This implementation ignores it and REPORTS it
//      (see MoveOutcome.Blocked) so the caller can decide.
//   3. Does the robot eat by stepping ON a candy, or by an explicit Eat command?
//      Stepping on it, here -- which is the single most important rule in the
//      file, because it means a route can eat candies it never aimed at, and
//      that breaks the naive "sum of pairwise distances" cost model.
//   4. When is the game over -- all candies eaten, a move budget, or never?
//   5. Is the robot driven by a human (commands) or by the program (a plan)?
//      Both, below: Step/Play is the game, PlanGreedy/PlanOptimal is the AI.
//
// The pieces below, and their costs, with R*C cells and K candies:
//
//   CandyGrid.Step / Play      the game itself                     O(1) per move
//   PlanGreedy                 repeatedly walk to the nearest      O(K * R * C)
//   TryPlanOptimal             Held-Karp over the candy set   O(2^K * K^2 + K*R*C)
//
// Why the state is a mutable object and not a pure function: eating changes the
// board, so "what is the shortest route" is not a question about the input grid
// but about a grid that the answer itself keeps rewriting. Keeping the game in
// one small class with Clone() lets the planners search on a copy while the real
// game stays untouched -- the same trick as undo/redo in a real game loop.

namespace CodingPatterns.Graphs;

public enum MoveOutcome
{
    /// <summary>Wall or edge; the robot did not move.</summary>
    Blocked,

    /// <summary>Stepped onto a plain floor cell.</summary>
    Moved,

    /// <summary>Stepped onto a candy and ate it.</summary>
    Ate,
}

public readonly record struct MoveResult(
    MoveOutcome Outcome, int Row, int Col, int Score, int CandiesLeft);

/// <summary>
/// The game. A rectangular board of floor / wall / candy with exactly one robot.
///
/// The robot glyph is '@' rather than 'R' on purpose: 'R' is already the Right
/// command, and a board character that collides with a command character is a
/// bug waiting to happen the first time someone builds a level from a string.
/// </summary>
public sealed class CandyGrid
{
    public const char Floor = '.';
    public const char Wall = '#';
    public const char Candy = '*';
    public const char RobotGlyph = '@';

    private readonly char[][] _cells;   // '@' is NOT stored here; the robot is a coordinate

    private CandyGrid(char[][] cells, int row, int col, int candies)
    {
        _cells = cells;
        RobotRow = row;
        RobotCol = col;
        CandiesLeft = candies;
    }

    public int Rows => _cells.Length;
    public int Cols => Rows == 0 ? 0 : _cells[0].Length;

    public int RobotRow { get; private set; }
    public int RobotCol { get; private set; }

    /// <summary>Candies eaten.</summary>
    public int Score { get; private set; }

    public int CandiesLeft { get; private set; }

    /// <summary>Moves that actually happened. Blocked commands do not count.</summary>
    public int Steps { get; private set; }

    /// <summary>Commands issued, including the ones that bounced off a wall.</summary>
    public int Commands { get; private set; }

    public bool IsWon => CandiesLeft == 0;

    /// <summary>
    /// Build from a picture of the board. Rejects ragged rows, unknown glyphs,
    /// and any board that does not hold exactly one robot -- all three are
    /// silent-corruption bugs if you let them through, because every one of them
    /// still produces a "grid" that the rest of the code will happily walk.
    /// </summary>
    public static CandyGrid Parse(params string[] rows)
    {
        if (rows is null || rows.Length == 0)
            throw new ArgumentException("board must have at least one row", nameof(rows));

        int cols = rows[0].Length;
        if (cols == 0)
            throw new ArgumentException("board must have at least one column", nameof(rows));

        var cells = new char[rows.Length][];
        int robotRow = -1, robotCol = -1, candies = 0;

        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r] is null || rows[r].Length != cols)
                throw new ArgumentException($"row {r} is ragged; every row must be {cols} wide", nameof(rows));

            cells[r] = new char[cols];
            for (int c = 0; c < cols; c++)
            {
                char ch = rows[r][c];
                switch (ch)
                {
                    case RobotGlyph:
                        if (robotRow >= 0)
                            throw new ArgumentException($"second robot at ({r},{c}); exactly one is allowed", nameof(rows));
                        (robotRow, robotCol) = (r, c);
                        cells[r][c] = Floor;              // the robot rides on top of a floor cell
                        break;

                    case Candy:
                        candies++;
                        cells[r][c] = Candy;
                        break;

                    case Floor:
                    case Wall:
                        cells[r][c] = ch;
                        break;

                    default:
                        throw new ArgumentException($"unexpected cell '{ch}' at ({r},{c})", nameof(rows));
                }
            }
        }

        if (robotRow < 0)
            throw new ArgumentException($"no robot ('{RobotGlyph}') on the board", nameof(rows));

        return new CandyGrid(cells, robotRow, robotCol, candies);
    }

    /// <summary>Independent copy, so a planner can search without disturbing play.</summary>
    public CandyGrid Clone()
    {
        var cells = new char[Rows][];
        for (int r = 0; r < Rows; r++)
            cells[r] = (char[])_cells[r].Clone();

        return new CandyGrid(cells, RobotRow, RobotCol, CandiesLeft);
    }

    public bool Inside(int r, int c) => r >= 0 && r < Rows && c >= 0 && c < Cols;

    public bool IsWall(int r, int c) => !Inside(r, c) || _cells[r][c] == Wall;

    public bool HasCandy(int r, int c) => Inside(r, c) && _cells[r][c] == Candy;

    /// <summary>Every remaining candy, row-major.</summary>
    public List<(int Row, int Col)> Candies()
    {
        var found = new List<(int, int)>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_cells[r][c] == Candy)
                    found.Add((r, c));
        return found;
    }

    // -------------------------------------------------------------- the moves

    public static readonly char[] Commands4 = { 'U', 'D', 'L', 'R' };

    /// <summary>Row/col delta for a command, or null if the character is not one.</summary>
    public static (int DR, int DC)? Delta(char command) => char.ToUpperInvariant(command) switch
    {
        'U' => (-1, 0),
        'D' => (1, 0),
        'L' => (0, -1),
        'R' => (0, 1),
        _ => null,
    };

    /// <summary>
    /// One command. Walls and edges are refused rather than thrown on, so a
    /// recorded command string always replays -- but the refusal is visible in
    /// the result, because silently swallowing it is how "my robot is one cell
    /// off" bugs are born.
    /// </summary>
    public MoveResult Step(char command)
    {
        var delta = Delta(command)
            ?? throw new ArgumentException($"'{command}' is not one of U/D/L/R", nameof(command));

        Commands++;

        int nr = RobotRow + delta.DR, nc = RobotCol + delta.DC;
        if (IsWall(nr, nc))
            return new MoveResult(MoveOutcome.Blocked, RobotRow, RobotCol, Score, CandiesLeft);

        RobotRow = nr;
        RobotCol = nc;
        Steps++;

        var outcome = MoveOutcome.Moved;
        if (_cells[nr][nc] == Candy)
        {
            _cells[nr][nc] = Floor;      // eaten: the board really does change
            Score++;
            CandiesLeft--;
            outcome = MoveOutcome.Ate;
        }

        return new MoveResult(outcome, nr, nc, Score, CandiesLeft);
    }

    /// <summary>Run a whole command string; whitespace is skipped.</summary>
    public List<MoveResult> Play(string commands)
    {
        var log = new List<MoveResult>();
        foreach (char ch in commands ?? "")
            if (!char.IsWhiteSpace(ch))
                log.Add(Step(ch));
        return log;
    }

    /// <summary>The board as it looks now, robot included.</summary>
    public string Render()
    {
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
                sb.Append(r == RobotRow && c == RobotCol ? RobotGlyph : _cells[r][c]);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public static class RobotEatsCandies
{
    private static readonly int[] DR = { -1, 1, 0, 0 };
    private static readonly int[] DC = { 0, 0, -1, 1 };
    private static readonly char[] DirCommand = { 'U', 'D', 'L', 'R' };

    private const int Unreachable = -1;

    // ------------------------------------------------------------------- BFS

    /// <summary>
    /// Step distance from <paramref name="src"/> to every cell, and the command
    /// that first arrived there. Candies are NOT obstacles -- they are just
    /// floor you happen to gain a point on -- so one BFS serves every query from
    /// this position regardless of what is still lying around.
    ///
    /// The parent direction is what turns a distance into a replayable command
    /// string: walking it backwards from a target to the source and reversing
    /// gives the moves, and it costs one extra byte per cell instead of storing
    /// a path per cell.
    /// </summary>
    public static (int[,] Dist, int[,] FromDir) Explore(CandyGrid game, (int Row, int Col) src)
    {
        int rows = game.Rows, cols = game.Cols;
        var dist = new int[rows, cols];
        var fromDir = new int[rows, cols];

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                dist[r, c] = Unreachable;
                fromDir[r, c] = -1;
            }

        dist[src.Row, src.Col] = 0;
        var frontier = new Queue<(int Row, int Col)>();
        frontier.Enqueue(src);

        while (frontier.Count > 0)
        {
            var (r, c) = frontier.Dequeue();

            for (int d = 0; d < 4; d++)
            {
                int nr = r + DR[d], nc = c + DC[d];

                if (game.IsWall(nr, nc) || dist[nr, nc] != Unreachable)
                    continue;

                dist[nr, nc] = dist[r, c] + 1;
                fromDir[nr, nc] = d;
                frontier.Enqueue((nr, nc));
            }
        }

        return (dist, fromDir);
    }

    // ---------------------------------------------------------------- greedy

    /// <summary>
    /// Repeatedly walk to the nearest remaining candy. Fast, obvious, and what
    /// most people write first -- but it is a heuristic, not the answer: see the
    /// counterexample in <see cref="Run"/>, where it walks 11 steps for a board
    /// that takes 9.
    ///
    /// The plan is built by actually PLAYING it on a clone, which matters
    /// because of contract question 3: the walk to the nearest candy can pass
    /// over other candies and eat them for free. Re-reading the board after each
    /// leg is what keeps the plan honest instead of aiming at candies that are
    /// already gone.
    ///
    /// Ties are broken by BFS discovery order (U, D, L, R at each expansion),
    /// which makes the output deterministic but arbitrary -- say that out loud
    /// rather than implying the route is canonical.
    ///
    /// Stops early if the remaining candies are walled off, so the caller must
    /// check <see cref="CandyGrid.IsWon"/> rather than assume.
    /// </summary>
    public static string PlanGreedy(CandyGrid game)
    {
        // Commands that walk the BFS tree from the source down to a target.
        static string PathTo(int[,] fromDir, (int Row, int Col) src, (int Row, int Col) target)
        {
            var moves = new List<char>();
            var (r, c) = target;

            while ((r, c) != src)
            {
                int d = fromDir[r, c];
                moves.Add(DirCommand[d]);
                r -= DR[d];
                c -= DC[d];
            }

            moves.Reverse();                       // built target -> src
            return new string(moves.ToArray());
        }

        var sim = game.Clone();
        var plan = new System.Text.StringBuilder();

        while (!sim.IsWon)
        {
            var (dist, fromDir) = Explore(sim, (sim.RobotRow, sim.RobotCol));

            (int Row, int Col) best = (-1, -1);
            int bestDist = int.MaxValue;

            foreach (var candy in sim.Candies())
            {
                int d = dist[candy.Row, candy.Col];
                if (d != Unreachable && d < bestDist)
                    (bestDist, best) = (d, candy);
            }

            if (best.Row < 0)
                break;                          // everything left is unreachable

            string leg = PathTo(fromDir, (sim.RobotRow, sim.RobotCol), best);
            plan.Append(leg);
            sim.Play(leg);                      // may eat extras on the way -- that is the point
        }

        return plan.ToString();
    }

    // --------------------------------------------------------- Held-Karp TSP

    /// <summary>
    /// The shortest route that eats every candy, exactly.
    ///
    /// Why a TSP over the candies is the right model even though the robot eats
    /// whatever it walks over: any winning route induces an order in which the
    /// candies get eaten, and its length is at least the sum of the shortest
    /// path distances between consecutive candies in that order. Conversely,
    /// that sum is achievable -- walk the shortest paths, and eating extra
    /// candies early only ever helps. So
    ///
    ///     optimal walk = min over orderings of sum of pairwise BFS distances
    ///
    /// and the incidental eating disappears from the cost model. (It does NOT
    /// disappear from a greedy heuristic, which is exactly why greedy loses.)
    ///
    /// That minimum is the open-path Travelling Salesman from a fixed start, so:
    ///
    ///     dp[mask][i] = shortest walk that starts at the robot, eats every
    ///                   candy in mask, and stops on candy i
    ///     dp[mask | 1&lt;&lt;j][j] = min(dp[mask][i] + d[i][j])   for j not in mask
    ///
    /// seeded with dp[1&lt;&lt;i][i] = d[start][i]. The answer is the best
    /// dp[full][i]; there is no return leg because the robot has nowhere to be
    /// afterwards. O(2^K * K^2) time and O(2^K * K) memory, so K is capped --
    /// 20 candies is already a billion table writes. Say the cap out loud in an
    /// interview: "exact up to ~15-18 candies, greedy beyond that" is a real
    /// answer, "it's exponential" is not.
    ///
    /// Returns false when some candy is unreachable, which is a genuinely
    /// different outcome from "the plan is empty".
    /// </summary>
    public static bool TryPlanOptimal(CandyGrid game, out string plan, int maxCandies = 18)
    {
        plan = "";

        var candies = game.Candies();
        int k = candies.Count;
        if (k == 0)
            return true;                        // already won; the empty plan is optimal
        if (k > maxCandies)
            throw new ArgumentOutOfRangeException(
                nameof(maxCandies), $"{k} candies exceeds the exact planner's cap of {maxCandies}; use PlanGreedy");

        // Node 0 is the robot; nodes 1..k are the candies. One BFS per node.
        var nodes = new List<(int Row, int Col)> { (game.RobotRow, game.RobotCol) };
        nodes.AddRange(candies);

        var dist = new int[k + 1][];
        var dirs = new int[k + 1][,];

        for (int i = 0; i <= k; i++)
        {
            var (d, from) = Explore(game, nodes[i]);
            dirs[i] = from;
            dist[i] = new int[k + 1];

            for (int j = 0; j <= k; j++)
            {
                dist[i][j] = d[nodes[j].Row, nodes[j].Col];
                if (i == 0 && dist[i][j] == Unreachable)
                    return false;               // a candy the robot can never touch
            }
        }

        const int Inf = int.MaxValue / 4;
        int full = (1 << k) - 1;

        var dp = new int[1 << k][];
        var parent = new int[1 << k][];
        for (int mask = 0; mask <= full; mask++)
        {
            dp[mask] = new int[k];
            parent[mask] = new int[k];
            for (int i = 0; i < k; i++)
            {
                dp[mask][i] = Inf;
                parent[mask][i] = -1;
            }
        }

        for (int i = 0; i < k; i++)
            dp[1 << i][i] = dist[0][i + 1];

        for (int mask = 1; mask <= full; mask++)
            for (int i = 0; i < k; i++)
            {
                if (dp[mask][i] >= Inf || (mask & (1 << i)) == 0)
                    continue;

                for (int j = 0; j < k; j++)
                {
                    if ((mask & (1 << j)) != 0)
                        continue;

                    int step = dist[i + 1][j + 1];
                    if (step == Unreachable)
                        continue;               // reachable from the start, but not from here

                    int next = mask | (1 << j);
                    if (dp[mask][i] + step < dp[next][j])
                    {
                        dp[next][j] = dp[mask][i] + step;
                        parent[next][j] = i;
                    }
                }
            }

        int last = -1, bestCost = Inf;
        for (int i = 0; i < k; i++)
            if (dp[full][i] < bestCost)
                (bestCost, last) = (dp[full][i], i);

        if (last < 0)
            return false;

        // Walk the table backwards to recover the visiting order...
        var order = new List<int>();
        for (int mask = full, i = last; i >= 0;)
        {
            order.Add(i);
            int prev = parent[mask][i];
            mask ^= 1 << i;
            i = prev;
        }
        order.Reverse();

        // Commands that walk the BFS tree from the source down to a target.
        static string PathTo(int[,] fromDir, (int Row, int Col) src, (int Row, int Col) target)
        {
            var moves = new List<char>();
            var (r, c) = target;

            while ((r, c) != src)
            {
                int d = fromDir[r, c];
                moves.Add(DirCommand[d]);
                r -= DR[d];
                c -= DC[d];
            }

            moves.Reverse();                       // built target -> src
            return new string(moves.ToArray());
        }

        // ...then stitch the BFS paths between consecutive stops.
        var moves = new System.Text.StringBuilder();
        int fromNode = 0;
        foreach (int candy in order)
        {
            moves.Append(PathTo(dirs[fromNode], nodes[fromNode], nodes[candy + 1]));
            fromNode = candy + 1;
        }

        plan = moves.ToString();
        return true;
    }

    // ------------------------------------------------------------------ demo

    public static void Run(string[] args)
    {
        if (args.Length > 0 && args[0] is "--play" or "-p")
        {
            PlayInteractive();
            return;
        }

        Console.WriteLine("== the game ==");

        var board = CandyGrid.Parse(
            "@.*.#",
            ".##.*",
            "*...#",
            "#.*..");

        Console.Write(board.Render());
        Console.WriteLine($"  {board.CandiesLeft} candies to eat");
        Console.WriteLine();

        // Drive it by hand, including a move that walks into a wall.
        foreach (char cmd in "RRDDL")
        {
            var result = board.Step(cmd);
            Console.WriteLine($"  {cmd} -> {result.Outcome,-7} at ({result.Row},{result.Col})  score {result.Score}  left {result.CandiesLeft}");
        }
        Console.WriteLine("  expect: R Ate at (0,2) is the first candy, D Blocked twice on the '#' below it");
        Console.WriteLine();
        Console.Write(board.Render());
        Console.WriteLine($"  steps {board.Steps} of {board.Commands} commands ({board.Commands - board.Steps} bounced)");

        Console.WriteLine();
        Console.WriteLine("== auto-play: greedy vs optimal ==");

        var level = CandyGrid.Parse(
            "@.*.#",
            ".##.*",
            "*...#",
            "#.*..");

        string greedy = PlanGreedy(level);
        var greedyRun = level.Clone();
        greedyRun.Play(greedy);
        Console.WriteLine($"  greedy  : {greedy,-24} {greedy.Length} steps, won = {greedyRun.IsWon}");

        TryPlanOptimal(level, out string optimal);
        var optimalRun = level.Clone();
        optimalRun.Play(optimal);
        Console.WriteLine($"  optimal : {optimal,-24} {optimal.Length} steps, won = {optimalRun.IsWon}");
        Console.WriteLine("  same length here -- greedy ties on most small boards, which is exactly why");
        Console.WriteLine("  'it passed my examples' is not evidence that a heuristic is correct:");

        Console.WriteLine();
        Console.WriteLine("== where greedy actually loses (a corridor) ==");

        // Candies at columns 0, 4, 7; robot at 5. Greedy grabs the candy one step
        // to its LEFT first, and then has to cross the whole corridor twice.
        // Going right first is worse for one move and better overall, because the
        // long walk back eats column 4 for free on the way past.
        var corridor = CandyGrid.Parse("*...*@.*");
        Console.WriteLine($"  {corridor.Render().TrimEnd()}");

        string corridorGreedy = PlanGreedy(corridor);
        TryPlanOptimal(corridor, out string corridorOptimal);

        var g = corridor.Clone();
        g.Play(corridorGreedy);
        var o = corridor.Clone();
        o.Play(corridorOptimal);

        Console.WriteLine($"  greedy  : {corridorGreedy} -> {corridorGreedy.Length} steps (L, then RRR, then LLLLLLL), won = {g.IsWon}");
        Console.WriteLine($"  optimal : {corridorOptimal} -> {corridorOptimal.Length} steps (RR first; the walk back eats column 4), won = {o.IsWon}");
        Console.WriteLine("  expect 11 vs 9 -- greedy is 22% worse, and the gap is unbounded in general");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var noCandy = CandyGrid.Parse("@..", "...");
        Console.WriteLine($"  board with no candy: won = {noCandy.IsWon}, greedy plan = \"{PlanGreedy(noCandy)}\"");

        var sealedOff = CandyGrid.Parse("@#*");
        Console.WriteLine($"  walled-off candy: greedy = \"{PlanGreedy(sealedOff)}\", optimal possible = {TryPlanOptimal(sealedOff, out _)} (expect False)");

        var onTop = CandyGrid.Parse("@*");
        var onTopRun = onTop.Clone();
        onTopRun.Play("R");
        Console.WriteLine($"  candy one step away: score {onTopRun.Score}, and a second 'R' -> {onTopRun.Step('R').Outcome} (edge of the board)");

        var boxed = CandyGrid.Parse("#@#");
        Console.WriteLine($"  boxed in: L -> {boxed.Step('L').Outcome}, R -> {boxed.Step('R').Outcome}, steps {boxed.Steps} of {boxed.Commands}");

        foreach (var (label, rows) in new (string, string[])[]
                 {
                     ("ragged", new[] { "@.", "..." }),
                     ("no robot", new[] { "..*" }),
                     ("two robots", new[] { "@.@" }),
                     ("bad glyph", new[] { "@x*" }),
                 })
        {
            try
            {
                CandyGrid.Parse(rows);
                Console.WriteLine($"  {label}: NOT rejected -- bug");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  {label} rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        try
        {
            CandyGrid.Parse("@.*").Step('X');
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  bad command rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== randomized checks ==");

        var rng = new Random(7);
        bool plansWin = true, greedyNeverBeatsOptimal = true, matchesBruteForce = true;
        int compared = 0;

        for (int trial = 0; trial < 600; trial++)
        {
            int rows = rng.Next(1, 6), cols = rng.Next(1, 6);
            var picture = new string[rows];

            for (int r = 0; r < rows; r++)
            {
                var row = new char[cols];
                for (int c = 0; c < cols; c++)
                    row[c] = rng.Next(10) switch
                    {
                        0 or 1 => CandyGrid.Wall,
                        2 or 3 or 4 => CandyGrid.Candy,
                        _ => CandyGrid.Floor,
                    };
                picture[r] = new string(row);
            }

            // Drop the robot on a cell that is not a wall.
            int rr = rng.Next(rows), rc = rng.Next(cols);
            var fixedRow = picture[rr].ToCharArray();
            fixedRow[rc] = CandyGrid.RobotGlyph;
            picture[rr] = new string(fixedRow);

            var game = CandyGrid.Parse(picture);
            if (game.Candies().Count > 7)
                continue;

            bool solvable = TryPlanOptimal(game, out string exact);
            string heuristic = PlanGreedy(game);

            var afterGreedy = game.Clone();
            afterGreedy.Play(heuristic);

            if (!solvable)
            {
                plansWin &= !afterGreedy.IsWon;   // greedy must not "win" an unwinnable board
                continue;
            }

            var afterExact = game.Clone();
            afterExact.Play(exact);

            compared++;
            plansWin &= afterExact.IsWon && afterGreedy.IsWon;
            greedyNeverBeatsOptimal &= heuristic.Length >= exact.Length;
            matchesBruteForce &= exact.Length == BruteForceBest(game);
        }

        Console.WriteLine($"  {compared} solvable boards: both plans finish the board  : {plansWin}");
        Console.WriteLine($"  {compared} solvable boards: greedy >= optimal            : {greedyNeverBeatsOptimal}");
        Console.WriteLine($"  {compared} solvable boards: Held-Karp == brute force      : {matchesBruteForce}");
    }

    /// <summary>Reference: try every order the candies could be eaten in.</summary>
    private static int BruteForceBest(CandyGrid game)
    {
        var candies = game.Candies();
        int k = candies.Count;
        if (k == 0)
            return 0;

        var nodes = new List<(int Row, int Col)> { (game.RobotRow, game.RobotCol) };
        nodes.AddRange(candies);

        var d = new int[k + 1][];
        for (int i = 0; i <= k; i++)
        {
            var (dist, _) = Explore(game, nodes[i]);
            d[i] = new int[k + 1];
            for (int j = 0; j <= k; j++)
                d[i][j] = dist[nodes[j].Row, nodes[j].Col];
        }

        int best = int.MaxValue;
        var used = new bool[k];

        void Recurse(int at, int eaten, int cost)
        {
            if (cost >= best)
                return;
            if (eaten == k)
            {
                best = cost;
                return;
            }

            for (int j = 0; j < k; j++)
            {
                if (used[j] || d[at][j + 1] == Unreachable)
                    continue;

                used[j] = true;
                Recurse(j + 1, eaten + 1, cost + d[at][j + 1]);
                used[j] = false;
            }
        }

        Recurse(0, 0, 0);
        return best;
    }

    /// <summary>`dotnet run -- RobotEatsCandies --play` for the actual game loop.</summary>
    private static void PlayInteractive()
    {
        var game = CandyGrid.Parse(
            "@.*.#",
            ".##.*",
            "*...#",
            "#.*..");

        Console.WriteLine("U/D/L/R to move, 'hint' for the optimal plan, 'q' to quit.");

        while (!game.IsWon)
        {
            Console.WriteLine();
            Console.Write(game.Render());
            Console.WriteLine($"score {game.Score}   left {game.CandiesLeft}   steps {game.Steps}");
            Console.Write("> ");

            string line = Console.ReadLine();
            if (line is null || line.Trim() is "q" or "quit")
                return;

            if (line.Trim() is "hint")
            {
                Console.WriteLine(TryPlanOptimal(game, out string plan)
                    ? $"  optimal from here: {plan} ({plan.Length} steps)"
                    : "  some candy is walled off -- this board cannot be finished");
                continue;
            }

            foreach (char ch in line)
            {
                if (char.IsWhiteSpace(ch))
                    continue;
                if (CandyGrid.Delta(ch) is null)
                {
                    Console.WriteLine($"  '{ch}' is not a move");
                    continue;
                }
                if (game.Step(ch).Outcome == MoveOutcome.Blocked)
                    Console.WriteLine($"  {char.ToUpperInvariant(ch)} is blocked");
            }
        }

        Console.WriteLine();
        Console.Write(game.Render());
        Console.WriteLine($"cleared in {game.Steps} steps. Optimal from the start was 12.");
    }
}

// ---- Notes for the follow-up questions ----
//
// "The robot has a battery -- N moves, maximise candies eaten."
//     Not a TSP any more; it is orienteering / prize-collecting TSP. The same
//     Held-Karp table answers it directly: dp[mask][i] is already the cheapest
//     way to eat exactly `mask`, so the answer is the largest popcount(mask)
//     with min_i dp[mask][i] <= N. Free, because the table was already built.
//
// "Candies are worth different amounts."
//     Same table, different objective: max over masks of value(mask) subject to
//     min_i dp[mask][i] <= N. Note the greedy heuristic gets worse here, not
//     better -- "nearest" and "best value per step" diverge.
//
// "The grid is 1000x1000 with 200 candies."
//     Exact is dead (2^200). Practical stack: BFS the pairwise distance matrix
//     (200 BFS runs, O(K * R * C)), then nearest-neighbour for an initial tour
//     and 2-opt / Or-opt to improve it. Christofides gives a 1.5-approximation
//     if the metric is symmetric, which grid step distance is.
//
// "Candies respawn / another robot competes."
//     Planning against a moving target is no longer a shortest-path question at
//     all. Re-plan every tick with a short horizon (greedy or beam search), or
//     go game-tree: minimax with alpha-beta for two robots, evaluated on
//     (my score - their score, distance to the nearest free candy).
//
// "Diagonal movement" / "the robot has a facing and can only turn."
//     Diagonals: extend the delta table to eight; every move still costs 1, so
//     BFS is unchanged. A facing turns each cell into (cell, direction) -- four
//     times the states, and turns become edges with their own cost, at which
//     point BFS on the 0/1 grid becomes Dijkstra or 0-1 BFS on the state graph.
//
// "Undo."
//     Step is not currently reversible: eating destroys the candy. Push
//     (row, col, ateHere) onto a stack per step and undo pops it. That is also
//     what a save/replay feature wants, and it is why the command string is kept
//     as plain text -- a replay is just Parse + Play.
