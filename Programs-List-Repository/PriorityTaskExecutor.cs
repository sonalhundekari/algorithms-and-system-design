// Priority Task Executor -- addTask(id, priority, timestamp) / executeTask()
// Difficulty: Medium base, Hard follow-ups
// Pattern: heap + LAZY DELETION (tombstones) -- "delete at pop, not at delete"
//
// The whole problem is one sentence: "the same task may be added many times, but
// once it has executed, every other copy of it must be skipped." That sentence
// is a data-structure question in disguise.
//
// THE TRAP. The instinct on hearing "skip the other copies" is to go and remove
// them when the task executes. You cannot. A binary heap has no way to find, let
// alone delete, an arbitrary element -- only the root is addressable. Scanning
// for the copies is O(n) per execute, and rebuilding the heap is O(n) on top.
// Every candidate who tries to keep the heap clean ends up there.
//
// THE MOVE. Do not delete. Let the dead copies sit in the heap as tombstones and
// throw them away when they surface at the root, which is the one moment they
// ARE addressable and the one moment their liveness actually matters:
//
//     executeTask():  pop until the popped entry is live, or the heap is empty
//
// Cost of a delete drops from O(n) to zero, and the pops are paid for by the
// pushes: every entry is enqueued once and dequeued at most once, so m
// operations cost O(m log m) total -- O(log m) amortized -- no matter how many
// duplicates arrive. That trade (waste memory, buy time) is the answer.
//
//     add A/5  add B/3  add A/8  add C/5        execute -> A     execute -> C
//     ------------------------------------      ------------     ------------
//     heap:  A/8  C/5  A/5  B/3                 A/8 popped, live  C/5 live
//     live:  A B C                              A retired        (A/5 was
//                                                                 discarded on
//     A/5 is not removed here ---^               heap: C/5 A/5 B/3  the way)
//
// THE OTHER TRAP. The skip test belongs at POP time, not at ADD time. When
// addTask runs you do not yet know whether that task will have executed by the
// time this copy reaches the root -- the future has not happened. (Checking at
// add time as well is a legal fast path, but it is an optimization, not the
// mechanism.)
//
// WHAT TO ASK BEFORE WRITING ANYTHING. The prompt under-specifies six things and
// each one changes the output:
//
//   1. Does a HIGHER priority number mean more urgent?   Both conventions ship.
//   2. What IS `timestamp`? Here: when the task was added, used as the
//      tie-break. If it means "do not run before this time" the problem is a
//      DELAYED queue and needs a second heap -- see the notes at the bottom.
//   3. Same task added twice, neither executed yet -- one run or two?
//      "Skip if already executed" says one. Which copy governs? The BEST one:
//      add(A,1) then add(A,9) runs A at 9, because both copies are in the heap
//      and 9 surfaces first. Re-adding is a decrease-key you get for free.
//   4. May a task be added AGAIN after it executed and run a second time?
//      OnceEver vs ReArmAfterExecution -- see ReAddPolicy.
//   5. What does executeTask() return on an empty queue -- null, throw, block?
//   6. Ties inside a priority: earliest timestamp first (FIFO), then insertion
//      order, so the output is deterministic instead of "any valid answer".
//
// THE STATE. Three fields, and the third is the one people miss:
//
//   heap          every occurrence ever added, keyed (priority, timestamp, seq)
//   executed      task ids that have run                    -> the skip rule
//   epoch[id]     how many times the id has been retired    -> the tombstone test
//
// An occurrence carries the epoch it was pushed under. It is live iff that
// stamp still equals the task's current epoch. Executing a task bumps its epoch,
// which invalidates every sibling copy in one O(1) write instead of n deletions.
// Cancel is the same bump. So one predicate covers duplicates, execution and
// cancellation, and the re-add policy touches only addTask.
//
// THREE IMPLEMENTATIONS, one interface, because the follow-up is always memory:
//
//   PriorityTaskExecutor          lazy heap.    O(log m) amortized, O(adds) memory
//   IndexedPriorityTaskExecutor   indexed heap. O(log n) worst,     O(tasks) memory
//   NaiveTaskExecutor             linear scan.  O(n) per execute -- the oracle
//
// The lazy one is the interview answer. The indexed one is the answer to "a task
// is added a million times and never executes -- your heap is a million
// tombstones." The naive one exists so the randomized block at the bottom can
// prove the other two agree with an implementation too dumb to be wrong.

namespace CodingPatterns.StackQueue;

/// <summary>Which way the priority number points. The prompt never says; ask.</summary>
public enum PriorityRank
{
    /// <summary>Priority 9 beats priority 1 -- the usual reading of "high priority".</summary>
    HigherValueWins,

    /// <summary>Priority 1 beats priority 9 -- the usual reading of "priority 1 incident".</summary>
    LowerValueWins,
}

/// <summary>What happens when an already-executed task is added again.</summary>
public enum ReAddPolicy
{
    /// <summary>A task id executes at most once, ever. A later add is dead on arrival.</summary>
    OnceEver,

    /// <summary>Adds made AFTER the execution re-arm the task; adds made before stay dead.</summary>
    ReArmAfterExecution,
}

/// <summary>
/// The surface all three implementations share, so the cross-checks in
/// <see cref="PriorityTaskExecutor.Run"/> can drive them through one loop.
/// </summary>
public interface IPriorityTaskExecutor
{
    void AddTask(string taskId, int priority, long timestamp);

