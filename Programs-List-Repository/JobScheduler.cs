// Job Scheduler -- FCFS vs priority scheduling on a single machine
// Difficulty: Medium base, Hard follow-ups
// Pattern: discrete-event simulation over a ready queue; greedy pick at each decision point
//
// DO NOT TRUST THE FIELD NAMES. The prompt calls the fields start_time and
// end_time, which reads like "this job occupies the machine from start to end"
// -- an interval problem. It is not. Check the sample:
//
//     (start=0, end=5)  ->  Job 1: 0-5
//     (start=1, end=4)  ->  Job 2: 5-8      <- ran at 5, not at 1
//     (start=2, end=6)  ->  Job 3: 8-12     <- ran at 8, not at 2
//
// So start_time is an ARRIVAL (release) time and end_time - start_time is a
// DURATION (burst). Nothing occupies the machine at its stated end_time. This is
// classic OS CPU scheduling, and the whole question turns on noticing it. Say it
// out loud before writing a line: "so end_time is arrival + duration, and jobs
// queue up behind each other?"
//
// The five decisions that actually define the answer:
//
//   1. Which job runs next?          <- the algorithm (FCFS / priority / ...)
//   2. Chosen from WHICH set?        <- only jobs that have ARRIVED. This is the
//                                       trap: non-preemptive priority does NOT
//                                       run the globally best job first, it runs
//                                       the best job among those present.
//   3. Lower or higher priority      <- the prompt says BOTH ("lower = higher
//      value wins?                      priority" in the constraints, "highest
//                                       priority job first" in the example).
//                                       PriorityOrder makes it explicit.
//   4. Can a running job be          <- non-preemptive unless stated. Preemptive
//      interrupted?                     is a different schedule entirely.
//   5. Ties?                          <- arrival, then input order. Deterministic
//                                       output beats "any valid answer".
//
// The pieces, all on one shared simulation loop:
//
//   Fcfs                    ready queue keyed by arrival           O(n log n)
//   Priority                ready queue keyed by priority          O(n log n)
//   ShortestJobFirst        ready queue keyed by duration          O(n log n)
//   PreemptivePriority      re-decide at every arrival             O(n^2 log n) worst
//   ShortestRemainingTime   same, keyed by remaining work          O(n^2 log n) worst
//   RoundRobin              FIFO queue, fixed quantum              O(total/q)
//   PriorityWithAging       priority, but waiting improves the key O(n^2)
//
// The machine is IDLE whenever nothing has arrived: the clock jumps forward to
// the next arrival rather than pretending a job can start early. Every algorithm
// here is work-conserving (never idle while a job is ready), which buys a free
// invariant worth quoting in an interview:
//
//   ALL work-conserving policies finish at the same time.
//
// The busy periods are fixed by arrivals and durations alone -- reordering work
// inside a busy period cannot change when it drains. So FCFS, priority, SJF and
// round robin all share a makespan; they differ only in WAITING time, which is
// the actual thing you are being asked to trade off. The randomized block at the
// bottom checks this on 3,000 inputs.

using System.Text;

namespace CodingPatterns.Greedy;

/// <summary>Which job the scheduler picks next. There is no default on purpose.</summary>
public enum SchedulingAlgorithm
{
    /// <summary>First come first serve: arrival order, run to completion.</summary>
    Fcfs,

    /// <summary>Best priority among ARRIVED jobs, run to completion.</summary>
    Priority,

    /// <summary>Priority, but a better-priority arrival interrupts the running job.</summary>
    PreemptivePriority,

    /// <summary>Shortest duration among arrived jobs, run to completion (SJF / SPT).</summary>
    ShortestJobFirst,

    /// <summary>Preemptive SJF: least work remaining wins at every arrival (SRTF / SRPT).</summary>
    ShortestRemainingTime,

    /// <summary>FIFO with a fixed time quantum; a job that outlasts its slice goes to the back.</summary>
    RoundRobin,
}

/// <summary>Which way the priority number points. The prompt is ambiguous; ask.</summary>
public enum PriorityOrder
{
    /// <summary>Priority 1 beats priority 5 -- what the constraints say.</summary>
    LowerIsHigher,

    /// <summary>Priority 5 beats priority 1 -- what "highest priority job" suggests.</summary>
    HigherIsHigher,
}

public static class JobScheduler
{
    // ------------------------------------------------------------------ model

    /// <summary>
    /// One job. <paramref name="StartTime"/> is the ARRIVAL time and
    /// <paramref name="EndTime"/> encodes the duration as EndTime - StartTime;
    /// neither is when the job actually runs. <see cref="Arrival"/> and
    /// <see cref="Duration"/> exist so the rest of the file never has to
    /// re-derive that, and so the naming trap is stated exactly once.
    /// </summary>
    public sealed record Job(int Id, int StartTime, int EndTime, int Priority)
    {
        public int Arrival => StartTime;

        public int Duration => EndTime - StartTime;

