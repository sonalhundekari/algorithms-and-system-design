/*
Filter System with a Dynamic Blacklist (design classic)

A stream of integers arrives one at a time. A blacklist ("the filter") is
maintained alongside it and can change at any moment. Three methods:

    ProcessFilter(value)   add or remove `value` from the filter
    ProcessInput(value)    one value arrives on the input stream
    Emit(value)            the system's output hook -- you call it, not the caller

BASE RULE. When a value arrives via ProcessInput and is not currently in the
filter, call Emit(value). That is the whole base problem, and it is one HashSet:

    void ProcessInput(int v) { if (!_filter.Contains(v)) Emit(v); }

FOLLOW-UP. Add an updateFlag and re-emit on filter edits:

    ProcessFilter(bool updateFlag, int value)
    Emit(bool updateFlag, int value)

    Emit(false, v)  an already-seen value has become newly BLOCKED
    Emit(true,  v)  a blocked-but-already-seen value has become VISIBLE again

The published trace, in time order, is the entire specification:

    ProcessFilter   +1   +2        -1   +3
    ProcessInput            1   3
    Emit                              +3   +1   -3

Decoded event by event -- do this out loud in the interview, because the trace is
terse enough that two people can read it two different ways:

    ProcessFilter(+1)   filter = {1}          nothing seen yet, no emit
    ProcessFilter(+2)   filter = {1, 2}       nothing seen yet, no emit
    ProcessInput(1)     seen = {1}            1 IS filtered -> swallowed, no emit
    ProcessInput(3)     seen = {1, 3}         3 is not filtered -> Emit(true, 3)
    ProcessFilter(-1)   filter = {2}          1 was seen and is now visible -> Emit(true, 1)
    ProcessFilter(+3)   filter = {2, 3}       3 was seen and is now blocked -> Emit(false, 3)

    emits: +3, +1, -3    -- exactly the trace

THE TRAP: THE FLAG MEANS OPPOSITE THINGS ON THE WAY IN AND ON THE WAY OUT.

    ProcessFilter(true, v)   ADD v to the filter    -> v becomes INVISIBLE
    Emit(true, v)            v is VISIBLE

so `ProcessFilter(true, 3)` produces `Emit(false, 3)`. That inversion is forced
by the trace (see ProcessFilter(+3) -> -3 above), it is not a choice, and writing
`Emit(updateFlag, value)` is the single most likely bug in this problem. Confirm
the polarity with the interviewer before writing a line.

THE SECOND THING THE TRACE PINS DOWN. Input 1 arrived while 1 was filtered, so it
was never emitted -- and un-filtering it still emits it. So "seen" means EVER
ARRIVED ON THE INPUT, not "was previously emitted". A value that was swallowed is
still remembered. Get this wrong and the +1 in the trace disappears.

THE MODEL. Give every value two independent bits and one derived predicate:

    seen(v)     v has appeared on the input at least once     (never goes back to false)
    blocked(v)  v is in the filter right now                  (toggles freely)

    visible(v) = seen(v) AND NOT blocked(v)

`visible` is precisely what the emit stream reports: the last emit for a value is
its current visibility. Both mutating methods then have the same three-line body
-- read visible, flip one bit, emit iff visible flipped -- and there are no
special cases to enumerate, because every case in the problem statement is just
one of the four edges of that predicate:

    seen: false -> true   while unblocked   -> Emit(true, v)    first sighting
    blocked: false -> true  while seen      -> Emit(false, v)   newly blocked
    blocked: true -> false  while seen      -> Emit(true, v)    visible again
    anything, while !seen(v)                -> silence          nobody was ever told

WHAT TO ASK BEFORE WRITING CODE
  1. Does the base ProcessFilter(int) TOGGLE, or is it add-only? A single-argument
     mutator that must do both can only be a toggle -- and the follow-up bolting a
     flag onto it is strong evidence the base really is a toggle. Implemented as a
     toggle here; ProcessFilter(bool, int) is exposed too so callers who want the
     explicit form never have to track parity.
  2. Repeated input of a value that is currently visible: emit again, or stay
     quiet? The base rule says "emit when it arrives and is not filtered", which
     is per-arrival. The follow-up is phrased in terms of state CHANGES, which is
     per-transition. Both are defensible and the trace never repeats a value, so
     it cannot settle it. InputEmission below implements both; per-arrival is the
     default because it is what the base rule literally says.
  3. Is a redundant filter edit -- adding what is already filtered -- an event?
     No. No state change, no emit. Falls out of the model for free.
  4. Can Emit re-enter the system? State is mutated BEFORE Emit is called, so a
     callback that turns around and calls ProcessInput sees a consistent object.

COMPLEXITY
  Time:  O(1) expected per call, all three methods.
  Space: O(distinct values filtered + distinct values seen). The seen set is the
         follow-up's price and it only grows -- see the notes at the bottom for
         what to do when the stream is unbounded.

Related: LRU Cache (same "design a small object, the hard part is the invariant"
shape), Two Sum (the base version is a HashSet lookup and nothing else).
*/