    /// <summary>The next task to run, or null if nothing is executable.</summary>
    string ExecuteTask();

    /// <summary>What <see cref="ExecuteTask"/> would return, without consuming it.</summary>
    string Peek();

    /// <summary>Drops every queued occurrence of a task. False if it had none.</summary>
    bool Cancel(string taskId);

    /// <summary>Distinct tasks still waiting to run. Identical across implementations.</summary>
    int PendingTasks { get; }

    /// <summary>Entries physically held, tombstones included. This is what differs.</summary>
    int QueuedEntries { get; }
}

// ============================================================================
//  1. The lazy heap -- the answer to give
// ============================================================================

/// <summary>
/// Priority-ordered execution with duplicate suppression, via a heap that is
/// never cleaned up and a liveness stamp checked at the root.
///
///   AddTask      O(log m)
///   ExecuteTask  O(log m) amortized -- a single call can pop k tombstones for
///                O(k log m), but those pops were paid for by the adds that
///                created them, so no sequence of m operations exceeds O(m log m)
///   Cancel       O(1)
///   memory       O(total adds), which is the price of the whole trick
/// </summary>
public sealed class PriorityTaskExecutor : IPriorityTaskExecutor
{
    /// <summary>
    /// One add. <see cref="Epoch"/> is the task's retirement count at push time:
    /// the entry is live iff that number is still current. Priority and Timestamp
    /// are carried for reporting only -- ordering lives entirely in the heap key.
    /// </summary>
    private readonly record struct Occurrence(string TaskId, int Priority, long Timestamp, long Epoch);

    private readonly PriorityQueue<Occurrence, (long Rank, long Timestamp, long Sequence)> _heap = new();

    // How many times each id has been retired (executed or cancelled). Absent == 0.
    private readonly Dictionary<string, long> _epoch = new();

    // Live occurrences per id, so PendingTasks and the tombstone count stay O(1).
    private readonly Dictionary<string, int> _liveCount = new();

    private readonly HashSet<string> _executed = new();
    private readonly List<string> _log = new();

    private readonly PriorityRank _rank;
    private readonly ReAddPolicy _reAdd;
    private readonly double _autoCompactRatio;

    private long _sequence;
    private int _pendingTasks;
    private int _liveOccurrences;

    /// <param name="rank">Direction of the priority number.</param>
    /// <param name="reAdd">Whether an executed task can be resurrected.</param>
    /// <param name="autoCompactRatio">
    /// Rebuild the heap once this fraction of it is tombstones; 0 disables it.
    /// Off by default because the plain algorithm should be visible first --
    /// turn it on to answer "your heap grows without bound."
    /// </param>
    public PriorityTaskExecutor(
        PriorityRank rank = PriorityRank.HigherValueWins,
        ReAddPolicy reAdd = ReAddPolicy.OnceEver,
        double autoCompactRatio = 0.0)
    {
        if (autoCompactRatio < 0 || autoCompactRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(autoCompactRatio), "ratio must be in [0, 1]");

        _rank = rank;
        _reAdd = reAdd;
        _autoCompactRatio = autoCompactRatio;
    }

    // ------------------------------------------------------------------- reads

    public int PendingTasks => _pendingTasks;

    public int QueuedEntries => _heap.Count;

    /// <summary>Tombstones currently sitting in the heap. The memory being traded away.</summary>
    public int StaleEntries => _heap.Count - _liveOccurrences;

    /// <summary>Execution order so far -- the transcript the tests compare.</summary>
    public IReadOnlyList<string> ExecutionOrder => _log;

    public bool HasExecuted(string taskId) => _executed.Contains(taskId);

    // ------------------------------------------------------------------ addTask

    /// <summary>
    /// Queues one occurrence. Duplicates are welcome and cheap: they are NOT
    /// reconciled here, because the heap already resolves them -- the best copy
    /// reaches the root first and retiring the task kills the rest.
    ///
    /// Timestamps need not be monotonic; a backdated add simply sorts ahead of
    /// its siblings at the same priority.
    /// </summary>
    public void AddTask(string taskId, int priority, long timestamp)
    {
        Validate(taskId);

        // Consumed even on the rejected path so the tie-break sequence is a
        // property of the CALL, not of whether the call did anything -- that is
        // what lets three different implementations agree exactly on ties.
        long seq = _sequence++;

        // The only place the re-add policy is visible. Everything downstream is
        // the same code for both policies.
        if (_reAdd == ReAddPolicy.OnceEver && _executed.Contains(taskId))
            return;

        _heap.Enqueue(
            new Occurrence(taskId, priority, timestamp, EpochOf(taskId)),
            KeyOf(priority, timestamp, seq));

        _liveOccurrences++;
        if (_liveCount.TryGetValue(taskId, out int live) && live > 0)
            _liveCount[taskId] = live + 1;
        else
        {
            _liveCount[taskId] = 1;
            _pendingTasks++;
        }
    }

    // -------------------------------------------------------------- executeTask

    /// <summary>
    /// Runs the best pending task and returns its id, or null when nothing is
    /// executable. Tombstones encountered on the way are discarded, which is the
    /// entire deletion strategy.
    /// </summary>
    public string ExecuteTask()
    {
        while (_heap.TryDequeue(out var occurrence, out _))
        {
            if (occurrence.Epoch != EpochOf(occurrence.TaskId))
                continue;                                   // superseded: already executed or cancelled

            _executed.Add(occurrence.TaskId);
            Retire(occurrence.TaskId);                      // kills every sibling copy in O(1)
            _log.Add(occurrence.TaskId);
            MaybeCompact();
            return occurrence.TaskId;
        }

        return null;
    }