        public override string ToString() => $"Job {Id}(arrive {Arrival}, run {Duration}, pri {Priority})";
    }

    /// <summary>A contiguous stretch of machine time given to one job.</summary>
    public sealed record Segment(int JobId, int Start, int End)
    {
        public int Length => End - Start;

        public override string ToString() => $"{Start}-{End}";
    }

    /// <summary>
    /// Per-job timing. Under preemption a job holds several
    /// <see cref="Segments"/>, so FirstStart and Finish are not one interval.
    ///
    ///   Turnaround = finish - arrival     (total time in the system)
    ///   Waiting    = turnaround - duration (time in the system NOT running)
    ///   Response   = first start - arrival (how long before anything happened)
    ///
    /// Waiting is what FCFS vs priority actually trades; response is what round
    /// robin buys and what an interactive system cares about.
    /// </summary>
    public sealed record JobStats(
        int JobId, int Arrival, int Duration, int Priority, int FirstStart, int Finish, IReadOnlyList<Segment> Segments)
    {
        public int Turnaround => Finish - Arrival;

        public int Waiting => Turnaround - Duration;

        public int Response => FirstStart - Arrival;
    }

    /// <summary>The finished schedule: the timeline, the per-job numbers, the averages.</summary>
    public sealed class ScheduleResult
    {
        internal ScheduleResult(
            SchedulingAlgorithm algorithm, IReadOnlyList<Segment> timeline, IReadOnlyList<JobStats> jobs)
        {
            Algorithm = algorithm;
            Timeline = timeline;
            Jobs = jobs;
        }

        public SchedulingAlgorithm Algorithm { get; }

        /// <summary>Every stretch of machine time, chronological, adjacent runs of one job merged.</summary>
        public IReadOnlyList<Segment> Timeline { get; }

        /// <summary>One entry per job, in EXECUTION order (by first start), not by id.</summary>
        public IReadOnlyList<JobStats> Jobs { get; }

        /// <summary>When the last job finishes. Identical across every algorithm here -- see the header.</summary>
        public int Makespan => Timeline.Count == 0 ? 0 : Timeline[^1].End;

        /// <summary>Machine time wasted between the first arrival and the makespan, i.e. gaps where nothing had arrived.</summary>
        public int IdleTime => Timeline.Count == 0
            ? 0
            : Makespan - Timeline[0].Start - Jobs.Sum(j => j.Duration);

        public double AverageWaiting => Jobs.Count == 0 ? 0 : Jobs.Average(j => (double)j.Waiting);

        public double AverageTurnaround => Jobs.Count == 0 ? 0 : Jobs.Average(j => (double)j.Turnaround);

        public double AverageResponse => Jobs.Count == 0 ? 0 : Jobs.Average(j => (double)j.Response);

        /// <summary>
        /// The output the prompt asks for -- one line per job in execution order:
        /// <c>Job 1: 0-5</c>. A preempted job lists each stretch:
        /// <c>Job 1: 0-1, 4-8</c>.
        /// </summary>
        public string Format()
        {
            var sb = new StringBuilder();
            foreach (var job in Jobs)
                sb.AppendLine($"Job {job.JobId}: {string.Join(", ", job.Segments.Select(s => s.ToString()))}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The same schedule with the timing columns an interviewer asks for next.</summary>
        public string FormatTable()
        {
            var sb = new StringBuilder();
            sb.AppendLine("  job  arrive  run  pri   start  finish   wait  turnaround");
            foreach (var j in Jobs)
                sb.AppendLine(
                    $"  {j.JobId,3}  {j.Arrival,6}  {j.Duration,3}  {j.Priority,3}   " +
                    $"{j.FirstStart,5}  {j.Finish,6}   {j.Waiting,4}  {j.Turnaround,10}");
            sb.Append(
                $"  makespan {Makespan}, idle {IdleTime}, avg wait {AverageWaiting:0.##}, " +
                $"avg turnaround {AverageTurnaround:0.##}");
            return sb.ToString();
        }
    }

    /// <summary>Builds jobs from (start, end, priority) triples, numbering them 1..n like the prompt's output.</summary>
    public static IReadOnlyList<Job> Build(params (int Start, int End, int Priority)[] specs)
        => (specs ?? Array.Empty<(int, int, int)>())
            .Select((s, i) => new Job(i + 1, s.Start, s.End, s.Priority))
            .ToList();

    // -------------------------------------------------------------- the entry

    /// <summary>
    /// Runs <paramref name="jobs"/> under <paramref name="algorithm"/> on one
    /// machine. <paramref name="quantum"/> is used only by
    /// <see cref="SchedulingAlgorithm.RoundRobin"/>.
    ///
    /// Jobs never start before they arrive, and the machine idles (clock jumps)
    /// when the ready set is empty. An empty job list is a legal input and
    /// produces an empty schedule, not a crash.
    /// </summary>
    public static ScheduleResult Schedule(
        IReadOnlyList<Job> jobs,
        SchedulingAlgorithm algorithm,
        PriorityOrder order = PriorityOrder.LowerIsHigher,
        int quantum = 1)
    {
        Validate(jobs);

        var slices = algorithm switch
        {
            SchedulingAlgorithm.RoundRobin => RoundRobinSlices(jobs, quantum),

            SchedulingAlgorithm.Fcfs or
            SchedulingAlgorithm.Priority or
            SchedulingAlgorithm.ShortestJobFirst => NonPreemptive(jobs, KeyFor(jobs, algorithm, order)),

            SchedulingAlgorithm.PreemptivePriority or
            SchedulingAlgorithm.ShortestRemainingTime => Preemptive(jobs, KeyFor(jobs, algorithm, order)),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "unknown algorithm"),
        };

        return Assemble(jobs, algorithm, slices);
    }

