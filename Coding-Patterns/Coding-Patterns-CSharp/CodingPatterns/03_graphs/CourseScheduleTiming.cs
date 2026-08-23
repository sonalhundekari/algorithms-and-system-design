// Course Schedule + the wall-clock follow-ups  (LC 207 / 210 / 2050)
// Difficulty: Medium base, Hard follow-up
// Pattern: Kahn topological sort; layered BFS; topological DP; DAG relaxation
//
// The base is canonical Kahn. What actually gets asked is one of the timing
// follow-ups stacked on top of it, and they are FOUR DIFFERENT PROBLEMS that
// look alike. Pin down which one before writing a line:
//
//   1. Can it be done at all?              -> CanFinish            (LC 207)
//   2. Give me an order.                   -> FindOrder            (LC 210)
//   3. Courses run in BATCHES: a batch     -> TotalTimeBatched
//      must fully finish before the next
//      starts, so a batch costs max(times
//      in batch).                           <- the "barrier" model
//   4. A course starts the moment ALL its  -> MinimumTime          (LC 2050)
//      own prerequisites are done -- no
//      global barrier, so independent
//      courses finish at different times.   <- the "earliest finish" model
//   5. A course starts the moment ANY ONE  -> MinimumTimeAnyPrereq
//      prerequisite is done.                <- shortest path on a DAG
//                                              (and here a cycle stops being a
//                                               deadlock -- see follow-up 3)
//
// 3 and 4 give different numbers on the same input, and the prompt rarely says
// which it means. "Does course C wait for the whole batch, or only for its own
// prerequisites?" is the single highest-signal clarifying question here. The
// test block below runs one input through both to show the gap (20 vs 11).
//
//
// The pieces and their costs -- all O(V + E) except the Dijkstra cross-check:
//
//   CanFinish / FindOrder      Kahn's BFS                       O(V + E)
//   TryFindCycle               DFS three-colour, returns a path O(V + E)
//   TotalTimeBatched           layered BFS, wave = max(times)   O(V + E)
//   EarliestFinishTimes        topological DP, finish[]         O(V + E)
//   MinimumTimeAnyPrereq       same DP with min instead of max  O(V + E)
//   ...Dijkstra                super-source shortest path       O(E log V)
//   Schedule                   Kahn through an object API       O(V + E) * retries
//
// EDGE DIRECTION IS THE OTHER TRAP. LeetCode flips it between problems:
//
//   LC 207 / 210:  prerequisites[i] = [course, prereq]   "to take course, take prereq first"
//   LC 2050:       relations[i]     = [prev, next]       and courses are 1-INDEXED
//
// Everything here takes the LC 207 layout, [course, prereq]. The single
// exception is MinimumTime, which is LC 2050 verbatim -- 1-indexed [prev, next]
// -- and says so in its own build loop. State which layout you are assuming out
// loud before you start writing; silently assuming one is the classic way to
// fail the whole question while writing perfectly correct topological sort code.
//
// Ids are assumed to be in 0..numCourses-1, as the LC constraints guarantee. The
// only argument actually checked is the length of `times`, because pairing two
// parallel arrays is the one thing a caller here really does get wrong.

namespace CodingPatterns.Graphs;

public static class CourseScheduleTiming
{
    /// <summary>Returned when a cycle makes the schedule impossible.</summary>
    public const int Impossible = -1;

    // ------------------------------------------------------- LC 207 / LC 210

    /// <summary>
    /// LC 207. Kahn: repeatedly retire a course with no outstanding
    /// prerequisites. If fewer than <paramref name="numCourses"/> ever retire,
    /// the survivors all sit on a cycle -- every one of them is waiting on
    /// another survivor, so none of their in-degrees can ever reach 0.
    ///
    /// Zero courses is vacuously true, and an empty prerequisite list is true
    /// for any n. Both are legal inputs, both are worth stating out loud.
    /// </summary>
    public static bool CanFinish(int numCourses, int[][] prerequisites)
    {
        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);                    // finishing the prereq unblocks the course
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        int finished = 0;
        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            finished++;