    public bool TryExecuteTask(out string taskId)
    {
        taskId = ExecuteTask();
        return taskId is not null;
    }

    /// <summary>
    /// The next task without consuming it. Note that this MUTATES the heap: the
    /// tombstones above the first live entry have to come off to see it. Worth
    /// saying out loud, because it means peek is not thread-safe under a reader
    /// lock and not free -- amortized O(log m), same as execute.
    /// </summary>
    public string Peek()
    {
        while (_heap.TryPeek(out var occurrence, out _))
        {
            if (occurrence.Epoch == EpochOf(occurrence.TaskId))
                return occurrence.TaskId;
            _heap.Dequeue();
        }

        return null;
    }

    /// <summary>Executes until the queue is empty and returns the order.</summary>
    public List<string> DrainAll()
    {
        var order = new List<string>();
        while (ExecuteTask() is { } id)
            order.Add(id);
        return order;
    }

    // ------------------------------------------------------- cancel / compaction

    /// <summary>
    /// Cancels a pending task. This is the payoff of the epoch stamp: cancel and
    /// execute are the SAME operation minus the bookkeeping, so supporting it
    /// costs no new machinery. Cancelling does not retire the id -- it can be
    /// added again under either policy.
    /// </summary>
    public bool Cancel(string taskId)
    {
        Validate(taskId);
        if (!_liveCount.TryGetValue(taskId, out int live) || live == 0)
            return false;

        Retire(taskId);
        MaybeCompact();
        return true;
    }

    /// <summary>
    /// Rebuilds the heap without its tombstones and returns how many were
    /// dropped. O(n) -- the scan is linear and re-heapifying is linear -- so
    /// triggering it only once the heap is (say) half garbage keeps the
    /// amortized cost per operation constant.
    /// </summary>
    public int Compact()
    {
        int before = _heap.Count;

        var survivors = _heap.UnorderedItems
            .Where(entry => entry.Element.Epoch == EpochOf(entry.Element.TaskId))
            .ToArray();

        _heap.Clear();
        _heap.EnqueueRange(survivors);
        return before - _heap.Count;
    }

    // ---------------------------------------------------------------- plumbing

    private long EpochOf(string taskId) => _epoch.TryGetValue(taskId, out long e) ? e : 0;

    /// <summary>
    /// Bumps the epoch, which is what actually makes the sibling copies stale,
    /// and settles the counters in one place so the two callers cannot disagree.
    /// </summary>
    private void Retire(string taskId)
    {
        _epoch[taskId] = EpochOf(taskId) + 1;

        if (_liveCount.TryGetValue(taskId, out int live) && live > 0)
        {
            _liveOccurrences -= live;
            _liveCount[taskId] = 0;
            _pendingTasks--;
        }
    }

    /// <summary>
    /// Smaller tuple wins, so the direction of the priority number is folded into
    /// the key once, here, and nothing else in the class ever thinks about it.
    /// Widening to long first keeps int.MinValue from surviving its own negation.
    /// </summary>
    private (long, long, long) KeyOf(int priority, long timestamp, long sequence)
        => (_rank == PriorityRank.HigherValueWins ? -(long)priority : priority, timestamp, sequence);

    private void MaybeCompact()
    {
        const int floor = 16;                               // below this the rebuild costs more than it saves
        if (_autoCompactRatio <= 0 || _heap.Count < floor)
            return;

        if (StaleEntries >= _autoCompactRatio * _heap.Count)
            Compact();
    }

    private static void Validate(string taskId)
    {
        if (taskId is null)
            throw new ArgumentNullException(nameof(taskId));
        if (taskId.Length == 0)
            throw new ArgumentException("task id must not be empty", nameof(taskId));
    }

    // ============================================================== demo / tests

    public static void Main() => PriorityTaskExecutorTests.Run();
}

// ============================================================================
//  2. The indexed heap -- the answer to "your heap grows without bound"
// ============================================================================

/// <summary>
/// Same semantics, one entry per task instead of one per add. Re-adding a task
/// is an explicit decrease-key: if the new occurrence is better it overwrites
/// the stored key and sifts up, and if it is worse it is dropped on the spot.
///
///   AddTask      O(log n) WORST case, n = distinct pending tasks
///   ExecuteTask  O(log n) worst case -- no tombstone run to walk
///   Cancel       O(log n)
///   memory       O(distinct pending tasks)
///
/// Strictly better bounds, and still the wrong first answer in an interview: it
/// needs a hand-rolled heap with a position map, because neither .NET's
/// PriorityQueue nor Python's heapq will let you move an interior element. That
/// is roughly triple the code for a constant-factor and a memory win, so reach
/// for it when the tombstone growth is shown to be real -- unbounded queues,
/// long-lived processes, or hot tasks re-added far more often than they run.
/// </summary>
public sealed class IndexedPriorityTaskExecutor : IPriorityTaskExecutor
{
    private readonly record struct Node(string TaskId, (long Rank, long Timestamp, long Sequence) Key);

    private readonly List<Node> _nodes = new();
    private readonly Dictionary<string, int> _position = new();     // task id -> index in _nodes
    private readonly HashSet<string> _executed = new();
    private readonly List<string> _log = new();