namespace CodingPatterns.ArraysStrings;

// =============================================================================
// Part 1 -- the base problem
// =============================================================================

/// <summary>
/// Stream filter, base version: emit a value when it arrives and is not
/// currently blacklisted. One set, no history.
/// </summary>
public partial class FilterSystem
{
    private readonly HashSet<int> _filter = new();
    private readonly List<int> _output = new();
    private readonly Action<int> _sink;

    /// <param name="sink">Optional external output hook. Emissions are recorded either way.</param>
    public FilterSystem(Action<int> sink = null) => _sink = sink;

    /// <summary>Everything Emit has been called with, in order.</summary>
    public IReadOnlyList<int> Output => _output;

    /// <summary>The blacklist as it stands right now.</summary>
    public IReadOnlyCollection<int> Filter => _filter;

    /// <summary>Toggles <paramref name="value"/>'s membership in the filter.</summary>
    public void ProcessFilter(int value)
    {
        // Add returns false when it was already there, which is exactly the
        // "then remove it" case -- one lookup, not two.
        if (!_filter.Add(value))
            _filter.Remove(value);
    }

    /// <summary>Explicit form: true adds to the filter (blocks), false removes (unblocks).</summary>
    public void ProcessFilter(bool updateFlag, int value)
    {
        if (updateFlag)
            _filter.Add(value);
        else
            _filter.Remove(value);
    }

    /// <summary>One value from the input stream.</summary>
    public void ProcessInput(int value)
    {
        if (!_filter.Contains(value))
            Emit(value);
    }

    /// <summary>The system's output hook. Override to send emissions somewhere real.</summary>
    protected virtual void Emit(int value)
    {
        _output.Add(value);
        _sink?.Invoke(value);
    }
}

// =============================================================================
// Part 2 -- the follow-up
// =============================================================================

/// <summary>What ProcessInput does when the value is already visible.</summary>
public enum InputEmission
{
    /// <summary>
    /// Emit(true, v) on every unfiltered arrival, repeats included. This is the
    /// base rule carried forward verbatim: the emit stream is the input stream
    /// minus the filtered values, plus the filter-edit transitions.
    /// </summary>
    EveryArrival,

    /// <summary>
    /// Emit only when visibility actually flips, so a repeated arrival of an
    /// already-visible value is silent. The emit stream becomes a pure
    /// state-change log, with no two consecutive emits per value agreeing.
    /// </summary>
    OnStateChange,
}

/// <summary>
/// Stream filter, follow-up version: filter edits replay their effect on values
/// that have already appeared, so the emit stream always describes the current
/// visibility of every value the system knows about.
/// </summary>
public class FilterSystemWithUpdates
{
    private readonly HashSet<int> _filter = new();   // blocked right now
    private readonly HashSet<int> _seen = new();     // ever arrived on the input
    private readonly List<(bool Visible, int Value)> _output = new();
    private readonly Action<bool, int> _sink;
    private readonly InputEmission _policy;