            foreach (int next in adj[course])
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
        }

        return finished == numCourses;
    }

    /// <summary>
    /// LC 210. Any valid topological order, or an EMPTY array when a cycle makes
    /// completion impossible -- note that for numCourses = 0 the empty array is
    /// the valid answer, not the failure signal, so callers should compare
    /// lengths rather than test for emptiness.
    ///
    /// A Queue makes the order arbitrary among ready courses; swapping in a
    /// PriorityQueue yields the lexicographically smallest order, which is a
    /// common "and can you make it deterministic?" follow-up.
    /// </summary>
    public static int[] FindOrder(int numCourses, int[][] prerequisites)
    {
        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        var order = new List<int>(numCourses);
        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            order.Add(course);

            foreach (int next in adj[course])
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
        }

        return order.Count == numCourses ? order.ToArray() : Array.Empty<int>();
    }

    /// <summary>
    /// Cycle detection that hands back an actual cycle instead of a bare false.
    /// Kahn can tell you a cycle EXISTS (the leftovers) but not which nodes form
    /// one; three-colour DFS can, because a back edge to a grey (on-stack) node
    /// is a cycle and the current stack spells it out.
    ///
    ///   white = untouched, grey = on the recursion stack, black = fully done.
    ///
    /// The returned path starts and ends at the same course, e.g. [1, 2, 1].
    /// Iterative rather than recursive so a 2000-node chain cannot blow the
    /// stack -- worth mentioning even when the constraints make it moot.
    ///
    /// No in-degree array here: DFS only ever walks forward along edges.
    /// </summary>
    public static bool TryFindCycle(int numCourses, int[][] prerequisites, out List<int> cycle)
    {
        const int White = 0, Grey = 1, Black = 2;

        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        foreach (var p in prerequisites)
            adj[p[1]].Add(p[0]);

        var colour = new int[numCourses];
        var path = new List<int>();                 // the current grey stack, in order
        cycle = new List<int>();

        for (int start = 0; start < numCourses; start++)
        {
            if (colour[start] != White)
                continue;

            // (node, index of the next neighbour to try)
            var stack = new Stack<(int Node, int Next)>();
            colour[start] = Grey;
            path.Add(start);
            stack.Push((start, 0));

            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();

                if (next == adj[node].Count)
                {
                    colour[node] = Black;           // exhausted: node is off the stack
                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                stack.Push((node, next + 1));
                int neighbour = adj[node][next];

                if (colour[neighbour] == Grey)
                {
                    // Back edge. Everything from `neighbour` onward in the grey
                    // stack is the cycle; close it by repeating the entry node.
                    int from = path.IndexOf(neighbour);
                    cycle = path.GetRange(from, path.Count - from);
                    cycle.Add(neighbour);
                    return true;
                }

                if (colour[neighbour] == White)
                {
                    colour[neighbour] = Grey;
                    path.Add(neighbour);
                    stack.Push((neighbour, 0));
                }
            }
        }

        return false;
    }

    // ------------------------------------------- follow-up 1 & 2: batch model
    //
    // "A batch of courses must finish before the next batch begins."
    //
    // Everything currently unblocked runs in parallel, then a barrier. The
    // barrier costs the SLOWEST course in the batch, so wall clock is
    //
    //     total = sum over waves of max(times in that wave)
    //
    // With every duration 1 that collapses to "number of waves", which is the
    // BFS depth from the in-degree-0 frontier -- follow-up 1 is just follow-up 2
    // with a uniform times array.

    /// <summary>
    /// Follow-up 1: every course takes one unit. Total wall clock is the number
    /// of layered waves, i.e. the length of the longest prerequisite chain.
    /// <see cref="Impossible"/> on a cycle.
    /// </summary>
    public static long TotalTimeUniform(int numCourses, int[][] prerequisites)
    {
        var ones = new int[numCourses];
        Array.Fill(ones, 1);
        return TotalTimeBatched(numCourses, prerequisites, ones);
    }

    /// <summary>
    /// Follow-up 2: <paramref name="times"/>[i] is course i's duration and the
    /// next wave starts only when the whole current wave has finished, so each
    /// wave costs its maximum duration.
    ///
    /// The one subtlety over plain BFS: process the queue a full LEVEL at a
    /// time (snapshot Count before draining), because a course unblocked
    /// mid-drain belongs to the NEXT wave, not this one. Getting that wrong
    /// silently merges waves and under-counts.
    ///
    /// Returns <see cref="Impossible"/> when a cycle leaves courses unfinished.
    /// </summary>
    public static long TotalTimeBatched(int numCourses, int[][] prerequisites, int[] times)
    {
        if (times is null || times.Length != numCourses)
            throw new ArgumentException($"times must hold exactly {numCourses} durations", nameof(times));

        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        long total = 0;
        int finished = 0;

        while (queue.Count > 0)
        {
            int wave = queue.Count;                 // snapshot: this wave only
            int slowest = 0;

            for (int i = 0; i < wave; i++)
            {
                int course = queue.Dequeue();
                finished++;
                slowest = Math.Max(slowest, times[course]);

                foreach (int next in adj[course])
                    if (--inDegree[next] == 0)
                        queue.Enqueue(next);        // lands in the next wave
            }

            total += slowest;                       // the barrier costs the slowest
        }

        return finished == numCourses ? total : Impossible;
    }

    /// <summary>
    /// Which courses run in which wave -- the schedule behind
    /// <see cref="TotalTimeBatched"/>. Fewer courses than n means a cycle.
    /// </summary>
    public static List<List<int>> Waves(int numCourses, int[][] prerequisites)
    {
        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        var waves = new List<List<int>>();
        while (queue.Count > 0)
        {
            int size = queue.Count;
            var wave = new List<int>(size);

            for (int i = 0; i < size; i++)
            {
                int course = queue.Dequeue();
                wave.Add(course);

                foreach (int next in adj[course])
                    if (--inDegree[next] == 0)
                        queue.Enqueue(next);
            }

            waves.Add(wave);
        }

        return waves;
    }

    // ------------------------------- LC 2050: earliest finish, no global barrier
    //
    // The batch model is NOT what LC 2050 asks for, and this is the distinction
    // people lose the question on. There is no barrier: a course starts the
    // instant its own prerequisites are done, so independent courses finish at
    // different times and a fast branch never waits for a slow unrelated one.
    //
    //     finish[v] = time[v] + max(finish[p] for p in prereqs(v))    (0 if none)
    //     answer    = max(finish)
    //
    // Evaluate it in topological order and every prerequisite is already final
    // when v is popped -- that is the entire proof, and it is why Kahn and DP
    // fuse into one loop.

    /// <summary>
    /// LC 2050 verbatim, written exactly as the site hands it to you: courses
    /// are 1-indexed in <paramref name="relations"/> (= [prev, next]) while
    /// <paramref name="time"/> is 0-indexed, so time[i] belongs to course i + 1.
    /// The -1 lives in the build loop and nowhere else.
    ///
    /// Returns <see cref="Impossible"/> on a cycle, which LC's constraints rule
    /// out but an interviewer will not.
    /// </summary>
    public static long MinimumTime(int n, int[][] relations, int[] time)
    {
        if (time is null || time.Length != n)
            throw new ArgumentException($"time must hold exactly {n} durations", nameof(time));

        var adj = new List<int>[n];
        for (int i = 0; i < n; i++)
            adj[i] = new List<int>();

        var inDegree = new int[n];
        foreach (var r in relations)
        {
            int prev = r[0] - 1, next = r[1] - 1;   // 1-indexed input, 0-indexed arrays
            adj[prev].Add(next);
            inDegree[next]++;
        }

        var queue = new Queue<int>();
        var finish = new long[n];
        for (int i = 0; i < n; i++)
        {
            finish[i] = time[i];                    // no prerequisites yet seen: start at 0
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        int done = 0;
        long answer = 0;

        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            done++;
            answer = Math.Max(answer, finish[course]);

            foreach (int next in adj[course])
            {
                // `course` is final, so it is a settled lower bound on next's start.
                finish[next] = Math.Max(finish[next], finish[course] + time[next]);
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        return done == n ? answer : Impossible;
    }

    /// <summary>
    /// Per-course earliest finish time under the same model, 0-indexed and
    /// taking the LC 207 [course, prereq] layout. Returns null on a cycle.
    ///
    /// Useful on its own: the critical path is recovered by walking back from
    /// the argmax through whichever prerequisite achieved the max.
    /// </summary>
    public static long[] EarliestFinishTimes(int numCourses, int[][] prerequisites, int[] times)
    {
        if (times is null || times.Length != numCourses)
            throw new ArgumentException($"times must hold exactly {numCourses} durations", nameof(times));

        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        var queue = new Queue<int>();
        var finish = new long[numCourses];
        for (int i = 0; i < numCourses; i++)
        {
            finish[i] = times[i];
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        int done = 0;
        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            done++;

            foreach (int next in adj[course])
            {
                finish[next] = Math.Max(finish[next], finish[course] + times[next]);
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        return done == numCourses ? finish : null;
    }

    /// <summary>
    /// The makespan under the LC 2050 model, 0-indexed: the largest earliest
    /// finish time. <see cref="Impossible"/> on a cycle, 0 for zero courses.
    /// </summary>
    public static long EarliestCompletion(int numCourses, int[][] prerequisites, int[] times)
    {
        var finish = EarliestFinishTimes(numCourses, prerequisites, times);
        if (finish is null)
            return Impossible;

        long answer = 0;
        foreach (long f in finish)
            answer = Math.Max(answer, f);
        return answer;
    }

    // ------------------------------ follow-up 3: ANY one prerequisite suffices
    //
    // Flip the max to a min and the problem changes shape entirely:
    //
    //     start[v]  = min(finish[p] for p in prereqs(v))   (0 if none)
    //     finish[v] = start[v] + time[v]
    //
    // which is exactly shortest path from a virtual source that points at every
    // in-degree-0 course, with edge p -> v weighted by time[p]. Layered BFS is
    // WRONG here: it holds v until the whole wave clears, but v was free to
    // start the moment its fastest prerequisite finished. The batch answer is an
    // over-count, never an under-count.
    //
    // On a DAG the topological relaxation is the right tool -- O(V + E), no heap.
    // But Dijkstra is not merely a cross-check here, and this is the sharpest
    // observation available on this question:
    //
    //   Under ANY semantics a cycle is NOT automatically a deadlock.
    //
    // If course 1 requires {0, 2} and course 2 requires {1}, then 1 and 2 form a
    // cycle -- yet 1 can start as soon as root 0 finishes, and 2 follows. The
    // schedule is perfectly feasible. The real feasibility test stops being
    // "acyclic" and becomes "reachable from some in-degree-0 course", which is
    // exactly what Dijkstra computes. Kahn cannot express it: nothing inside a
    // cycle ever reaches in-degree 0, so the topological version rejects the
    // whole input. Both are below, and which one is correct depends entirely on
    // whether the interviewer's graph is guaranteed acyclic.

    /// <summary>
    /// Follow-up 3, topological relaxation. DAG only: returns the makespan (max
    /// finish), or <see cref="Impossible"/> on ANY cycle -- including cycles that
    /// are actually schedulable under these semantics (see
    /// <see cref="MinimumTimeAnyPrereqDijkstra"/>). Prefer this when the input is
    /// promised acyclic; it is O(V + E) with no heap.
    /// </summary>
    public static long MinimumTimeAnyPrereq(int numCourses, int[][] prerequisites, int[] times)
    {
        if (times is null || times.Length != numCourses)
            throw new ArgumentException($"times must hold exactly {numCourses} durations", nameof(times));

        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        const long Inf = long.MaxValue / 4;
        var queue = new Queue<int>();
        var start = new long[numCourses];
        for (int i = 0; i < numCourses; i++)
        {
            start[i] = inDegree[i] == 0 ? 0 : Inf;  // roots go immediately
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        int done = 0;
        long answer = 0;

        while (queue.Count > 0)
        {
            int course = queue.Dequeue();
            done++;

            long finish = start[course] + times[course];
            answer = Math.Max(answer, finish);

            foreach (int next in adj[course])
            {
                start[next] = Math.Min(start[next], finish);   // ANY one is enough
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        return done == numCourses ? answer : Impossible;
    }

    /// <summary>
    /// Dijkstra from a super-source: every in-degree-0 course enters the heap at
    /// distance 0, and relaxing p -> v costs time[p] because v may begin the
    /// moment p is done. Agrees with <see cref="MinimumTimeAnyPrereq"/> on every
    /// DAG, and stays correct on cyclic input where that one gives up.
    ///
    /// <see cref="Impossible"/> here means a course was left at infinity, i.e.
    /// unreachable from every root -- a genuine deadlock, a knot of courses each
    /// waiting only on each other. That, not acyclicity, is the real feasibility
    /// condition under ANY semantics.
    ///
    /// Lazy deletion (skip a popped entry whose key is stale) rather than
    /// decrease-key, which C#'s PriorityQueue does not offer. O(E log V).
    /// </summary>
    public static long MinimumTimeAnyPrereqDijkstra(int numCourses, int[][] prerequisites, int[] times)
    {
        if (times is null || times.Length != numCourses)
            throw new ArgumentException($"times must hold exactly {numCourses} durations", nameof(times));

        var adj = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            adj[i] = new List<int>();

        var inDegree = new int[numCourses];
        foreach (var p in prerequisites)
        {
            adj[p[1]].Add(p[0]);
            inDegree[p[0]]++;
        }

        const long Inf = long.MaxValue / 4;
        var start = new long[numCourses];
        Array.Fill(start, Inf);

        var heap = new PriorityQueue<int, long>();
        for (int i = 0; i < numCourses; i++)
            if (inDegree[i] == 0)
            {
                start[i] = 0;
                heap.Enqueue(i, 0);
            }

        long answer = 0;
        int settled = 0;

        while (heap.TryDequeue(out int course, out long key))
        {
            if (key > start[course])
                continue;                           // stale heap entry

            settled++;
            long finish = start[course] + times[course];
            answer = Math.Max(answer, finish);

            foreach (int next in adj[course])
                if (finish < start[next])
                {
                    start[next] = finish;
                    heap.Enqueue(next, finish);
                }
        }

        return settled == numCourses ? answer : Impossible;
    }

    // ------------------------------------------------------- the OOP wrapper
    //
    // Snowflake likes to hide the same graph behind objects: courses carry their
    // own prerequisite list, and an external didFail(course) can reject a
    // finished course and force a re-run. Nothing structural changes -- the
    // topological order still holds, because a failed course simply has not
    // finished yet, so its dependents stay blocked and go back on the queue.
    //
    // The only new requirement is a retry cap. Without one, a permanently
    // failing course spins the queue forever.

    /// <summary>A course as an object: identity, its own prerequisites, and a duration.</summary>
    public sealed class Course
    {
        public Course(int id, IEnumerable<int> prevCourses = null, int duration = 1)
        {
            Id = id;
            PrevCourses = new List<int>(prevCourses ?? Enumerable.Empty<int>());
            Duration = duration;
        }

        public int Id { get; }

        /// <summary>Ids that must finish before this course may start.</summary>
        public List<int> PrevCourses { get; }

        public int Duration { get; }

        public override string ToString() => $"Course({Id})";
    }

    public sealed record ScheduleResult(bool Completed, List<int> Order, int Attempts, int Retries);

    /// <summary>
    /// Kahn driven through the object API. <paramref name="run"/> stands in for
    /// the external didFail hook: true = the course completed, false = it must be
    /// re-run.
    ///
    /// A failed course is pushed back onto the queue and its dependents are NOT
    /// unblocked -- decrementing in-degree on a course that did not actually
    /// finish is the bug this shape is fishing for. Requeue at the BACK so other
    /// ready work proceeds meanwhile; requeueing at the front just spins on a
    /// flaky course.
    ///
    /// Bails out after <paramref name="maxAttemptsPerCourse"/> tries for any one
    /// course, and reports partial progress rather than throwing.
    /// </summary>
    public static ScheduleResult Schedule(
        IReadOnlyList<Course> courses, Func<Course, bool> run = null, int maxAttemptsPerCourse = 3)
    {
        if (courses is null)
            throw new ArgumentNullException(nameof(courses));
        if (maxAttemptsPerCourse < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttemptsPerCourse), "need at least one attempt");

        run ??= _ => true;

        // Ids are arbitrary ints rather than 0..n-1, so the arrays above become
        // dictionaries. Same Kahn, one indirection deeper.
        var byId = new Dictionary<int, Course>();
        foreach (var course in courses)
            if (!byId.TryAdd(course.Id, course))
                throw new ArgumentException($"duplicate course id {course.Id}", nameof(courses));

        var dependents = new Dictionary<int, List<int>>();
        var inDegree = new Dictionary<int, int>();
        foreach (var course in courses)
        {
            dependents.TryAdd(course.Id, new List<int>());
            inDegree.TryAdd(course.Id, 0);
        }

        foreach (var course in courses)
            foreach (int prev in course.PrevCourses)
            {
                if (!byId.ContainsKey(prev))
                    throw new ArgumentException($"course {course.Id} requires unknown course {prev}", nameof(courses));

                dependents[prev].Add(course.Id);
                inDegree[course.Id]++;
            }

        var queue = new Queue<int>();
        foreach (var course in courses)
            if (inDegree[course.Id] == 0)
                queue.Enqueue(course.Id);

        var attempts = new Dictionary<int, int>();
        var order = new List<int>();
        int totalAttempts = 0, retries = 0;

        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            attempts[id] = attempts.GetValueOrDefault(id) + 1;
            totalAttempts++;

            if (!run(byId[id]))
            {
                retries++;
                if (attempts[id] >= maxAttemptsPerCourse)
                    return new ScheduleResult(false, order, totalAttempts, retries);

                queue.Enqueue(id);                  // still unfinished: dependents stay blocked
                continue;
            }

            order.Add(id);
            foreach (int next in dependents[id])
                if (--inDegree[next] == 0)
                    queue.Enqueue(next);
        }

        return new ScheduleResult(order.Count == courses.Count, order, totalAttempts, retries);
    }

    // ------------------------------------------------------------------ tests

    public static void Run()
    {
        Console.WriteLine("== base: LC 207 / 210 ==");

        var chain = new[] { new[] { 1, 0 }, new[] { 2, 1 }, new[] { 3, 2 } };
        var cycle2 = new[] { new[] { 1, 0 }, new[] { 0, 1 } };

        Console.WriteLine($"  chain 0->1->2->3 finishable: {CanFinish(4, chain)} (expect True)");
        Console.WriteLine($"  order: [{string.Join(", ", FindOrder(4, chain))}] (expect 0, 1, 2, 3)");
        Console.WriteLine($"  2-cycle finishable: {CanFinish(2, cycle2)} (expect False)");
        Console.WriteLine($"  order on a cycle: [{string.Join(", ", FindOrder(2, cycle2))}] (expect empty)");
        Console.WriteLine($"  no prerequisites: {CanFinish(3, Array.Empty<int[]>())} (expect True)");
        Console.WriteLine($"  zero courses: {CanFinish(0, Array.Empty<int[]>())} (expect True -- vacuous)");
        Console.WriteLine($"  self-loop [[1,1]]: {CanFinish(2, new[] { new[] { 1, 1 } })} (expect False)");
        Console.WriteLine($"  duplicate edge [[1,0],[1,0]]: {CanFinish(2, new[] { new[] { 1, 0 }, new[] { 1, 0 } })} (expect True)");

        Console.WriteLine();
        Console.WriteLine("== cycle detection that names the cycle ==");

        Console.WriteLine($"  chain has a cycle: {TryFindCycle(4, chain, out var none)} (expect False)");
        TryFindCycle(2, cycle2, out var found2);
        Console.WriteLine($"  [[1,0],[0,1]] -> [{string.Join(" -> ", found2)}] (expect 0 -> 1 -> 0 or 1 -> 0 -> 1)");
        TryFindCycle(2, new[] { new[] { 1, 1 } }, out var self);
        Console.WriteLine($"  self-loop     -> [{string.Join(" -> ", self)}] (expect 1 -> 1)");

        // A cycle hanging off an acyclic prefix: the DFS must not report the prefix.
        var tail = new[] { new[] { 1, 0 }, new[] { 2, 1 }, new[] { 3, 2 }, new[] { 1, 3 } };
        TryFindCycle(4, tail, out var deep);
        Console.WriteLine($"  0 -> 1 -> 2 -> 3 -> 1 -> [{string.Join(" -> ", deep)}] (expect the 1,2,3 loop, no 0)");

        Console.WriteLine();
        Console.WriteLine("== follow-up 1: uniform durations, batch model ==");

        Console.WriteLine($"  chain of 4        -> {TotalTimeUniform(4, chain)} (expect 4: one course per wave)");
        Console.WriteLine($"  4 independent     -> {TotalTimeUniform(4, Array.Empty<int[]>())} (expect 1: all in one wave)");
        Console.WriteLine($"  cycle             -> {TotalTimeUniform(2, cycle2)} (expect -1)");
        Console.WriteLine($"  zero courses      -> {TotalTimeUniform(0, Array.Empty<int[]>())} (expect 0)");

        var diamond = new[] { new[] { 1, 0 }, new[] { 2, 0 }, new[] { 3, 1 }, new[] { 3, 2 } };
        Console.WriteLine($"  diamond 0->{{1,2}}->3 -> {TotalTimeUniform(4, diamond)} (expect 3)");
        Console.WriteLine($"  waves: {string.Join(" | ", Waves(4, diamond).Select(w => "{" + string.Join(",", w) + "}"))}");

        Console.WriteLine();
        Console.WriteLine("== follow-up 2: variable durations, batch model ==");

        // The prompt's example: 1->2 and 3->4 with times 1,10,10,1. Written twice
        // because the two models take different layouts -- [course, prereq]
        // 0-indexed for the batch model, LC 2050's 1-indexed [prev, next] for
        // MinimumTime. Same graph either way.
        // Wave {0,2} costs max(1,10) = 10; wave {1,3} costs max(10,1) = 10.
        var promptBatched = new[] { new[] { 1, 0 }, new[] { 3, 2 } };
        var promptRelations = new[] { new[] { 1, 2 }, new[] { 3, 4 } };
        var promptTimes = new[] { 1, 10, 10, 1 };

        Console.WriteLine("  prerequisites 1->2 and 3->4, times [1,10,10,1]");
        Console.WriteLine($"    batched (barrier)    -> {TotalTimeBatched(4, promptBatched, promptTimes)} (expect 20)");
        Console.WriteLine($"    LC 2050 (no barrier) -> {MinimumTime(4, promptRelations, promptTimes)} (expect 11)");
        Console.WriteLine("    ^ SAME INPUT, different models. Ask which one before coding.");

        var lineTimes = new[] { 3, 5, 2, 7 };
        Console.WriteLine($"  single chain, times [3,5,2,7] -> {TotalTimeBatched(4, chain, lineTimes)} (expect 17 = the sum)");
        Console.WriteLine($"  all independent, same times   -> {TotalTimeBatched(4, Array.Empty<int[]>(), lineTimes)} (expect 7 = the max)");
        Console.WriteLine($"  cycle                          -> {TotalTimeBatched(2, cycle2, new[] { 1, 1 })} (expect -1)");

        try
        {
            TotalTimeBatched(3, Array.Empty<int[]>(), new[] { 1, 2 });
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  wrong-length times rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== LC 2050: earliest finish, no barrier ==");

        // LC 2050 sample 2: 5 courses, times [1,2,3,4,5], answer 12.
        var lcRelations = new[] { new[] { 1, 5 }, new[] { 2, 5 }, new[] { 3, 5 }, new[] { 3, 4 }, new[] { 4, 5 } };
        Console.WriteLine($"  n=5 {string.Join(",", lcRelations.Select(r => $"{r[0]}->{r[1]}"))}, times [1,2,3,4,5]");
        Console.WriteLine($"    -> {MinimumTime(5, lcRelations, new[] { 1, 2, 3, 4, 5 })} (expect 12: 3 then 4 then 5)");
        Console.WriteLine($"  LC 2050 sample 1: {MinimumTime(3, new[] { new[] { 1, 3 }, new[] { 2, 3 } }, new[] { 3, 2, 5 })} (expect 8)");

        var finishes = EarliestFinishTimes(4, diamond, new[] { 2, 5, 1, 3 });
        Console.WriteLine($"  diamond finishes: [{string.Join(", ", finishes)}] (expect 2, 7, 3, 10)");
        Console.WriteLine($"  cycle -> {EarliestCompletion(2, cycle2, new[] { 1, 1 })} (expect -1)");
        Console.WriteLine($"  MinimumTime agrees with EarliestCompletion on the prompt input: "
            + $"{MinimumTime(4, promptRelations, promptTimes) == EarliestCompletion(4, promptBatched, promptTimes)}");

        Console.WriteLine();
        Console.WriteLine("== follow-up 3: ANY one prerequisite is enough ==");

        // 5 nodes, hand-checkable. Courses 0,1 are roots; 2 needs 0 AND/OR 1;
        // 3 needs 2; 4 is independent.
        //   times: 0 -> 1, 1 -> 100, 2 -> 1, 3 -> 1, 4 -> 1
        var anyEdges = new[] { new[] { 2, 0 }, new[] { 2, 1 }, new[] { 3, 2 } };
        var anyTimes = new[] { 1, 100, 1, 1, 1 };

        Console.WriteLine("  0(t=1) and 1(t=100) both feed 2(t=1) -> 3(t=1); 4(t=1) is independent");
        Console.WriteLine($"    ALL prerequisites (LC 2050): {EarliestCompletion(5, anyEdges, anyTimes)} (expect 102: 2 waits on the slow 1)");
        Console.WriteLine($"    ANY prerequisite (topo min): {MinimumTimeAnyPrereq(5, anyEdges, anyTimes)} (expect 100: only course 1 itself is slow)");
        Console.WriteLine($"    ANY prerequisite (Dijkstra): {MinimumTimeAnyPrereqDijkstra(5, anyEdges, anyTimes)} (must match)");
        Console.WriteLine($"    layered BFS (barrier):       {TotalTimeBatched(5, anyEdges, anyTimes)} (expect 102 -- over-counts, this is the trap)");
        Console.WriteLine("    The batch model holds 2 until the whole first wave clears; under ANY,");
        Console.WriteLine("    course 2 was free to start at t=1 the moment course 0 finished.");

        // A true deadlock: 0 and 1 wait only on each other, nothing else feeds them.
        Console.WriteLine($"  ANY on a closed 2-cycle, topo     -> {MinimumTimeAnyPrereq(2, cycle2, new[] { 1, 1 })} (expect -1)");
        Console.WriteLine($"  ANY on a closed 2-cycle, Dijkstra -> {MinimumTimeAnyPrereqDijkstra(2, cycle2, new[] { 1, 1 })} (expect -1)");

        // But a cycle FED BY A ROOT is schedulable under ANY semantics, and this
        // is where the two implementations legitimately disagree:
        //   0 is a root (t=2). 1 needs {0, 2}. 2 needs {1}.
        //   1 starts when 0 finishes at 2 -> finishes 5; 2 finishes 6.
        var fedCycle = new[] { new[] { 1, 0 }, new[] { 1, 2 }, new[] { 2, 1 } };
        var fedTimes = new[] { 2, 3, 1 };
        Console.WriteLine("  0(t=2) feeds 1(t=3); 1 and 2(t=1) require each other");
        Console.WriteLine($"    topo relaxation -> {MinimumTimeAnyPrereq(3, fedCycle, fedTimes)} (expect -1: Kahn cannot enter a cycle)");
        Console.WriteLine($"    Dijkstra        -> {MinimumTimeAnyPrereqDijkstra(3, fedCycle, fedTimes)} (expect 6: the schedule really is feasible)");
        Console.WriteLine("    Under ANY semantics the feasibility test is reachability, not acyclicity.");

        Console.WriteLine();
        Console.WriteLine("== the OOP wrapper (external didFail) ==");

        var objects = new List<Course>
        {
            new(0),
            new(1, new[] { 0 }),
            new(2, new[] { 0 }),
            new(3, new[] { 1, 2 }),
        };

        var clean = Schedule(objects);
        Console.WriteLine($"  clean run: completed={clean.Completed}, order=[{string.Join(", ", clean.Order)}], retries={clean.Retries}");

        // Course 1 fails once, then succeeds. Course 3 must still come last.
        int failuresLeft = 1;
        var flaky = Schedule(objects, c => c.Id != 1 || failuresLeft-- <= 0);
        Console.WriteLine($"  flaky course 1: completed={flaky.Completed}, order=[{string.Join(", ", flaky.Order)}], retries={flaky.Retries} (expect 1)");
        Console.WriteLine($"    3 still last, and 1 still precedes it: {flaky.Order.Last() == 3 && flaky.Order.IndexOf(1) < flaky.Order.IndexOf(3)}");

        var broken = Schedule(objects, c => c.Id != 2, maxAttemptsPerCourse: 3);
        Console.WriteLine($"  course 2 always fails: completed={broken.Completed} (expect False), got as far as [{string.Join(", ", broken.Order)}]");

        try
        {
            Schedule(new List<Course> { new(0, new[] { 9 }) });
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  dangling prerequisite rejected: {ex.Message.Split(" (Parameter")[0]}");
        }

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(2050);
        bool ordersValid = true, uniformAgrees = true, dpAgrees = true, anyAgrees = true, dijkstraAgrees = true;
        bool lcAgrees = true, batchNeverUnder = true, cyclesRejected = true, dijkstraOnCycles = true;
        int cyclesSeen = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            int n = rng.Next(0, 9);

            // Random DAG: draw edges only from a random permutation's prefix to
            // its suffix, which makes acyclicity structural rather than hoped-for.
            var label = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
            var edges = new List<int[]>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (rng.Next(4) == 0)
                        edges.Add(new[] { label[j], label[i] });   // [course, prereq]

            var dag = edges.ToArray();
            var times = new int[n];
            for (int i = 0; i < n; i++)
                times[i] = rng.Next(1, 12);

            // 1. The order really is a topological order.
            var order = FindOrder(n, dag);
            ordersValid &= order.Length == n;
            var position = new int[n];
            for (int i = 0; i < n; i++)
                position[order[i]] = i;
            foreach (var e in dag)
                ordersValid &= position[e[1]] < position[e[0]];

            // 2. Uniform durations: the batch total is the longest chain, which
            //    is also what the earliest-finish DP reports when every time is 1.
            var ones = new int[n];
            Array.Fill(ones, 1);
            uniformAgrees &= TotalTimeUniform(n, dag) == EarliestCompletion(n, dag, ones);

            // 3. Kahn's DP vs a plain recursive definition.
            dpAgrees &= EarliestCompletion(n, dag, times) == BruteForceFinish(n, dag, times, useMax: true);

            // 4. The 1-indexed LC 2050 entry point computes the same thing as the
            //    0-indexed one -- i.e. the ±1 in its build loop is right.
            var relations = dag.Select(e => new[] { e[1] + 1, e[0] + 1 }).ToArray();
            lcAgrees &= MinimumTime(n, relations, times) == EarliestCompletion(n, dag, times);

            // 5. The ANY variant, both ways, against the recursion.
            long anyTopo = MinimumTimeAnyPrereq(n, dag, times);
            anyAgrees &= anyTopo == BruteForceFinish(n, dag, times, useMax: false);
            dijkstraAgrees &= MinimumTimeAnyPrereqDijkstra(n, dag, times) == anyTopo;

            // 6. The barrier can only ever cost more than no barrier.
            batchNeverUnder &= TotalTimeBatched(n, dag, times) >= EarliestCompletion(n, dag, times);

            // 7. Add one back edge. Everything that requires acyclicity must
            //    report impossible -- but Dijkstra deliberately must NOT, because
            //    a cycle fed from outside is still schedulable under ANY
            //    semantics. It is checked against Bellman-Ford instead, which
            //    makes no acyclicity assumption at all.
            if (n >= 2 && dag.Length > 0)
            {
                var withCycle = dag.Append(new[] { dag[0][1], dag[0][0] }).ToArray();
                cyclesSeen++;

                cyclesRejected &=
                    !CanFinish(n, withCycle) &&
                    FindOrder(n, withCycle).Length == 0 &&
                    TotalTimeBatched(n, withCycle, times) == Impossible &&
                    EarliestCompletion(n, withCycle, times) == Impossible &&
                    MinimumTimeAnyPrereq(n, withCycle, times) == Impossible &&
                    TryFindCycle(n, withCycle, out _);

                dijkstraOnCycles &=
                    MinimumTimeAnyPrereqDijkstra(n, withCycle, times) == BellmanFordAny(n, withCycle, times);
            }
        }

        Console.WriteLine($"  4,000 random DAGs, every topological order valid:            {ordersValid}");
        Console.WriteLine($"  uniform durations: layered BFS == earliest-finish DP:        {uniformAgrees}");
        Console.WriteLine($"  earliest-finish DP == recursive definition:                  {dpAgrees}");
        Console.WriteLine($"  LC 2050 (1-indexed) == EarliestCompletion (0-indexed):       {lcAgrees}");
        Console.WriteLine($"  ANY-prerequisite DP == recursive definition:                 {anyAgrees}");
        Console.WriteLine($"  ANY-prerequisite DP == Dijkstra (on DAGs):                   {dijkstraAgrees}");
        Console.WriteLine($"  batch total >= no-barrier total, always:                     {batchNeverUnder}");
        Console.WriteLine($"  cyclic inputs rejected by every acyclic method:              {cyclesRejected} ({cyclesSeen} cases)");
        Console.WriteLine($"  ANY-prerequisite Dijkstra == Bellman-Ford on those cycles:   {dijkstraOnCycles}");
    }

    /// <summary>
    /// Reference implementation: the recurrence written out literally, with no
    /// topological sort at all. Exponential on a dense DAG, which is fine at
    /// n &lt;= 8 and is exactly why the memoized/Kahn version exists.
    ///
    /// useMax = true  -> wait for ALL prerequisites (LC 2050).
    /// useMax = false -> start after ANY one prerequisite (follow-up 3).
    /// </summary>
    private static long BruteForceFinish(int numCourses, int[][] prerequisites, int[] times, bool useMax)
    {
        var prereqs = new List<int>[numCourses];
        for (int i = 0; i < numCourses; i++)
            prereqs[i] = new List<int>();
        foreach (var e in prerequisites)
            prereqs[e[0]].Add(e[1]);

        long Finish(int course)
        {
            if (prereqs[course].Count == 0)
                return times[course];

            long start = useMax ? 0 : long.MaxValue;
            foreach (int p in prereqs[course])
                start = useMax ? Math.Max(start, Finish(p)) : Math.Min(start, Finish(p));

            return start + times[course];
        }

        long answer = 0;
        for (int i = 0; i < numCourses; i++)
            answer = Math.Max(answer, Finish(i));
        return answer;
    }

    /// <summary>
    /// Reference for the ANY variant that assumes nothing about the graph:
    /// relax every edge V times, Bellman-Ford style. O(V * E) and unbothered by
    /// cycles, which is exactly what makes it a fair judge of the Dijkstra
    /// version on cyclic input.
    /// </summary>
    private static long BellmanFordAny(int numCourses, int[][] prerequisites, int[] times)
    {
        const long Inf = long.MaxValue / 4;
        var start = new long[numCourses];
        var hasPrereq = new bool[numCourses];

        foreach (var e in prerequisites)
            hasPrereq[e[0]] = true;

        for (int i = 0; i < numCourses; i++)
            start[i] = hasPrereq[i] ? Inf : 0;

        for (int round = 0; round < numCourses; round++)
            foreach (var e in prerequisites)
            {
                int course = e[0], prereq = e[1];
                if (start[prereq] < Inf)
                    start[course] = Math.Min(start[course], start[prereq] + times[prereq]);
            }

        long answer = 0;
        for (int i = 0; i < numCourses; i++)
        {
            if (start[i] >= Inf)
                return Impossible;                  // never reachable from any root
            answer = Math.Max(answer, start[i] + times[i]);
        }

        return answer;
    }
}

// ---- Notes for the follow-up questions ----
//
// "Only K courses can run at once."
//     The barrier model dies here -- unlimited parallelism was doing all the
//     work. With a machine limit this becomes P|prec|Cmax, which is NP-hard in
//     general. Say so, then offer list scheduling (repeatedly assign the ready
//     course with the longest remaining critical path to the first free slot):
//     it is a 2 - 1/K approximation and it is what an interviewer wants to hear.
//
// "Which courses are on the critical path?"
//     Keep a parent pointer wherever EarliestFinishTimes takes the max, then walk
//     back from argmax(finish). Slack per course = latest start - earliest start,
//     computed by a second pass in reverse topological order. That is CPM, and it
//     is the same DP run backwards.
//
// "Print the lexicographically smallest valid order."
//     Swap Kahn's Queue for a PriorityQueue<int,int>. O((V + E) log V). Note that
//     this is NOT the same as the fastest schedule -- ordering ties by id says
//     nothing about durations.
//
// "Courses have a release time / cannot start before month r."
//     Initialize finish[v] = max(time[v], r[v] + time[v]) for roots and clamp
//     start[v] up to r[v] during relaxation. The topological DP is unchanged
//     otherwise; only the base case moves.
//
// "The prerequisite graph arrives as a stream of edges."
//     Kahn is a batch algorithm. For incremental edges keep an online
//     topological order (Pearce-Kelly): on inserting u -> v, if u already
//     precedes v nothing changes, otherwise reorder only the affected window,
//     and a cycle shows up when v can reach u.
//
// "The input uses the other edge order."
//     Every method here reads prerequisites[i] as [course, prereq]. If the
//     interviewer's pairs are [prereq, course], flip the two indices in that
//     method's build loop -- adj[p[0]].Add(p[1]) and inDegree[p[1]]++ -- and
//     nothing else changes. Say which layout you are assuming before you write
//     the loop.
//
// "n is 2000 and prerequisites is 5000" -- the LC 210 constraints.
//     Kahn at O(V + E) is ~7k operations. Even the O(V * E) Bellman-Ford-style
//     relaxation would pass, so reach for clarity over cleverness; the adjacency
//     list plus in-degree array is under 100 KB.