    /// <summary>
    /// The tie-break key for the ready queue, smallest first. Every algorithm is
    /// the SAME loop with a different key -- that is the point of the design, and
    /// it is the thing to say when asked "now add SJF".
    ///
    /// Arrival then input index always trail the primary key so the schedule is
    /// deterministic; "any valid answer" is a much worse thing to hand over than
    /// a stable one.
    /// </summary>
    private static Func<int, int, (int, int, int)> KeyFor(
        IReadOnlyList<Job> jobs, SchedulingAlgorithm algorithm, PriorityOrder order)
    {
        int Pri(int i) => order == PriorityOrder.LowerIsHigher ? jobs[i].Priority : -jobs[i].Priority;

        return algorithm switch
        {
            // FCFS ignores priority entirely -- arrival IS the key.
            SchedulingAlgorithm.Fcfs => (i, _) => (jobs[i].Arrival, i, 0),

            SchedulingAlgorithm.Priority or
            SchedulingAlgorithm.PreemptivePriority => (i, _) => (Pri(i), jobs[i].Arrival, i),

            SchedulingAlgorithm.ShortestJobFirst => (i, _) => (jobs[i].Duration, jobs[i].Arrival, i),

            // The only key that moves: remaining work shrinks as the job runs.
            SchedulingAlgorithm.ShortestRemainingTime => (i, remaining) => (remaining, jobs[i].Arrival, i),

            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "unknown algorithm"),
        };
    }

    // ------------------------------------------------------ non-preemptive run
    //
    // The whole simulation in one loop:
    //
    //   admit everything that has arrived by `time`
    //   nothing ready?  -> the machine idles; jump the clock to the next arrival
    //   otherwise       -> pop the best, run it to completion, advance the clock
    //
    // The admitted job's arrival is <= time by construction, so its start is just
    // `time` -- no max() needed, which is the small proof that makes the code
    // shorter than the description.

    private static List<Segment> NonPreemptive(IReadOnlyList<Job> jobs, Func<int, int, (int, int, int)> key)
    {
        int n = jobs.Count;
        var arrivals = ByArrival(jobs);
        var ready = new PriorityQueue<int, (int, int, int)>();
        var timeline = new List<Segment>(n);

        int next = 0, time = 0;

        while (timeline.Count < n)
        {
            while (next < n && jobs[arrivals[next]].Arrival <= time)
            {
                int i = arrivals[next++];
                ready.Enqueue(i, key(i, jobs[i].Duration));
            }

            if (ready.Count == 0)
            {
                time = jobs[arrivals[next]].Arrival;        // idle gap: nothing has arrived yet
                continue;
            }

            int idx = ready.Dequeue();
            int finish = time + jobs[idx].Duration;          // non-preemptive: runs to completion
            timeline.Add(new Segment(jobs[idx].Id, time, finish));
            time = finish;
        }

        return timeline;
    }

    // ---------------------------------------------------------- preemptive run
    //
    // Same loop, but the running job is only committed until the next EVENT, and
    // the only event that can change the decision is an arrival (a completion is
    // already the end of the slice). So: run to min(remaining, next arrival),
    // then re-decide from scratch. Emitting a slice per event and merging
    // adjacent slices afterwards keeps the loop obviously correct instead of
    // juggling "am I still the best job?" state.
    //
    // Slices are strictly positive: everything with arrival <= time was already
    // admitted, so the next arrival is strictly in the future.

    private static List<Segment> Preemptive(IReadOnlyList<Job> jobs, Func<int, int, (int, int, int)> key)
    {
        int n = jobs.Count;
        var arrivals = ByArrival(jobs);
        var remaining = jobs.Select(j => j.Duration).ToArray();
        var ready = new PriorityQueue<int, (int, int, int)>();
        var slices = new List<Segment>();

        int next = 0, time = 0, done = 0;

        while (done < n)
        {
            while (next < n && jobs[arrivals[next]].Arrival <= time)
            {
                int i = arrivals[next++];
                ready.Enqueue(i, key(i, remaining[i]));
            }

            if (ready.Count == 0)
            {
                time = jobs[arrivals[next]].Arrival;
                continue;
            }

            int idx = ready.Dequeue();
            int slice = remaining[idx];
            if (next < n)
                slice = Math.Min(slice, jobs[arrivals[next]].Arrival - time);

            slices.Add(new Segment(jobs[idx].Id, time, time + slice));
            time += slice;
            remaining[idx] -= slice;

            if (remaining[idx] > 0)
                ready.Enqueue(idx, key(idx, remaining[idx]));   // re-keyed: SRTF's key just moved
            else
                done++;
        }

        return slices;
    }