    public FilterSystemWithUpdates(
        InputEmission policy = InputEmission.EveryArrival,
        Action<bool, int> sink = null)
    {
        _policy = policy;
        _sink = sink;
    }

    /// <summary>Every (updateFlag, value) pair Emit has been called with, in order.</summary>
    public IReadOnlyList<(bool Visible, int Value)> Output => _output;

    public IReadOnlyCollection<int> Filter => _filter;

    public IReadOnlyCollection<int> Seen => _seen;

    /// <summary>
    /// visible(v) = seen(v) AND NOT blocked(v). The predicate the emit stream
    /// reports, and the only thing either mutator has to watch.
    /// </summary>
    public bool IsVisible(int value) => _seen.Contains(value) && !_filter.Contains(value);

    /// <summary>
    /// Adds (<paramref name="updateFlag"/> true) or removes (false)
    /// <paramref name="value"/> from the filter, and reports the flip if the value
    /// has already been seen. Note the inversion: adding to the filter emits FALSE.
    /// </summary>
    public void ProcessFilter(bool updateFlag, int value)
    {
        bool wasVisible = IsVisible(value);

        if (updateFlag)
            _filter.Add(value);
        else
            _filter.Remove(value);

        // Covers all three "no event" cases at once: a redundant edit (visibility
        // unchanged), and any edit at all to a value that has never been seen
        // (IsVisible is false on both sides, because seen(v) is false).
        if (IsVisible(value) != wasVisible)
            Emit(!wasVisible, value);
    }

    /// <summary>One value from the input stream.</summary>
    public void ProcessInput(int value)
    {
        bool wasVisible = IsVisible(value);

        _seen.Add(value);                       // one-way: a value is never un-seen

        if (!IsVisible(value))
            return;                             // filtered -- swallowed, but remembered

        // Visible now. Either this is the flip (first sighting while unfiltered),
        // or it was already visible and the policy decides whether to repeat.
        if (!wasVisible || _policy == InputEmission.EveryArrival)
            Emit(true, value);
    }

    /// <summary>The system's output hook. Override to send emissions somewhere real.</summary>
    protected virtual void Emit(bool updateFlag, int value)
    {
        _output.Add((updateFlag, value));
        _sink?.Invoke(updateFlag, value);
    }
}

// =============================================================================
// Part 3 -- an independent reference model, for cross-checking
// =============================================================================

/// <summary>
/// Recomputes the emit stream from an operation log the slow, obvious way:
/// after every operation, take a full snapshot of which values are visible and
/// diff it against the previous snapshot. Shares no logic with the incremental
/// implementation, which is what makes agreement between them evidence.
/// </summary>
public static class FilterSystemReference
{
    public readonly record struct Op(bool IsFilter, bool UpdateFlag, int Value)
    {
        public static Op Filter(bool updateFlag, int value) => new(true, updateFlag, value);

        public static Op Input(int value) => new(false, false, value);

        public override string ToString() =>
            IsFilter ? $"filter({(UpdateFlag ? '+' : '-')}{Value})" : $"input({Value})";
    }

    /// <summary>Pure state-change semantics, i.e. <see cref="InputEmission.OnStateChange"/>.</summary>
    public static List<(bool Visible, int Value)> Replay(IEnumerable<Op> ops)
    {
        var opList = ops.ToList();
        var universe = opList.Select(o => o.Value).Distinct().ToList();

        var filter = new HashSet<int>();
        var seen = new HashSet<int>();
        var emits = new List<(bool, int)>();

        // Snapshot of visible(v) for every value the log mentions.
        Dictionary<int, bool> Snapshot() => universe.ToDictionary(
            v => v, v => seen.Contains(v) && !filter.Contains(v));

        var before = Snapshot();

        foreach (var op in opList)
        {
            if (op.IsFilter)
            {
                if (op.UpdateFlag) filter.Add(op.Value); else filter.Remove(op.Value);
            }
            else
            {
                seen.Add(op.Value);
            }

            var after = Snapshot();
            foreach (var v in universe)
            {
                if (after[v] != before[v])
                    emits.Add((after[v], v));
            }

            before = after;
        }

        return emits;
    }