    private readonly PriorityRank _rank;
    private readonly ReAddPolicy _reAdd;
    private long _sequence;

    public IndexedPriorityTaskExecutor(
        PriorityRank rank = PriorityRank.HigherValueWins,
        ReAddPolicy reAdd = ReAddPolicy.OnceEver)
    {
        _rank = rank;
        _reAdd = reAdd;
    }

    public int PendingTasks => _nodes.Count;

    public int QueuedEntries => _nodes.Count;                       // no tombstones: the two are equal

    public IReadOnlyList<string> ExecutionOrder => _log;

    public void AddTask(string taskId, int priority, long timestamp)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentException("task id must not be null or empty", nameof(taskId));

        long seq = _sequence++;
        if (_reAdd == ReAddPolicy.OnceEver && _executed.Contains(taskId))
            return;

        var key = _rank == PriorityRank.HigherValueWins
            ? (-(long)priority, timestamp, seq)
            : ((long)priority, timestamp, seq);

        if (_position.TryGetValue(taskId, out int i))
        {
            // Already queued. The lazy version would keep both copies and let the
            // better one surface; here that resolution happens now, and the
            // observable result is identical.
            if (Compare(key, _nodes[i].Key) >= 0)
                return;

            _nodes[i] = _nodes[i] with { Key = key };
            SiftUp(i);                                              // a better key can only move up
            return;
        }

        _nodes.Add(new Node(taskId, key));
        _position[taskId] = _nodes.Count - 1;
        SiftUp(_nodes.Count - 1);
    }

    public string ExecuteTask()
    {
        if (_nodes.Count == 0)
            return null;

        string taskId = _nodes[0].TaskId;
        RemoveAt(0);
        _executed.Add(taskId);
        _log.Add(taskId);
        return taskId;
    }

    public string Peek() => _nodes.Count == 0 ? null : _nodes[0].TaskId;

    public bool Cancel(string taskId)
    {
        if (!_position.TryGetValue(taskId, out int i))
            return false;

        RemoveAt(i);
        return true;
    }

    // ------------------------------------------------------------- heap guts

    private static int Compare((long, long, long) a, (long, long, long) b)
        => Comparer<(long, long, long)>.Default.Compare(a, b);

    /// <summary>
    /// Removing an INTERIOR node is the one operation a plain heap cannot do, and
    /// the reason this class exists. The replacement pulled up from the tail can
    /// violate the heap in either direction, so both sifts run; at most one of
    /// them moves anything.
    /// </summary>
    private void RemoveAt(int i)
    {
        int last = _nodes.Count - 1;
        _position.Remove(_nodes[i].TaskId);

        if (i == last)
        {
            _nodes.RemoveAt(last);
            return;
        }

        _nodes[i] = _nodes[last];
        _position[_nodes[i].TaskId] = i;
        _nodes.RemoveAt(last);

        SiftDown(i);
        SiftUp(i);
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (Compare(_nodes[i].Key, _nodes[parent].Key) >= 0)
                break;
            Swap(i, parent);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        int n = _nodes.Count;
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, best = i;
            if (left < n && Compare(_nodes[left].Key, _nodes[best].Key) < 0)
                best = left;
            if (right < n && Compare(_nodes[right].Key, _nodes[best].Key) < 0)
                best = right;
            if (best == i)
                return;
            Swap(i, best);
            i = best;
        }
    }

    private void Swap(int a, int b)
    {
        (_nodes[a], _nodes[b]) = (_nodes[b], _nodes[a]);
        _position[_nodes[a].TaskId] = a;
        _position[_nodes[b].TaskId] = b;
    }
}

// ============================================================================
//  3. The oracle -- too slow to ship, too simple to be wrong
// ============================================================================

/// <summary>
/// Keeps every live occurrence in a list and scans it. O(n) per execute, which
/// is exactly the thing the heap exists to avoid -- but it encodes the SPEC
/// ("run the smallest key among live occurrences, then kill that id's copies")
/// with no cleverness in the way, so the randomized block can hold the other two
/// implementations against it.
/// </summary>
public sealed class NaiveTaskExecutor : IPriorityTaskExecutor
{
    private readonly List<(string TaskId, (long, long, long) Key)> _live = new();
    private readonly HashSet<string> _executed = new();
    private readonly List<string> _log = new();

    private readonly PriorityRank _rank;
    private readonly ReAddPolicy _reAdd;
    private long _sequence;

    public NaiveTaskExecutor(
        PriorityRank rank = PriorityRank.HigherValueWins,
        ReAddPolicy reAdd = ReAddPolicy.OnceEver)
    {
        _rank = rank;
        _reAdd = reAdd;
    }

    public int PendingTasks => _live.Select(e => e.TaskId).Distinct().Count();

    public int QueuedEntries => _live.Count;

    public IReadOnlyList<string> ExecutionOrder => _log;

    public void AddTask(string taskId, int priority, long timestamp)
    {
        long seq = _sequence++;
        if (_reAdd == ReAddPolicy.OnceEver && _executed.Contains(taskId))
            return;

        _live.Add((taskId, _rank == PriorityRank.HigherValueWins
            ? (-(long)priority, timestamp, seq)
            : ((long)priority, timestamp, seq)));
    }