    // --------------------------------------------------------- round robin run
    //
    // No priority queue at all -- a plain FIFO plus a quantum. The one detail
    // that separates a correct implementation from a plausible one: when a
    // quantum expires at the same instant a job arrives, the ARRIVAL goes into
    // the queue first and the preempted job goes behind it. Enqueue them the
    // other way round and the preempted job jumps ahead of a job that has been
    // waiting since before it started -- a real bug, and the standard textbook
    // convention is the one implemented here.

    private static List<Segment> RoundRobinSlices(IReadOnlyList<Job> jobs, int quantum)
    {
        if (quantum < 1)
            throw new ArgumentOutOfRangeException(nameof(quantum), "quantum must be at least 1");

        int n = jobs.Count;
        var arrivals = ByArrival(jobs);
        var remaining = jobs.Select(j => j.Duration).ToArray();
        var queue = new Queue<int>();
        var slices = new List<Segment>();

        int next = 0, time = 0, done = 0;

        while (done < n)
        {
            while (next < n && jobs[arrivals[next]].Arrival <= time)
                queue.Enqueue(arrivals[next++]);

            if (queue.Count == 0)
            {
                time = jobs[arrivals[next]].Arrival;
                continue;
            }

            int idx = queue.Dequeue();
            int slice = Math.Min(quantum, remaining[idx]);

            slices.Add(new Segment(jobs[idx].Id, time, time + slice));
            time += slice;
            remaining[idx] -= slice;

            // Arrivals during the slice are admitted BEFORE the preempted job returns.
            while (next < n && jobs[arrivals[next]].Arrival <= time)
                queue.Enqueue(arrivals[next++]);

            if (remaining[idx] > 0)
                queue.Enqueue(idx);
            else
                done++;
        }

        return slices;
    }

    // ------------------------------------------------- the starvation follow-up
    //
    // "What if a low-priority job never runs?" -- the answer interviewers want is
    // aging: a job's effective priority improves the longer it has waited.
    //
    //     effective(j, t) = priority(j) - (t - arrival(j)) / agingInterval
    //
    // With that, waiting is bounded: after enough intervals ANY job outranks the
    // stream in front of it, so no job waits forever. The cost is that the jobs
    // it overtakes now wait longer, and since one long job overtaking several
    // short ones adds more delay than it sheds, the AVERAGE wait usually gets
    // worse. Aging buys a bound on the worst case, not throughput -- saying that
    // out loud is the difference between reciting the fix and understanding it.
    //
    // The key now depends on the current time, so it cannot be cached in a heap;
    // this rescans the ready set at every decision point. O(n^2), and fine.

    /// <summary>
    /// Non-preemptive priority with aging. <paramref name="agingInterval"/> is
    /// how many time units of waiting buy one point of priority.
    /// </summary>
    public static ScheduleResult PriorityWithAging(
        IReadOnlyList<Job> jobs, int agingInterval, PriorityOrder order = PriorityOrder.LowerIsHigher)
    {
        Validate(jobs);
        if (agingInterval < 1)
            throw new ArgumentOutOfRangeException(nameof(agingInterval), "aging interval must be at least 1");

        int n = jobs.Count;
        var arrivals = ByArrival(jobs);
        var ready = new List<int>();
        var timeline = new List<Segment>(n);

        int Pri(int i) => order == PriorityOrder.LowerIsHigher ? jobs[i].Priority : -jobs[i].Priority;

        int next = 0, time = 0;

        while (timeline.Count < n)
        {
            while (next < n && jobs[arrivals[next]].Arrival <= time)
                ready.Add(arrivals[next++]);

            if (ready.Count == 0)
            {
                time = jobs[arrivals[next]].Arrival;
                continue;
            }

            int best = -1;
            (int, int, int) bestKey = default;
            foreach (int i in ready)
            {
                var key = (Pri(i) - (time - jobs[i].Arrival) / agingInterval, jobs[i].Arrival, i);
                if (best < 0 || Comparer<(int, int, int)>.Default.Compare(key, bestKey) < 0)
                    (best, bestKey) = (i, key);
            }

            ready.Remove(best);
            int finish = time + jobs[best].Duration;
            timeline.Add(new Segment(jobs[best].Id, time, finish));
            time = finish;
        }

        return Assemble(jobs, SchedulingAlgorithm.Priority, timeline);
    }

    // ------------------------------------------------------------- plumbing