    /// <summary>
    /// Drops each emit that repeats the previous verdict for the same value.
    /// Collapsing an <see cref="InputEmission.EveryArrival"/> stream this way must
    /// reproduce the <see cref="InputEmission.OnStateChange"/> stream exactly --
    /// the two policies differ only by redundant repeats.
    /// </summary>
    public static List<(bool Visible, int Value)> Collapse(IEnumerable<(bool Visible, int Value)> emits)
    {
        var last = new Dictionary<int, bool>();
        var kept = new List<(bool, int)>();

        foreach (var (visible, value) in emits)
        {
            if (last.TryGetValue(value, out bool previous) && previous == visible)
                continue;

            last[value] = visible;
            kept.Add((visible, value));
        }

        return kept;
    }
}

// =============================================================================
// Part 4 -- tests
// =============================================================================

public partial class FilterSystem
{
    private static string Show(IEnumerable<(bool Visible, int Value)> emits) =>
        emits.Any()
            ? string.Join(" ", emits.Select(e => $"{(e.Visible ? '+' : '-')}{e.Value}"))
            : "(nothing)";

    public static void Run()
    {
        Console.WriteLine("== base version: emit unless currently filtered ==");

        var basic = new FilterSystem();
        basic.ProcessInput(7);              // nothing filtered yet -> emits
        basic.ProcessFilter(7);             // toggle on
        basic.ProcessInput(7);              // swallowed
        basic.ProcessInput(8);              // emits
        basic.ProcessFilter(7);             // toggle off again
        basic.ProcessInput(7);              // emits
        basic.ProcessInput(7);              // emits AGAIN -- per-arrival, by the base rule

        Console.WriteLine($"  emitted: {string.Join(" ", basic.Output)}   (expect 7 8 7 7)");
        Console.WriteLine("  the base problem is one HashSet: no history, no replay, nothing to");
        Console.WriteLine("  reconsider when the filter changes. That is what the follow-up breaks.");

        Console.WriteLine();
        Console.WriteLine("== the published trace, event by event ==");

        var traced = new FilterSystemWithUpdates();
        var script = new (string Label, Action Step)[]
        {
            ("ProcessFilter(+1)", () => traced.ProcessFilter(true, 1)),
            ("ProcessFilter(+2)", () => traced.ProcessFilter(true, 2)),
            ("ProcessInput(1)",   () => traced.ProcessInput(1)),
            ("ProcessInput(3)",   () => traced.ProcessInput(3)),
            ("ProcessFilter(-1)", () => traced.ProcessFilter(false, 1)),
            ("ProcessFilter(+3)", () => traced.ProcessFilter(true, 3)),
        };

        int consumed = 0;
        foreach (var (label, step) in script)
        {
            step();
            var fresh = traced.Output.Skip(consumed).ToList();
            consumed = traced.Output.Count;

            Console.WriteLine(
                $"  {label,-18} filter={{{string.Join(",", traced.Filter.OrderBy(v => v))}}}" +
                $"  seen={{{string.Join(",", traced.Seen.OrderBy(v => v))}}}" +
                $"  emit: {Show(fresh)}");
        }

        Console.WriteLine($"  full emit stream: {Show(traced.Output)}   (expect +3 +1 -3)");
        Console.WriteLine("  +1 is the interesting one: 1 arrived while it was filtered, so it was never");
        Console.WriteLine("  emitted -- and un-filtering it emits it anyway. 'Seen' is not 'was emitted'.");

        Console.WriteLine();
        Console.WriteLine("== the polarity inversion, stated plainly ==");

        var polarity = new FilterSystemWithUpdates();
        polarity.ProcessInput(5);
        polarity.ProcessFilter(true, 5);
        polarity.ProcessFilter(false, 5);
        Console.WriteLine($"  input(5), filter(true, 5), filter(false, 5) -> {Show(polarity.Output)}");
        Console.WriteLine("  ProcessFilter(TRUE, 5) emitted FALSE. Adding to the filter hides the value;");
        Console.WriteLine("  the emit flag reports visibility. Passing updateFlag straight through is wrong.");

        Console.WriteLine();
        Console.WriteLine("== every edge case worth naming ==");

        Console.WriteLine($"  {"filtered then unfiltered, never input:",-46}{Show(Script(f => { f.ProcessFilter(true, 4); f.ProcessFilter(false, 4); }))}");
        Console.WriteLine($"  {"unfilter a value nobody ever filtered:",-46}{Show(Script(f => f.ProcessFilter(false, 4)))}");
        Console.WriteLine($"  {"filter a value twice (redundant edit):",-46}{Show(Script(f => { f.ProcessInput(4); f.ProcessFilter(true, 4); f.ProcessFilter(true, 4); }))}");
        Console.WriteLine($"  {"unfilter twice after a real block:",-46}{Show(Script(f => { f.ProcessInput(4); f.ProcessFilter(true, 4); f.ProcessFilter(false, 4); f.ProcessFilter(false, 4); }))}");
        Console.WriteLine($"  {"input BEFORE it is filtered:",-46}{Show(Script(f => { f.ProcessInput(4); f.ProcessFilter(true, 4); }))}");
        Console.WriteLine($"  {"input AFTER it is filtered:",-46}{Show(Script(f => { f.ProcessFilter(true, 4); f.ProcessInput(4); }))}");
        Console.WriteLine($"  {"blocked input, then unblocked:",-46}{Show(Script(f => { f.ProcessFilter(true, 4); f.ProcessInput(4); f.ProcessFilter(false, 4); }))}");
        Console.WriteLine($"  {"three block/unblock cycles after one input:",-46}{Show(Script(f => { f.ProcessInput(4); for (int i = 0; i < 3; i++) { f.ProcessFilter(true, 4); f.ProcessFilter(false, 4); } }))}");
        Console.WriteLine($"  {"repeated input, EveryArrival (default):",-46}{Show(Script(f => { f.ProcessInput(4); f.ProcessInput(4); f.ProcessInput(4); }))}");
        Console.WriteLine($"  {"repeated input, OnStateChange:",-46}{Show(Script(f => { f.ProcessInput(4); f.ProcessInput(4); f.ProcessInput(4); }, InputEmission.OnStateChange))}");
        Console.WriteLine("  A value the system has never seen on the input is invisible to filter edits,");
        Console.WriteLine("  however many of them there are -- there is no one to tell.");

        Console.WriteLine();
        Console.WriteLine("== the invariant, checked continuously ==");
        Console.WriteLine("  For every value: the LAST thing emitted about it == seen(v) && !blocked(v).");
        Console.WriteLine("  Anything never mentioned by the input is never emitted at all.");

        var rng = new Random(20260810);
        bool matchesReference = true, collapses = true, lastEmitIsTruth = true;
        bool noRedundantEmits = true, unseenStaySilent = true;
        int trials = 0, opsRun = 0;

        for (int trial = 0; trial < 4000; trial++)
        {
            // A small value universe on purpose: collisions are where the bugs are.
            int universe = 1 + rng.Next(4);
            var ops = new List<FilterSystemReference.Op>();
            for (int i = 0; i < 20; i++)
            {
                int value = rng.Next(universe);
                ops.Add(rng.Next(2) == 0
                    ? FilterSystemReference.Op.Input(value)
                    : FilterSystemReference.Op.Filter(rng.Next(2) == 0, value));
            }

            var everyArrival = new FilterSystemWithUpdates();
            var onStateChange = new FilterSystemWithUpdates(InputEmission.OnStateChange);

            foreach (var op in ops)
            {
                foreach (var system in new[] { everyArrival, onStateChange })
                {
                    if (op.IsFilter)
                        system.ProcessFilter(op.UpdateFlag, op.Value);
                    else
                        system.ProcessInput(op.Value);
                }

                // The invariant has to hold after EVERY operation, not just at the end.
                foreach (var system in new[] { everyArrival, onStateChange })
                {
                    var last = new Dictionary<int, bool>();
                    foreach (var (visible, value) in system.Output)
                        last[value] = visible;

                    for (int v = 0; v < universe; v++)
                    {
                        bool everInput = last.ContainsKey(v);
                        lastEmitIsTruth &= !everInput || last[v] == system.IsVisible(v);
                        unseenStaySilent &= everInput || !system.IsVisible(v);
                    }
                }
            }

            var reference = FilterSystemReference.Replay(ops);
            matchesReference &= onStateChange.Output.SequenceEqual(reference);
            collapses &= FilterSystemReference.Collapse(everyArrival.Output).SequenceEqual(reference);

            // OnStateChange must never say the same thing twice in a row about a value.
            var previous = new Dictionary<int, bool>();
            foreach (var (visible, value) in onStateChange.Output)
            {
                noRedundantEmits &= !previous.TryGetValue(value, out bool was) || was != visible;
                previous[value] = visible;
            }

            trials++;
            opsRun += ops.Count;
        }

        Console.WriteLine($"  {$"{trials:n0} random scripts, {opsRun:n0} operations",-56}");
        Console.WriteLine($"  {"OnStateChange == snapshot-diff reference:",-56}{matchesReference}");
        Console.WriteLine($"  {"collapse(EveryArrival) == OnStateChange:",-56}{collapses}");
        Console.WriteLine($"  {"last emit per value == its current visibility:",-56}{lastEmitIsTruth}");
        Console.WriteLine($"  {"a never-input value is never visible, never emitted:",-56}{unseenStaySilent}");
        Console.WriteLine($"  {"OnStateChange never repeats a verdict for a value:",-56}{noRedundantEmits}");

        Console.WriteLine();
        Console.WriteLine("== the base version is the follow-up with the replay switched off ==");

        bool baseAgrees = true;
        for (int trial = 0; trial < 2000; trial++)
        {
            var basicSystem = new FilterSystem();
            var full = new FilterSystemWithUpdates();
            var expected = new List<int>();

            for (int i = 0; i < 20; i++)
            {
                int value = rng.Next(4);
                if (rng.Next(2) == 0)
                {
                    basicSystem.ProcessInput(value);
                    full.ProcessInput(value);
                    if (full.IsVisible(value))
                        expected.Add(value);
                }
                else
                {
                    bool flag = rng.Next(2) == 0;
                    basicSystem.ProcessFilter(flag, value);
                    full.ProcessFilter(flag, value);
                }
            }

            // Keep only the follow-up emissions that an input caused, and the two
            // implementations must agree value for value.
            baseAgrees &= basicSystem.Output.SequenceEqual(expected);
        }

        Console.WriteLine($"  {"base output == follow-up's input-triggered emissions:",-56}{baseAgrees}");
        Console.WriteLine("  Same predicate, two different amounts of memory. The follow-up's whole cost");
        Console.WriteLine("  is the seen set -- and that is also its only scaling problem.");

        Console.WriteLine();
        Console.WriteLine("== the output hook is a hook ==");

        var routed = new List<string>();
        var wired = new FilterSystemWithUpdates(sink: (visible, value) =>
            routed.Add(visible ? $"show {value}" : $"hide {value}"));
        wired.ProcessInput(9);
        wired.ProcessFilter(true, 9);
        Console.WriteLine($"  routed downstream: {string.Join(", ", routed)}");
        Console.WriteLine("  State is mutated BEFORE Emit fires, so a sink that calls back into the");
        Console.WriteLine("  system sees consistent state rather than a half-applied update.");
    }

    private static IReadOnlyList<(bool Visible, int Value)> Script(
        Action<FilterSystemWithUpdates> steps,
        InputEmission policy = InputEmission.EveryArrival)
    {
        var system = new FilterSystemWithUpdates(policy);
        steps(system);
        return system.Output;
    }
}

// ---- Notes for the follow-up questions ----
//
// "The stream is unbounded -- the seen set grows forever."
//     It does, and it is the follow-up's only real cost: the base version holds
//     |filter| entries, the follow-up holds |filter| + |distinct inputs ever|.
//     You cannot drop a value from `seen` on correctness grounds, because any
//     value can be filtered at any future moment and would then owe an emit. The
//     honest answers are all about weakening the contract:
//       - Bound the memory and the promise together: keep `seen` as an LRU of the
//         last N distinct values, and document that filter edits only replay over
//         a recent window. Says what it does, and it is what a real system wants.
//       - A Bloom filter is tempting and is the wrong shape: false positives mean
//         emitting state changes for values that never arrived -- inventing output
//         is worse than dropping it. If you must, invert the roles so the error is
//         a miss rather than a fabrication.
//       - If values are dense and bounded (say 0..2^24), two bitsets beat two hash
//         sets by an order of magnitude on both space and cache behaviour, and the
//         code is unchanged.
//
// "ProcessFilter should take a RANGE, or a predicate, not a single value."
//     This is the follow-up that actually changes the algorithm. A single-value
//     edit touches exactly one value, so the replay is O(1); a range edit has to
//     find every SEEN value inside the range. Keep `seen` in a sorted structure
//     and the replay costs O(log n + k) for the k values it actually reports,
//     which is output-optimal. An arbitrary predicate cannot be indexed and
//     forces an O(|seen|) scan -- the point to make out loud is that the emit
//     count is the lower bound, so the goal is a structure whose scan cost is
//     proportional to the number of emits and not to the size of the state.
//
// "Filters as counts, not booleans (multiple owners each filtering a value)."
//     Replace the filter HashSet with a Dictionary<int,int> refcount. Blocked
//     means count > 0, so an emit fires only on the 0->1 and 1->0 crossings, and
//     everything else is unchanged. This is worth raising unprompted, because a
//     toggle-based API silently breaks the moment two subsystems both want to
//     filter the same value -- the second toggle un-filters it.
//
// "Make it thread-safe."
//     Two separable problems. The state is two sets, so a single lock or a
//     ConcurrentDictionary covers the mutations; the hard part is that emissions
//     must stay ORDERED and non-overlapping, or a downstream consumer sees
//     +v / -v out of order and ends up believing the wrong thing forever, since
//     nothing ever re-sends the truth. Emit under the lock (simple, and Emit had
//     better be fast), or take a sequence number under the lock and let a single
//     drain thread emit in sequence order.
//
// "Persist / restore the system."
//     `seen` plus `filter` is the entire state and both are plain sets. The
//     interesting question is what a subscriber that reconnects should be told:
//     it needs a snapshot of visible(v) for every seen v, not a replay of the
//     emit log. That is the level-triggered vs edge-triggered distinction that
//     InputEmission already makes concrete.
//
// "How would you test it?"
//     The Run() block is the answer. The strongest check is not a fixture, it is
//     the invariant: for every value, the LAST emit about it must equal
//     seen(v) && !blocked(v), asserted after every single operation on random
//     scripts drawn from a deliberately tiny value universe. That one property
//     catches the polarity inversion, the missing seen-set, the redundant-edit
//     emit, and the un-filtering of a never-seen value -- every bug this problem
//     has -- and it is a sentence long.