    public string ExecuteTask()
    {
        if (_live.Count == 0)
            return null;

        int best = 0;
        for (int i = 1; i < _live.Count; i++)
            if (Comparer<(long, long, long)>.Default.Compare(_live[i].Key, _live[best].Key) < 0)
                best = i;

        string taskId = _live[best].TaskId;
        _live.RemoveAll(e => e.TaskId == taskId);           // the skip rule, written out longhand
        _executed.Add(taskId);
        _log.Add(taskId);
        return taskId;
    }

    public string Peek()
    {
        if (_live.Count == 0)
            return null;

        int best = 0;
        for (int i = 1; i < _live.Count; i++)
            if (Comparer<(long, long, long)>.Default.Compare(_live[i].Key, _live[best].Key) < 0)
                best = i;

        return _live[best].TaskId;
    }

    public bool Cancel(string taskId) => _live.RemoveAll(e => e.TaskId == taskId) > 0;
}

// ============================================================================
//  Demo + tests
// ============================================================================

internal static class PriorityTaskExecutorTests
{
    public static void Run()
    {
        WorkedExample();
        TieBreaking();
        TheSkipRule();
        RankDirection();
        ReAdding();
        Cancellation();
        Compaction();
        EdgeCases();
        Randomized();
    }

    private static void WorkedExample()
    {
        Console.WriteLine("== the worked example ==");

        var q = new PriorityTaskExecutor();
        q.AddTask("t1", 5, 1);
        q.AddTask("t2", 3, 2);
        q.AddTask("t1", 8, 3);          // same task again, better priority
        q.AddTask("t3", 5, 4);

        Console.WriteLine("  add t1/5@1, t2/3@2, t1/8@3, t3/5@4");
        Console.WriteLine($"  queued entries {q.QueuedEntries}, distinct pending {q.PendingTasks}");
        Console.WriteLine($"  peek -> {q.Peek()}");

        var order = new List<string> { q.ExecuteTask(), q.ExecuteTask(), q.ExecuteTask() };
        Console.WriteLine($"  execute x3 -> {string.Join(", ", order)}");
        Console.WriteLine($"  execute on empty -> {Show(q.ExecuteTask())}");
        Console.WriteLine($"  matches expected [t1, t3, t2]: {order.SequenceEqual(new[] { "t1", "t3", "t2" })}");
        Console.WriteLine("  t1 runs FIRST at priority 8 -- the second add is a free decrease-key --");
        Console.WriteLine("  and its priority-5 copy never runs, because t1 is already executed.");
        Console.WriteLine("  t3 beats t2 on priority; t2 is last despite being added second.");
    }

    private static void TieBreaking()
    {
        Console.WriteLine();
        Console.WriteLine("== ties: priority, then timestamp, then insertion order ==");

        var q = new PriorityTaskExecutor();
        q.AddTask("late", 5, 30);
        q.AddTask("early", 5, 10);
        q.AddTask("middle", 5, 20);
        Console.WriteLine($"  three tasks at priority 5, timestamps 30/10/20 -> {string.Join(", ", q.DrainAll())}");

        var same = new PriorityTaskExecutor();
        same.AddTask("a", 1, 7);
        same.AddTask("b", 1, 7);
        same.AddTask("c", 1, 7);
        Console.WriteLine($"  identical priority AND timestamp      -> {string.Join(", ", same.DrainAll())}");
        Console.WriteLine("  ^ falls back to insertion order, so the answer is stable run to run.");

        var backdated = new PriorityTaskExecutor();
        backdated.AddTask("first", 5, 100);
        backdated.AddTask("backdated", 5, 1);        // timestamps need not arrive in order
        Console.WriteLine($"  a backdated add jumps the queue        -> {string.Join(", ", backdated.DrainAll())}");
    }

    private static void TheSkipRule()
    {
        Console.WriteLine();
        Console.WriteLine("== the skip rule, and the tombstones it leaves behind ==");

        var q = new PriorityTaskExecutor();
        for (int i = 1; i <= 5; i++)
            q.AddTask("hot", i, i);      // one task, five occurrences
        q.AddTask("cold", 0, 9);

        Console.WriteLine($"  added hot 5x and cold 1x: entries {q.QueuedEntries}, pending {q.PendingTasks}, " +
                          $"stale {q.StaleEntries}");
        Console.WriteLine($"  execute -> {q.ExecuteTask()}  (priority 5, the best of the five copies)");
        Console.WriteLine($"  now:  entries {q.QueuedEntries}, pending {q.PendingTasks}, stale {q.StaleEntries}");
        Console.WriteLine("  four dead copies are STILL IN THE HEAP -- executing did not remove them.");
        Console.WriteLine($"  execute -> {q.ExecuteTask()}  (pops and discards all four on the way to cold)");
        Console.WriteLine($"  execute -> {Show(q.ExecuteTask())}");
        Console.WriteLine($"  hot executed exactly once: {q.ExecutionOrder.Count(id => id == "hot") == 1}");
    }

    private static void RankDirection()
    {
        Console.WriteLine();
        Console.WriteLine("== which end of the priority scale wins ==");

        foreach (var rank in Enum.GetValues<PriorityRank>())
        {
            var q = new PriorityTaskExecutor(rank);
            q.AddTask("p1", 1, 0);
            q.AddTask("p5", 5, 0);
            q.AddTask("p3", 3, 0);
            Console.WriteLine($"  {rank,-16} -> {string.Join(", ", q.DrainAll())}");
        }

        Console.WriteLine("  Same input, same class, reversed answer. This is a question, not a guess.");
    }