    private static int[] ByArrival(IReadOnlyList<Job> jobs)
        => Enumerable.Range(0, jobs.Count)
            .OrderBy(i => jobs[i].Arrival)
            .ThenBy(i => i)                                  // stable: input order breaks arrival ties
            .ToArray();

    /// <summary>
    /// Merges adjacent slices of the same job, then derives the per-job numbers.
    /// Everything downstream reads the merged timeline, so a preemptive schedule
    /// and a non-preemptive one print through the same code path.
    /// </summary>
    private static ScheduleResult Assemble(
        IReadOnlyList<Job> jobs, SchedulingAlgorithm algorithm, List<Segment> slices)
    {
        var timeline = new List<Segment>(slices.Count);
        foreach (var slice in slices)
        {
            if (timeline.Count > 0 && timeline[^1].JobId == slice.JobId && timeline[^1].End == slice.Start)
                timeline[^1] = timeline[^1] with { End = slice.End };
            else
                timeline.Add(slice);
        }

        var byJob = new Dictionary<int, List<Segment>>();
        foreach (var segment in timeline)
        {
            if (!byJob.TryGetValue(segment.JobId, out var list))
                byJob[segment.JobId] = list = new List<Segment>();
            list.Add(segment);
        }

        var stats = new List<JobStats>(jobs.Count);
        foreach (var job in jobs)
        {
            var segments = byJob[job.Id];
            stats.Add(new JobStats(
                job.Id, job.Arrival, job.Duration, job.Priority, segments[0].Start, segments[^1].End, segments));
        }

        stats.Sort((a, b) => a.FirstStart.CompareTo(b.FirstStart));   // execution order, as the prompt prints it
        return new ScheduleResult(algorithm, timeline, stats);
    }

    private static void Validate(IReadOnlyList<Job> jobs)
    {
        if (jobs is null)
            throw new ArgumentNullException(nameof(jobs));

        var seen = new HashSet<int>();
        foreach (var job in jobs)
        {
            if (job is null)
                throw new ArgumentException("job list contains a null", nameof(jobs));
            if (job.StartTime < 0)
                throw new ArgumentException($"job {job.Id} arrives at {job.StartTime}; arrival cannot be negative", nameof(jobs));
            if (job.EndTime <= job.StartTime)
                throw new ArgumentException($"job {job.Id} has duration {job.Duration}; duration must be positive", nameof(jobs));
            if (job.Priority == int.MinValue)
                throw new ArgumentException($"job {job.Id} has an un-negatable priority", nameof(jobs));   // HigherIsHigher flips the sign
            if (!seen.Add(job.Id))
                throw new ArgumentException($"duplicate job id {job.Id}", nameof(jobs));
        }
    }

    // ------------------------------------------------------------------ tests

    public static void Main()
    {
        Console.WriteLine("== the prompt's example, FCFS ==");

        // start = arrival, end - start = duration. NOT an occupancy interval.
        var sample = Build((0, 5, 2), (1, 4, 1), (2, 6, 3));
        Console.WriteLine("  jobs: " + string.Join("  ", sample));

        var fcfs = Schedule(sample, SchedulingAlgorithm.Fcfs);
        Console.WriteLine(Indent(fcfs.Format()));
        Console.WriteLine($"  matches the expected output: " +
                          $"{fcfs.Format() == "Job 1: 0-5\nJob 2: 5-8\nJob 3: 8-12".Replace("\n", Environment.NewLine)}");
        Console.WriteLine(fcfs.FormatTable());

        Console.WriteLine();
        Console.WriteLine("== the same jobs, priority (lower value wins) ==");

        var priority = Schedule(sample, SchedulingAlgorithm.Priority);
        Console.WriteLine(Indent(priority.Format()));
        Console.WriteLine("  IDENTICAL to FCFS -- and that is the lesson. At t=0 only job 1 has");
        Console.WriteLine("  arrived, so it runs regardless of its priority, and non-preemptive means");
        Console.WriteLine("  nothing can take the machine back until t=5.");

        Console.WriteLine();
        Console.WriteLine("== the same jobs, higher value wins ==");

        var flipped = Schedule(sample, SchedulingAlgorithm.Priority, PriorityOrder.HigherIsHigher);
        Console.WriteLine(Indent(flipped.Format()));
        Console.WriteLine("  Jobs 2 and 3 swap. Same input, same algorithm name, different answer --");
        Console.WriteLine("  which is why the direction of the priority number is a question, not a guess.");

        Console.WriteLine();
        Console.WriteLine("== the same jobs, preemptive priority ==");

        var preempt = Schedule(sample, SchedulingAlgorithm.PreemptivePriority);
        Console.WriteLine(Indent(preempt.Format()));
        Console.WriteLine("  Now job 2 (priority 1) really does win: it arrives at t=1 and takes the");
        Console.WriteLine("  machine off job 1, which resumes at t=4 with 4 units left.");
        Console.WriteLine($"  avg wait: FCFS {fcfs.AverageWaiting:0.##}, priority {priority.AverageWaiting:0.##}, " +
                          $"preemptive {preempt.AverageWaiting:0.##}");
        Console.WriteLine($"  makespan is the same for all three: " +
                          $"{fcfs.Makespan} / {priority.Makespan} / {preempt.Makespan}");

        Console.WriteLine();
        Console.WriteLine("== the prompt's OTHER example: priorities [3,1,2], arrivals [0,1,2] ==");

        // "priority scheduling would execute the highest priority job first" --
        // true only if everything is present at t=0, or if preemption is allowed.
        var intro = Build((0, 4, 3), (1, 4, 1), (2, 4, 2));      // durations 4, 3, 2
        Console.WriteLine("  " + string.Join("  ", intro));
        Console.WriteLine("  non-preemptive priority:");
        Console.WriteLine(Indent(Schedule(intro, SchedulingAlgorithm.Priority).Format(), 4));
        Console.WriteLine("    -> job 1 runs first despite the WORST priority: it was the only arrival at t=0.");
        Console.WriteLine("  preemptive priority:");
        Console.WriteLine(Indent(Schedule(intro, SchedulingAlgorithm.PreemptivePriority).Format(), 4));
        Console.WriteLine("    -> the priority-1 job takes over one unit in, as the prompt's sentence implies.");

        var together = Build((0, 4, 3), (0, 3, 1), (0, 2, 2));   // same durations, all arriving at 0
        Console.WriteLine("  all three arriving at t=0:");
        Console.WriteLine(Indent(Schedule(together, SchedulingAlgorithm.Priority).Format(), 4));
        Console.WriteLine("    -> job 2, job 3, job 1: strict priority order, because arrival stops mattering.");

        Console.WriteLine();
        Console.WriteLine("== idle time ==");

        var gap = Build((0, 2, 1), (10, 13, 1));
        var idle = Schedule(gap, SchedulingAlgorithm.Fcfs);
        Console.WriteLine(Indent(idle.Format()));
        Console.WriteLine($"  job 2 arrives at 10, so the machine sits idle 2..10: idle={idle.IdleTime} (expect 8)");
        Console.WriteLine("  the clock JUMPS to the next arrival -- it never starts a job early to fill the gap.");

        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        Console.WriteLine($"  no jobs: makespan {Schedule(Build(), SchedulingAlgorithm.Fcfs).Makespan}, " +
                          $"output '{Schedule(Build(), SchedulingAlgorithm.Fcfs).Format()}' (expect 0, empty)");
        Console.WriteLine($"  one job arriving late: {Schedule(Build((7, 9, 1)), SchedulingAlgorithm.Priority).Format()} (expect 7-9)");
        Console.WriteLine($"  simultaneous arrivals, equal priority: " +
                          $"{Schedule(Build((0, 3, 1), (0, 2, 1), (0, 4, 1)), SchedulingAlgorithm.Priority).Format().Replace(Environment.NewLine, " | ")}");
        Console.WriteLine("    ^ ties fall back to input order, so the output is stable run to run.");

        foreach (var (label, bad) in new (string, Func<ScheduleResult>)[]
                 {
                     ("zero duration", () => Schedule(Build((3, 3, 1)), SchedulingAlgorithm.Fcfs)),
                     ("end before start", () => Schedule(Build((5, 2, 1)), SchedulingAlgorithm.Fcfs)),
                     ("negative arrival", () => Schedule(Build((-1, 4, 1)), SchedulingAlgorithm.Fcfs)),
                     ("quantum 0", () => Schedule(Build((0, 4, 1)), SchedulingAlgorithm.RoundRobin, quantum: 0)),
                 })
        {
            try
            {
                bad();
                Console.WriteLine($"  {label}: NOT rejected -- bug");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"  {label} rejected: {ex.Message.Split(" (Parameter")[0]}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("== the other policies, one input ==");

        // 1: arrives 0, runs 7, pri 3   2: arrives 2, runs 4, pri 1
        // 3: arrives 4, runs 1, pri 2   4: arrives 5, runs 4, pri 2
        var mixed = Build((0, 7, 3), (2, 6, 1), (4, 5, 2), (5, 9, 2));
        Console.WriteLine("  " + string.Join("  ", mixed));

        foreach (var algorithm in Enum.GetValues<SchedulingAlgorithm>())
        {
            var result = Schedule(mixed, algorithm, quantum: 2);
            Console.WriteLine($"  {algorithm,-22} {result.Format().Replace(Environment.NewLine, " | ")}");
            Console.WriteLine($"  {"",-22} makespan {result.Makespan}, avg wait {result.AverageWaiting,5:0.##}, " +
                              $"avg turnaround {result.AverageTurnaround,5:0.##}, avg response {result.AverageResponse,5:0.##}");
        }

        Console.WriteLine("  ^ same makespan everywhere (work-conserving), and SRTF has the best average");
        Console.WriteLine("    turnaround -- that is a theorem, not luck. Round robin trades turnaround");
        Console.WriteLine("    for RESPONSE, which is the tradeoff an interactive scheduler is buying.");

        Console.WriteLine();
        Console.WriteLine("== starvation, and aging as the fix ==");

        // One long low-priority job against a stream of short high-priority ones.
        var starve = Build((0, 8, 5), (0, 3, 1), (2, 5, 1), (4, 7, 1), (6, 9, 1));
        var starved = Schedule(starve, SchedulingAlgorithm.Priority);
        var aged = PriorityWithAging(starve, agingInterval: 1);

        Console.WriteLine($"  plain priority: {starved.Format().Replace(Environment.NewLine, " | ")}");
        Console.WriteLine($"    job 1 waits {starved.Jobs.First(j => j.JobId == 1).Waiting}, " +
                          $"worst wait {starved.Jobs.Max(j => j.Waiting)}");
        Console.WriteLine($"  with aging(1):  {aged.Format().Replace(Environment.NewLine, " | ")}");
        Console.WriteLine($"    job 1 waits {aged.Jobs.First(j => j.JobId == 1).Waiting}, " +
                          $"worst wait {aged.Jobs.Max(j => j.Waiting)}");
        Console.WriteLine($"  avg wait {starved.AverageWaiting:0.##} -> {aged.AverageWaiting:0.##}");
        Console.WriteLine("  Aging caps the worst wait by pushing the short jobs back, and the AVERAGE");
        Console.WriteLine("  gets worse doing it -- overtaking four short jobs with one long one costs");
        Console.WriteLine("  more total delay than it saves. Aging buys a starvation bound, not throughput.");

        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(1337);
        var algorithms = Enum.GetValues<SchedulingAlgorithm>();

        bool schedulesValid = true, makespanInvariant = true, srtfBestTurnaround = true;
        bool fcfsIsArrivalOrder = true, bigQuantumIsFcfs = true, flatPriorityIsFcfs = true, agingValid = true;

        for (int trial = 0; trial < 3000; trial++)
        {
            int n = rng.Next(0, 8);
            var jobs = new List<Job>(n);
            for (int i = 0; i < n; i++)
            {
                int arrival = rng.Next(0, 12);
                jobs.Add(new Job(i + 1, arrival, arrival + rng.Next(1, 8), rng.Next(0, 5)));
            }

            int quantum = rng.Next(1, 5);
            var results = algorithms.Select(a => Schedule(jobs, a, quantum: quantum)).ToList();

            foreach (var result in results)
                schedulesValid &= IsValid(jobs, result);

            // Every policy here is work-conserving, so the busy periods -- and
            // therefore the finish time -- are fixed by the input alone.
            makespanInvariant &= results.Select(r => r.Makespan).Distinct().Count() <= 1;

            // SRPT is optimal for total flow time on one machine with release
            // times, so nothing else can beat it on average turnaround.
            double srtf = results.First(r => r.Algorithm == SchedulingAlgorithm.ShortestRemainingTime).AverageTurnaround;
            srtfBestTurnaround &= results.All(r => srtf <= r.AverageTurnaround + 1e-9);

            // FCFS executes in arrival order, by definition.
            var fcfsOrder = results.First(r => r.Algorithm == SchedulingAlgorithm.Fcfs).Jobs;
            fcfsIsArrivalOrder &= fcfsOrder
                .Zip(fcfsOrder.Skip(1))
                .All(p => Comparer<(int, int)>.Default.Compare(
                              (p.First.Arrival, p.First.JobId), (p.Second.Arrival, p.Second.JobId)) <= 0);

            // A quantum no job can exhaust degenerates round robin into FCFS.
            bigQuantumIsFcfs &= Schedule(jobs, SchedulingAlgorithm.RoundRobin, quantum: 100).Format()
                == results.First(r => r.Algorithm == SchedulingAlgorithm.Fcfs).Format();

            // With every priority equal, nothing ever out-ranks the incumbent, so
            // preemptive priority must collapse to FCFS -- no preemption at all.
            var flat = jobs.Select(j => j with { Priority = 7 }).ToList();
            flatPriorityIsFcfs &= Schedule(flat, SchedulingAlgorithm.PreemptivePriority).Format()
                == Schedule(flat, SchedulingAlgorithm.Fcfs).Format();

            var agedResult = PriorityWithAging(jobs, rng.Next(1, 4));
            agingValid &= IsValid(jobs, agedResult) && agedResult.Makespan == results[0].Makespan;
        }

        Console.WriteLine($"  3,000 random inputs x {algorithms.Length} policies, every schedule legal:  {schedulesValid}");
        Console.WriteLine($"  makespan identical across all policies (work-conserving):       {makespanInvariant}");
        Console.WriteLine($"  SRTF never beaten on average turnaround (SRPT optimality):      {srtfBestTurnaround}");
        Console.WriteLine($"  FCFS runs jobs in arrival order:                                {fcfsIsArrivalOrder}");
        Console.WriteLine($"  round robin with an oversized quantum == FCFS:                  {bigQuantumIsFcfs}");
        Console.WriteLine($"  preemptive priority with equal priorities == FCFS:              {flatPriorityIsFcfs}");
        Console.WriteLine($"  aged schedules legal and same makespan:                         {agingValid}");

        // SJF minimizes average waiting when everything is available up front
        // (1||sum C_j). Checked against every permutation, which is the honest
        // way to show a greedy rule is optimal rather than just plausible.
        bool sjfOptimal = true;
        for (int trial = 0; trial < 200; trial++)
        {
            int n = rng.Next(1, 7);
            var jobs = Enumerable.Range(0, n)
                .Select(i => new Job(i + 1, 0, rng.Next(1, 15), rng.Next(0, 5)))
                .ToList();

            double best = Permutations(Enumerable.Range(0, n).ToArray())
                .Min(order =>
                {
                    int time = 0, waiting = 0;
                    foreach (int i in order)
                    {
                        waiting += time;                      // all arrive at 0, so wait == start
                        time += jobs[i].Duration;
                    }
                    return (double)waiting / n;
                });

            sjfOptimal &= Math.Abs(Schedule(jobs, SchedulingAlgorithm.ShortestJobFirst).AverageWaiting - best) < 1e-9;
        }

        Console.WriteLine($"  SJF == brute-force best average wait (all arriving at t=0):     {sjfOptimal}");
    }

    /// <summary>
    /// A schedule is legal when: segments are chronological and never overlap,
    /// no job runs before it arrives, each job gets exactly its duration, and
    /// every job runs. Checking the OUTPUT rather than the algorithm is what
    /// makes one validator work for all seven policies.
    /// </summary>
    private static bool IsValid(IReadOnlyList<Job> jobs, ScheduleResult schedule)
    {
        int previousEnd = int.MinValue;
        foreach (var segment in schedule.Timeline)
        {
            if (segment.Length <= 0 || segment.Start < previousEnd)
                return false;
            previousEnd = segment.End;
        }

        if (schedule.Jobs.Count != jobs.Count)
            return false;

        foreach (var job in jobs)
        {
            var stats = schedule.Jobs.FirstOrDefault(s => s.JobId == job.Id);
            if (stats is null)
                return false;
            if (stats.FirstStart < job.Arrival)
                return false;
            if (stats.Segments.Sum(s => s.Length) != job.Duration)
                return false;
        }

        return true;
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }

    private static string Indent(string text, int spaces = 2)
    {
        var pad = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => pad + line));
    }
}

// ---- Notes for the follow-up questions ----
//
// "Now there are K machines."
//     The ready-queue loop survives; the clock does not. Replace `time` with a
//     min-heap of the K machines' free times: pop the earliest-free machine, pop
//     the best ready job, assign. Still O(n log n). Note that the makespan
//     invariant DIES here -- with K > 1 the order genuinely changes the finish
//     time, and minimizing it is NP-hard (P||Cmax), so longest-processing-time
//     first is the 4/3-approximation to reach for.
//
// "Jobs have deadlines -- can we meet them all?"
//     Different problem: earliest deadline first is optimal for feasibility on
//     one machine (Jackson's rule), and EDF is exactly this loop with the key
//     swapped to `deadline`. If instead you want to MAXIMIZE the number of jobs
//     that fit, that is the greedy-with-a-max-heap scheduler (LC 630 / 1834).
//
// "Jobs keep arriving forever -- make it an online scheduler."
//     Nothing here needs the full input up front except the arrival-sorted array.
//     Feed arrivals into the same ready queue as they land and the loop is
//     already online; the only change is that the idle branch blocks on new work
//     instead of jumping the clock. Round robin is what an OS actually ships,
//     because FCFS lets one long job destroy interactive latency.
//
// "Which is 'best'?"
//     There is no answer without a metric, and offering one is the trap.
//       FCFS   -- fair by arrival, terrible average wait (convoy effect: one long
//                 job at the front delays everything behind it).
//       SJF    -- provably optimal average wait, but needs durations up front and
//                 starves long jobs.
//       SRTF   -- optimal average turnaround, at the cost of context switches.
//       RR     -- best response time, worst turnaround; the quantum is the dial.
//       MLFQ   -- what real kernels run: RR queues at several priorities, jobs
//                 demoted for using a full quantum, periodically boosted (aging).
//
// "Context switches are not free."
//     Add a fixed cost c to every segment boundary and the preemptive policies
//     stop dominating. This is the honest objection to SRTF and the reason a
//     real quantum is milliseconds, not microseconds.
//
// "n is a million."
//     Every policy here is O(n log n) except the aging variant's rescan. To keep
//     aging cheap, bucket by priority band and rotate bands on a timer -- which
//     is precisely MLFQ, and precisely why MLFQ exists.