    private static void ReAdding()
    {
        Console.WriteLine();
        Console.WriteLine("== adding a task again after it has already executed ==");

        foreach (var policy in Enum.GetValues<ReAddPolicy>())
        {
            var q = new PriorityTaskExecutor(reAdd: policy);
            q.AddTask("job", 1, 0);
            string first = q.ExecuteTask();
            q.AddTask("job", 9, 1);              // arrives strictly after the execution
            string second = q.ExecuteTask();

            Console.WriteLine($"  {policy,-22} execute -> {first}, re-add, execute -> {Show(second)}");
        }

        // The half-and-half case: some copies pre-date the execution, some follow
        // it. Only the ones added afterwards may come back.
        var mixed = new PriorityTaskExecutor(reAdd: ReAddPolicy.ReArmAfterExecution);
        mixed.AddTask("job", 1, 0);
        mixed.AddTask("job", 2, 1);              // both of these are dead once job runs
        mixed.ExecuteTask();
        mixed.AddTask("job", 3, 2);
        Console.WriteLine($"  ReArm with 2 stale copies queued: execute -> {Show(mixed.ExecuteTask())}, " +
                          $"then {Show(mixed.ExecuteTask())}");
        Console.WriteLine("  ^ exactly one comeback: the two pre-execution copies stayed dead, which is");
        Console.WriteLine("    the epoch stamp doing its job -- 'already executed' is per-copy, not per-id.");
    }

    private static void Cancellation()
    {
        Console.WriteLine();
        Console.WriteLine("== cancel: the same epoch bump, minus the execution ==");

        var q = new PriorityTaskExecutor();
        q.AddTask("keep", 5, 0);
        q.AddTask("drop", 9, 0);
        q.AddTask("drop", 8, 1);

        Console.WriteLine($"  cancel drop (2 copies) -> {q.Cancel("drop")}, pending now {q.PendingTasks}");
        Console.WriteLine($"  cancel drop again      -> {q.Cancel("drop")} (nothing left to cancel)");
        Console.WriteLine($"  cancel unknown         -> {q.Cancel("never-added")}");
        Console.WriteLine($"  execute -> {q.ExecuteTask()}, then {Show(q.ExecuteTask())}");

        var reused = new PriorityTaskExecutor();
        reused.AddTask("task", 1, 0);
        reused.Cancel("task");
        reused.AddTask("task", 1, 1);
        Console.WriteLine($"  cancelled != executed, so it can be re-added -> {Show(reused.ExecuteTask())}");
    }

    private static void Compaction()
    {
        Console.WriteLine();
        Console.WriteLine("== the memory objection, and two answers to it ==");

        // One task hammered 200 times, plus one ordinary task left pending so the
        // queue is not trivially empty afterwards.
        static void Load(IPriorityTaskExecutor q)
        {
            for (int i = 0; i < 200; i++)
                q.AddTask("spam", i, i);
            q.AddTask("quiet", 0, 999);
            q.ExecuteTask();                     // runs spam at priority 199
        }

        var lazy = new PriorityTaskExecutor();
        Load(lazy);
        Console.WriteLine($"  200 adds of one task + 1 other, 1 execute -> entries {lazy.QueuedEntries}, " +
                          $"pending {lazy.PendingTasks}, stale {lazy.StaleEntries}");
        Console.WriteLine("  Nothing will ever pop those 199 corpses: the one live entry below them is");
        Console.WriteLine("  all that is left to pop for, so 199 wasted slots outlive every future call.");
        Console.WriteLine($"  Compact() drops {lazy.Compact()} of them; entries now {lazy.QueuedEntries}.");

        var auto = new PriorityTaskExecutor(autoCompactRatio: 0.5);
        var indexed = new IndexedPriorityTaskExecutor();
        Load(auto);
        Load(indexed);
        Console.WriteLine($"  auto-compacting at 50% stale:                   entries {auto.QueuedEntries}");
        Console.WriteLine($"  indexed heap (1 entry per task, never garbage): entries {indexed.QueuedEntries}");
        Console.WriteLine($"  all three still agree on what runs next: " +
                          $"{lazy.Peek()} / {auto.Peek()} / {indexed.Peek()}");
    }

    private static void EdgeCases()
    {
        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var empty = new PriorityTaskExecutor();
        Console.WriteLine($"  execute on a fresh queue -> {Show(empty.ExecuteTask())}, peek -> {Show(empty.Peek())}");
        Console.WriteLine($"  TryExecuteTask -> {empty.TryExecuteTask(out _)}");

        var dup = new PriorityTaskExecutor();
        dup.AddTask("x", 4, 7);
        dup.AddTask("x", 4, 7);                  // byte-identical add
        Console.WriteLine($"  the exact same add twice -> {string.Join(", ", dup.DrainAll())} (runs once)");

        var worse = new PriorityTaskExecutor();
        worse.AddTask("x", 9, 0);
        worse.AddTask("x", 1, 0);                // a WORSE re-add must not demote it
        worse.AddTask("y", 5, 0);
        Console.WriteLine($"  re-adding at a worse priority cannot demote a task -> " +
                          $"{string.Join(", ", worse.DrainAll())} (expect x, y)");

        var negative = new PriorityTaskExecutor();
        negative.AddTask("neg", int.MinValue, 0);
        negative.AddTask("pos", int.MaxValue, 0);
        Console.WriteLine($"  extreme priorities -> {string.Join(", ", negative.DrainAll())} " +
                          "(int.MinValue negates safely because the key is long)");

        var peekMatches = new PriorityTaskExecutor();
        peekMatches.AddTask("a", 1, 0);
        peekMatches.AddTask("b", 2, 0);
        Console.WriteLine($"  peek agrees with the next execute: {peekMatches.Peek() == peekMatches.ExecuteTask()}");

        foreach (var (label, bad) in new (string, Action)[]
                 {
                     ("null id", () => new PriorityTaskExecutor().AddTask(null, 1, 0)),
                     ("empty id", () => new PriorityTaskExecutor().AddTask("", 1, 0)),
                     ("compact ratio 2.0", () => _ = new PriorityTaskExecutor(autoCompactRatio: 2.0)),
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
    }

    private static void Randomized()
    {
        Console.WriteLine();
        Console.WriteLine("== randomized cross-checks ==");

        var rng = new Random(20250810);
        var ids = new[] { "t0", "t1", "t2", "t3", "t4", "t5" };

        bool lazyMatchesNaive = true, indexedMatchesNaive = true, compactingMatches = true;
        bool pendingAgrees = true, peekAgrees = true, onceEverHolds = true, staleAccountingHolds = true;

        for (int trial = 0; trial < 4000; trial++)
        {
            var rank = rng.Next(2) == 0 ? PriorityRank.HigherValueWins : PriorityRank.LowerValueWins;
            var policy = rng.Next(2) == 0 ? ReAddPolicy.OnceEver : ReAddPolicy.ReArmAfterExecution;

            var lazy = new PriorityTaskExecutor(rank, policy);
            var compacting = new PriorityTaskExecutor(rank, policy, autoCompactRatio: 0.5);
            var indexed = new IndexedPriorityTaskExecutor(rank, policy);
            var naive = new NaiveTaskExecutor(rank, policy);

            var lazyLog = new List<string>();
            var compactingLog = new List<string>();
            var indexedLog = new List<string>();
            var naiveLog = new List<string>();

            int operations = rng.Next(0, 60);
            for (int op = 0; op < operations; op++)
            {
                string id = ids[rng.Next(ids.Length)];
                int roll = rng.Next(100);

                if (roll < 60)
                {
                    // Tiny priority and timestamp ranges on purpose: ties are
                    // where tie-break bugs live, so make them the common case.
                    int priority = rng.Next(0, 5);
                    long timestamp = rng.Next(0, 6);
                    lazy.AddTask(id, priority, timestamp);
                    compacting.AddTask(id, priority, timestamp);
                    indexed.AddTask(id, priority, timestamp);
                    naive.AddTask(id, priority, timestamp);
                }
                else if (roll < 90)
                {
                    // peek() must name whatever execute() is about to return --
                    // easy to get wrong when peek has to prune tombstones first.
                    peekAgrees &= lazy.Peek() == naive.Peek() && indexed.Peek() == naive.Peek();

                    string a = lazy.ExecuteTask();
                    string b = compacting.ExecuteTask();
                    string c = indexed.ExecuteTask();
                    string d = naive.ExecuteTask();

                    if (a is not null) lazyLog.Add(a);
                    if (b is not null) compactingLog.Add(b);
                    if (c is not null) indexedLog.Add(c);
                    if (d is not null) naiveLog.Add(d);

                    lazyMatchesNaive &= a == d;
                    compactingMatches &= b == d;
                    indexedMatchesNaive &= c == d;
                }
                else
                {
                    bool a = lazy.Cancel(id);
                    bool b = compacting.Cancel(id);
                    bool c = indexed.Cancel(id);
                    bool d = naive.Cancel(id);
                    lazyMatchesNaive &= a == d && b == d && c == d;
                }

                pendingAgrees &= lazy.PendingTasks == naive.PendingTasks
                              && indexed.PendingTasks == naive.PendingTasks
                              && compacting.PendingTasks == naive.PendingTasks;

                // Live occurrences are never negative and never exceed the heap:
                // the counter that makes StaleEntries and PendingTasks O(1) is
                // maintained in four places, so assert it rather than trust it.
                staleAccountingHolds &= lazy.StaleEntries >= 0 && lazy.StaleEntries <= lazy.QueuedEntries;
            }

            // Drain, then check the headline guarantee on the full transcript.
            lazyLog.AddRange(lazy.DrainAll());
            compactingLog.AddRange(compacting.DrainAll());
            while (indexed.ExecuteTask() is { } id) indexedLog.Add(id);
            while (naive.ExecuteTask() is { } id) naiveLog.Add(id);

            lazyMatchesNaive &= lazyLog.SequenceEqual(naiveLog);
            compactingMatches &= compactingLog.SequenceEqual(naiveLog);
            indexedMatchesNaive &= indexedLog.SequenceEqual(naiveLog);

            if (policy == ReAddPolicy.OnceEver)
                onceEverHolds &= lazyLog.Distinct().Count() == lazyLog.Count;
        }

        Console.WriteLine($"  4,000 random op streams, lazy heap == naive oracle:        {lazyMatchesNaive}");
        Console.WriteLine($"  indexed heap == naive oracle:                              {indexedMatchesNaive}");
        Console.WriteLine($"  auto-compaction changes nothing observable:                {compactingMatches}");
        Console.WriteLine($"  all four report the same distinct pending count:           {pendingAgrees}");
        Console.WriteLine($"  peek() names exactly what the next execute() returns:      {peekAgrees}");
        Console.WriteLine($"  OnceEver: no task id ever appears twice in the transcript: {onceEverHolds}");
        Console.WriteLine($"  tombstone accounting stays in [0, heap size]:              {staleAccountingHolds}");

        // The amortized claim, measured, on deliberately lopsided traffic: 20 adds
        // per execute, drawn from a pool small enough that most adds are
        // duplicates destined to become tombstones.
        var stress = new PriorityTaskExecutor(reAdd: ReAddPolicy.ReArmAfterExecution);
        var pool = Enumerable.Range(0, 400).Select(i => $"task{i}").ToArray();
        int adds = 0, executed = 0;

        for (int round = 0; round < 500; round++)
        {
            for (int i = 0; i < 20; i++, adds++)
                stress.AddTask(pool[rng.Next(pool.Length)], rng.Next(50), rng.Next(1000));
            if (stress.ExecuteTask() is not null)
                executed++;
        }

        int entries = stress.QueuedEntries, live = stress.PendingTasks, stale = stress.StaleEntries;
        int reclaimed = stress.Compact();
        int afterCompact = stress.QueuedEntries;
        int drained = stress.DrainAll().Count;

        Console.WriteLine($"  {adds} adds against {executed} executes, {pool.Length} distinct tasks:");
        Console.WriteLine($"    heap holds {entries} entries to represent {live} pending tasks ({stale} stale)");
        Console.WriteLine($"    Compact() reclaims {reclaimed} of them, leaving {afterCompact}");
        Console.WriteLine($"    the final drain runs {drained} more and empties it: " +
                          $"{stress.QueuedEntries == 0 && stress.PendingTasks == 0}");
        Console.WriteLine($"    total pops <= total pushes: {executed + drained <= adds}");
        Console.WriteLine("  ^ every entry is pushed once and popped at most once, so the tombstone runs");
        Console.WriteLine("    are bounded by the adds that made them -- O(log m) amortized, however many");
        Console.WriteLine($"    duplicates arrive. The {entries} vs {live} gap is the memory being traded away,");
        Console.WriteLine($"    and note Compact() only clears TOMBSTONES -- the {afterCompact} survivors are live");
        Console.WriteLine($"    duplicates of the same {live} tasks. Getting to {live} entries needs the indexed heap.");
    }

    private static string Show(string value) => value ?? "null";
}

// ---- Notes for the follow-up questions ----
//
// "What if `timestamp` means 'do not execute before this time'?"
//     Different problem -- a DELAYED priority queue -- and worth catching before
//     you write anything. Two heaps: a pending heap keyed by ready-time, and the
//     ready heap keyed by priority as here. executeTask(now) first drains every
//     pending entry with readyTime <= now into the ready heap, then pops. The
//     skip rule and the epoch stamp are untouched; only admission changes. A real
//     scheduler blocks on a timer set to the pending heap's root instead of
//     polling, which is exactly how a timer wheel or DelayQueue works.
//
// "Make executeTask() block until something is available."
//     Do not hand-roll it. Wrap the heap in a lock and a condition variable
//     (SemaphoreSlim / Monitor.Wait), signal on every add, and re-check the
//     predicate in a loop -- spurious wakeups are real, and the tombstone skip
//     means "the heap is non-empty" is NOT the same predicate as "something is
//     executable". That gap is the bug to expect: signalling on add can wake a
//     consumer that then finds only corpses and must go back to sleep.
//
// "Two threads call executeTask() at once."
//     The window is between the pop and the epoch bump: both threads can pop live
//     copies of the same task and both will run it. A single lock over
//     pop-check-retire closes it. Lock-free versions need the retire to be a CAS
//     on the epoch, with the loser discarding its entry and retrying -- which is
//     just lazy deletion again, one level down. Sharding by priority band scales
//     better than a finer-grained heap lock, because the root is the contention
//     point by construction.
//
// "Change a task's priority."
//     Re-adding it only ever IMPROVES the effective priority (the better copy
//     wins), so re-add is a decrease-key, not a set-priority. To genuinely lower
//     one: Cancel then AddTask, which the epoch stamp already makes O(1) + O(log m).
//     Say this explicitly -- "re-add is not the same as set" is the subtle part.
//
// "Millions of tasks, priorities only 0..9."
//     Drop the comparison heap entirely: one FIFO deque per priority plus a
//     bitmask of non-empty levels makes every operation O(1), which is how OS
//     run queues and most job servers are actually built. The tombstone trick
//     still applies inside each level.
//
// "Nothing may be lost across a restart."
//     The heap stops being the source of truth. Append adds and executions to a
//     durable log (or a table with a status column) and treat the in-memory heap
//     as a cache rebuilt on startup; the executed set becomes the dedup key for
//     at-least-once delivery. Distributed, this is a visibility-timeout queue --
//     execute leases the task instead of retiring it, and a crashed worker's
//     lease expires and the task returns to the heap.
//
// "Low-priority tasks never run."
//     Same starvation story as any priority scheduler, same fix: aging. The key
//     then depends on wall-clock time and no longer sits still in a heap, so
//     either re-add periodically with a boosted priority (cheap, works with this
//     exact code) or bucket into priority bands and rotate them on a timer, which
//     is MLFQ. See JobScheduler.cs in 05_greedy for the measured version.
