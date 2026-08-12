/*
Rate Limiter -- Return Dropped Times (+ online multi-rule concurrent limiter)

OFFLINE TASK. Given arrival times in non-decreasing order, enforce every rule
jointly and return the times of the requests that were DROPPED, in arrival order.

    at most 3 accepted requests in any 1-second window
    at most 20 accepted requests in any 10-second window

    requests = [0.1, 0.2, 0.3, 0.4, 5.0]
    t = 0.4 would be the 4th accept inside (-0.6, 0.4]   -> dropped
    dropped  = [0.4]

ONLINE TASK (the follow-up this always turns into). Same rules, but requests
arrive live on many threads, the handler can throw, and throttled work must be
RETAINED and run when capacity appears rather than dropped on the floor.

---------------------------------------------------------------------------
WHAT TO PIN DOWN BEFORE WRITING ANYTHING
---------------------------------------------------------------------------

 1. Do the windows count ACCEPTED requests or ALL arrivals? This changes the
    answer, not just the code. "3 per second" as a service guarantee means
    accepted -- a dropped request was never served, so it consumed nothing.
    That is the default here. Some platform-style versions count raw positions
    in the sorted input instead; that is DroppedCountingAllArrivals below.
 2. Rolling window or fixed calendar second? "3 per any 1-second window" is
    rolling: (t-1, t]. "no more than 3 in the same second" is a bucket keyed by
    floor(t), and [0.9, 0.95, 0.99, 1.0] gets a DIFFERENT answer under each
    reading. Rolling is the default; the bucket variant is DroppedSameSecond.
 3. Is the boundary inclusive? A request at exactly t=1.0 against an accept at
    t=0.0 -- in or out? Half-open (t-W, t] is the only reading under which a
    steady 3-per-second stream is sustainable forever, so expiry compares <=.
 4. One rule dropped it, both rules dropped it -- still ONE entry in the output.
    A request is dropped once, not once per violated rule.
 5. Real-valued times or integer ticks? See the float section below; it is a
    real bug, not pedantry, and "can we use milliseconds as longs?" is a good
    question to ask out loud.

---------------------------------------------------------------------------
THE STRUCTURE: ONE SLIDING-WINDOW LOG PER RULE
---------------------------------------------------------------------------

Per rule, keep the accepted timestamps still inside that rule's window, oldest
at the front. For each arrival: expire the front of EVERY window, then admit
only if EVERY rule has room.

    t = 0.4, rules (3 / 1s) and (20 / 10s)

        1s window   [0.1, 0.2, 0.3]   full  -> drop, and append nothing
        10s window  [0.1, 0.2, 0.3]   room

Expiry is by time and the oldest accepted timestamp always expires first, so
the structure only ever needs push-back / pop-front -- a queue. Each timestamp
is pushed once and popped once per rule, so the inner `while` is amortized O(1)
and the whole scan is O(n * R).

Memory is the quiet win: a window can never hold more than MaxRequests entries,
because the entry that would have been one more is exactly the entry that got
dropped. So space is O(sum of MaxRequests) = 23 slots here, whether the stream
is 5 requests or 200 million.

    Time:  O(n * R) amortized, R = number of rules
    Space: O(sum of MaxRequests) -- independent of n

---------------------------------------------------------------------------
THE TRAP THAT DECIDES THIS INTERVIEW: ONLY ACCEPTED TIMESTAMPS GO IN
---------------------------------------------------------------------------

Pushing a dropped request into the windows corrupts every later decision. A
burst of 100 requests at t=0 would keep the 1-second window full until t=1
even though only 3 were ever served -- the limiter then throttles traffic it
never admitted, and a client that retries hard makes its own outage worse. This
is the single most commonly reported failure mode on this problem; see
DroppedRequestsDoNotConsumeQuota in the tests.

THE SECOND TRAP: THE 10-SECOND RULE IS NOT DECORATION.

Under sustained load, rule 1 alone would allow 3/sec = 30 per 10 seconds. Rule 2
caps that at 20, so rule 2 becomes the BINDING constraint and starts dropping
requests that rule 1 was perfectly happy with. An implementation that checks
only the 1-second rule passes the worked example and fails exactly there --
that is what RuleTwoIsTheBindingConstraint pins down, and why it also asserts
the same stream drops nothing under rule 1 by itself.

THE THIRD TRAP: BINARY FLOATING POINT ON THE BOUNDARY.

    [0.0, 0.1, 0.2, 1.0, 1.1, 1.2]

1.2 - 1.0 is 0.19999999999999996, and the stored 0.2 is 0.20000000000000001, so
0.2 <= 1.2 - 1.0 is FALSE: the timestamp that should have expired stays, the
window reads 3, and the request at 1.2 is dropped for no reason a human can see.
Nothing about the algorithm is wrong -- the tie is simply not representable.
The fix is not an epsilon (that just moves the arbitrary boundary); it is to
take time in integer ticks, which is what every production limiter does. Both
overloads are here: the double one because that is what gets handed to you, and
the long/tick one because that is what you should ask for.

---------------------------------------------------------------------------
THE ONLINE VARIANT: THREE THINGS BREAK AT ONCE
---------------------------------------------------------------------------

 a) CONCURRENCY. "check the windows, then append" is a read-modify-write. Two
    threads that both read Count == 2 both append, and the 4th request of the
    second sails through. The quota check and the state update have to be ONE
    critical section -- see MultiWindowLimiter.TryAcquire, and the 20-thread
    CheckAndReserveIsAtomic test that fails loudly without it.
 b) HANDLER FAILURE. Quota is taken when the reservation is made, but the
    request is only really served when the handler returns. If the handler
    throws, the reservation must be REFUNDED, or a flapping downstream silently
    eats the whole budget while serving nothing.
 c) THROTTLED WORK IS RETAINED, NOT DISCARDED. A throttled request is parked in
    a delay queue keyed by its next eligible time, and when it wakes, EVERY rule
    is re-evaluated from scratch: elapsed time may have freed rule 1 while rule 2
    is still full, and a refund may have freed capacity earlier than the wake
    time predicted.

Note what the lock does NOT cover: handler execution. Holding a limiter lock
across arbitrary user code serialises the entire service and turns the limiter
into the bottleneck it exists to prevent. The price of running the handler
outside the lock is exactly the refund path in (b) -- provisional reservations.

The clock is injected. Production wants a MONOTONIC clock (Stopwatch, not
DateTime.UtcNow): a wall clock that steps backwards over NTP sync or a DST
change puts `now - window` in the future and freezes the limiter. Tests want a
fake clock, because the alternative is a test suite made of Thread.Sleep.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CodingPatterns.Concurrency;

/// <summary>At most <paramref name="MaxRequests"/> accepted requests in any
/// <paramref name="WindowSeconds"/>-long window.</summary>
public readonly record struct WindowRule(int MaxRequests, double WindowSeconds);

/// <summary>The same rule over integer ticks (milliseconds, microseconds, …) --
/// the representation that makes boundary comparisons exact.</summary>
public readonly record struct TickRule(int MaxRequests, long WindowTicks);

// ---------------------------------------------------------------------------
// Part 1: the offline replay
// ---------------------------------------------------------------------------

public static class RateLimiterDroppedTimes
{
    /// <summary>The two rules from the statement. Kept as DATA rather than a pair
    /// of hardcoded ifs, because the follow-up is always "now add a third rule".</summary>
    public static readonly WindowRule[] DefaultRules =
    {
        new(MaxRequests: 3, WindowSeconds: 1.0),
        new(MaxRequests: 20, WindowSeconds: 10.0),
    };

    /// <summary>The arrival times that got rate limited, in arrival order.</summary>
    public static List<double> Dropped(IEnumerable<double> times, IReadOnlyList<WindowRule> rules = null)
        => Partition(times, rules).Dropped;

    /// <summary>
    /// Splits arrivals into (accepted, dropped), both in arrival order.
    ///
    /// One queue per rule, holding only ACCEPTED timestamps still inside that
    /// rule's window. Expire every window first, then ask every rule for a
    /// verdict, then append to every window -- a request that violates two rules
    /// is still dropped exactly once.
    /// </summary>
    public static (List<double> Accepted, List<double> Dropped) Partition(
        IEnumerable<double> times,
        IReadOnlyList<WindowRule> rules = null)
    {
        rules ??= DefaultRules;
        var windows = rules.Select(_ => new Queue<double>()).ToArray();
        var accepted = new List<double>();
        var dropped = new List<double>();

        foreach (var t in times)
        {
            // Expire across ALL rules before anyone is asked for a verdict.
            // Interleaving expiry with the check is how off-by-one bugs get in.
            for (int i = 0; i < rules.Count; i++)
                while (windows[i].Count > 0 && windows[i].Peek() <= t - rules[i].WindowSeconds)
                    windows[i].Dequeue();

            bool hasRoom = true;
            for (int i = 0; i < rules.Count && hasRoom; i++)
                hasRoom = windows[i].Count < rules[i].MaxRequests;

            if (hasRoom)
            {
                foreach (var window in windows)
                    window.Enqueue(t);          // accepted timestamps only
                accepted.Add(t);
            }
            else
            {
                dropped.Add(t);                 // NOT appended to any window
            }
        }

        return (accepted, dropped);
    }

    /// <summary>
    /// The same algorithm over integer ticks. Identical logic, exact boundaries:
    /// `window.Peek() &lt;= t - WindowTicks` is true or false for a real reason,
    /// not because of how 0.1 rounds. Prefer this whenever you control the input.
    /// </summary>
    public static (List<long> Accepted, List<long> Dropped) PartitionTicks(
        IEnumerable<long> ticks,
        IReadOnlyList<TickRule> rules)
    {
        var windows = rules.Select(_ => new Queue<long>()).ToArray();
        var accepted = new List<long>();
        var dropped = new List<long>();

        foreach (var t in ticks)
        {
            for (int i = 0; i < rules.Count; i++)
                while (windows[i].Count > 0 && windows[i].Peek() <= t - rules[i].WindowTicks)
                    windows[i].Dequeue();

            bool hasRoom = true;
            for (int i = 0; i < rules.Count && hasRoom; i++)
                hasRoom = windows[i].Count < rules[i].MaxRequests;

            if (hasRoom)
            {
                foreach (var window in windows)
                    window.Enqueue(t);
                accepted.Add(t);
            }
            else
            {
                dropped.Add(t);
            }
        }

        return (accepted, dropped);
    }

    /// <summary>
    /// Variant: the windows count EVERY arrival, accepted or not.
    ///
    /// Only for the phrasing "drop a request if it would create more than N
    /// requests in the window", where the window describes the input stream
    /// rather than served traffic. Behaviour differs after a burst: this keeps
    /// rejecting until the burst itself ages out, while the accepted-only
    /// version recovers as soon as it has served under quota.
    /// </summary>
    public static List<double> DroppedCountingAllArrivals(
        IEnumerable<double> times,
        IReadOnlyList<WindowRule> rules = null)
    {
        rules ??= DefaultRules;
        var windows = rules.Select(_ => new Queue<double>()).ToArray();
        var dropped = new List<double>();

        foreach (var t in times)
        {
            bool over = false;
            for (int i = 0; i < rules.Count; i++)
            {
                while (windows[i].Count > 0 && windows[i].Peek() <= t - rules[i].WindowSeconds)
                    windows[i].Dequeue();

                windows[i].Enqueue(t);          // every arrival counts, even a doomed one
                if (windows[i].Count > rules[i].MaxRequests)
                    over = true;
            }

            if (over)
                dropped.Add(t);
        }

        return dropped;
    }

    /// <summary>
    /// Variant: rule 1 is a FIXED calendar second (floor(t)), not a rolling window.
    ///
    /// "More than 3 in the same second" and "more than 3 in any 1-second window"
    /// are different rules. [0.9, 0.95, 0.99, 1.0] passes here (three in second 0,
    /// one in second 1) and drops its last request under the rolling reading.
    /// Ask which one is meant; do not guess.
    /// </summary>
    public static List<double> DroppedSameSecond(
        IEnumerable<double> times,
        int perSecond = 3,
        WindowRule rollingRule = default)
    {
        if (rollingRule == default)
            rollingRule = new WindowRule(20, 10.0);

        long bucketSecond = long.MinValue;
        int bucketCount = 0;
        var rolling = new Queue<double>();
        var dropped = new List<double>();

        foreach (var t in times)
        {
            long second = (long)Math.Floor(t);
            if (second != bucketSecond)
            {
                bucketSecond = second;
                bucketCount = 0;
            }

            while (rolling.Count > 0 && rolling.Peek() <= t - rollingRule.WindowSeconds)
                rolling.Dequeue();

            if (bucketCount < perSecond && rolling.Count < rollingRule.MaxRequests)
            {
                bucketCount++;
                rolling.Enqueue(t);
            }
            else
            {
                dropped.Add(t);
            }
        }

        return dropped;
    }

    public static void Run() => RateLimiterDroppedTimesDemo.Execute();
}

// ---------------------------------------------------------------------------
// Part 2: the online limiter
// ---------------------------------------------------------------------------

/// <summary>Injectable time source: monotonic in production, fake in tests.</summary>
public interface IClock
{
    /// <summary>Seconds since an arbitrary fixed origin. Never decreases.</summary>
    double Now { get; }
}

/// <summary>
/// Stopwatch-backed, so the clock cannot step backwards over an NTP correction
/// or a DST change. DateTime.UtcNow can, and a limiter whose `now` moves
/// backwards computes a window bound in the future and stops admitting anything.
/// </summary>
public sealed class MonotonicClock : IClock
{
    public static readonly MonotonicClock Instance = new();

    private readonly long _origin = Stopwatch.GetTimestamp();

    public double Now => (Stopwatch.GetTimestamp() - _origin) / (double)Stopwatch.Frequency;
}

/// <summary>Deterministic clock -- the only way to test time-dependent logic
/// without a suite full of Thread.Sleep and flaky CI runs.</summary>
public sealed class FakeClock : IClock
{
    private readonly object _lock = new();
    private double _now;

    public FakeClock(double start = 0.0) => _now = start;

    public double Now
    {
        get { lock (_lock) return _now; }
    }

    public double Advance(double seconds)
    {
        lock (_lock) return _now += seconds;
    }
}

/// <summary>
/// What to do with a timestamp older than one already admitted.
///
/// Out-of-order arrivals are not hypothetical -- several producers, a queue that
/// reorders on retry, or a caller stamping events client-side all produce them,
/// and feeding one straight into a FIFO window silently corrupts it: the front
/// stops being the oldest entry, so expiry evicts the wrong timestamps and the
/// limiter's counts quietly stop meaning anything. Choose a policy on purpose.
/// </summary>
public enum LatePolicy
{
    /// <summary>Treat late data as arriving now. Never rejects; distorts history.</summary>
    Clamp,

    /// <summary>Refuse late data outright. Exact windows; loses events.</summary>
    Reject,

    /// <summary>Tolerate lateness up to a bound, reject beyond it. The usual answer.</summary>
    Bounded,
}

public sealed class LateArrivalException : InvalidOperationException
{
    public LateArrivalException(string message) : base(message) { }
}

/// <summary>
/// A provisional claim on quota, held while the handler runs. Provisional is the
/// whole point: the quota is taken before the handler runs so concurrent callers
/// can see it, but it only becomes final at <see cref="MultiWindowLimiter.Commit"/>
/// -- and goes back on <see cref="MultiWindowLimiter.Refund"/> if the handler threw.
/// </summary>
public readonly record struct Reservation(long RequestId, double Timestamp);

/// <summary>
/// Thread-safe sliding-window limiter over several rules at once.
///
/// The lock covers expire + capacity check + append as ONE critical section, and
/// deliberately does not extend over handler execution.
/// </summary>
public sealed class MultiWindowLimiter
{
    private readonly object _lock = new();
    private readonly WindowRule[] _rules;

    // LinkedList rather than Queue: refunds have to remove an entry from the
    // middle. It is O(k) with k <= MaxRequests (23 total here), so the cost is
    // nothing; a plain Queue is enough if you never need to refund.
    private readonly LinkedList<double>[] _windows;

    private readonly IClock _clock;
    private readonly LatePolicy _latePolicy;
    private readonly double _maxLateness;

    private long _nextRequestId = 1;
    private double _highWater = double.NegativeInfinity;   // newest timestamp admitted

    public MultiWindowLimiter(
        IReadOnlyList<WindowRule> rules = null,
        IClock clock = null,
        LatePolicy latePolicy = LatePolicy.Bounded,
        double maxLateness = 0.5)
    {
        rules ??= RateLimiterDroppedTimes.DefaultRules;
        if (rules.Count == 0)
            throw new ArgumentException("At least one rule is required.", nameof(rules));

        _rules = rules.ToArray();
        _windows = _rules.Select(_ => new LinkedList<double>()).ToArray();
        _clock = clock ?? MonotonicClock.Instance;
        _latePolicy = latePolicy;
        _maxLateness = maxLateness;
    }

    /// <summary>
    /// Reserve capacity, or return false if any rule is at its limit.
    ///
    /// One lock, one critical section: expire, check every rule, append. Nothing
    /// happens between the check and the append, which is the only reason two
    /// threads cannot both claim the last free slot.
    /// </summary>
    public bool TryAcquire(out Reservation reservation, double? now = null)
    {
        lock (_lock)
        {
            double t = Normalize(now ?? _clock.Now);
            Expire(t);

            for (int i = 0; i < _rules.Length; i++)
            {
                if (_windows[i].Count >= _rules[i].MaxRequests)
                {
                    reservation = default;
                    return false;
                }
            }

            foreach (var window in _windows)
                window.AddLast(t);

            if (t > _highWater)
                _highWater = t;

            reservation = new Reservation(_nextRequestId++, t);
            return true;
        }
    }

    /// <summary>
    /// Make a reservation final. A no-op by design -- the quota was consumed at
    /// reservation time so concurrent callers could see it. It exists so call
    /// sites read symmetrically against Refund, and so accounting and metrics
    /// have one obvious place to live.
    /// </summary>
    public void Commit(Reservation reservation) { }

    /// <summary>
    /// Give the quota back after a failed handler, so a request that was never
    /// served costs nothing. Removes that exact timestamp from every window;
    /// an entry that already expired on its own is simply not there.
    /// </summary>
    public void Refund(Reservation reservation)
    {
        lock (_lock)
        {
            foreach (var window in _windows)
                window.Remove(reservation.Timestamp);   // first match; counts are what matter
        }
    }

    /// <summary>
    /// Seconds until at least one slot is free under EVERY rule.
    ///
    /// The binding rule is whichever must wait longest: for a full window the
    /// earliest relief is when its oldest entry expires, at first + WindowSeconds.
    /// Rules with room contribute 0. This is a HINT, not a promise -- a refund can
    /// free capacity sooner and another thread can take the slot first, so
    /// whatever wakes on it must re-check rather than assume.
    /// </summary>
    public double RetryAfter(double? now = null)
    {
        lock (_lock)
        {
            double t = now ?? _clock.Now;
            Expire(t);

            double wait = 0.0;
            for (int i = 0; i < _rules.Length; i++)
            {
                if (_windows[i].Count >= _rules[i].MaxRequests)
                    wait = Math.Max(wait, _windows[i].First.Value + _rules[i].WindowSeconds - t);
            }

            return Math.Max(wait, 0.0);
        }
    }

    /// <summary>Current occupancy per rule -- for tests, metrics and debugging.</summary>
    public int[] Snapshot()
    {
        lock (_lock)
        {
            Expire(_clock.Now);
            return _windows.Select(w => w.Count).ToArray();
        }
    }

    // ---- internals; the caller already holds the lock

    private void Expire(double now)
    {
        for (int i = 0; i < _rules.Length; i++)
        {
            var window = _windows[i];
            while (window.Count > 0 && window.First.Value <= now - _rules[i].WindowSeconds)
                window.RemoveFirst();
        }
    }

    /// <summary>Applies the event-time policy so the windows stay sorted -- the
    /// invariant every sliding-window log depends on.</summary>
    private double Normalize(double t)
    {
        if (t >= _highWater)
            return t;

        double lateness = _highWater - t;

        if (_latePolicy == LatePolicy.Clamp)
            return _highWater;

        if (_latePolicy == LatePolicy.Bounded && lateness <= _maxLateness)
            return _highWater;              // within tolerance: fold into the current instant

        throw new LateArrivalException($"Timestamp {t} arrived {lateness:F3}s late.");
    }
}

/// <summary>Counters for <see cref="DeferredExecutor"/>. Deferred counts PARKING
/// EVENTS, not distinct requests -- one request can park more than once.</summary>
public sealed record ExecutorStats(int Executed, int Failed, int Deferred, int Pending);

/// <summary>
/// Runs work under a limiter, retaining throttled work instead of dropping it.
///
/// Two entry points on purpose:
///   Submit -- try now, park on the delay queue if throttled.
///   Pump   -- run everything currently eligible. A background thread calls this
///             on a timer in production; tests call it directly against a
///             FakeClock, which is what makes "deferred work is rechecked, not
///             discarded" an assertion rather than a hope.
/// </summary>
public sealed class DeferredExecutor
{
    private readonly MultiWindowLimiter _limiter;
    private readonly Action<object> _handler;
    private readonly IClock _clock;

    // Keyed by (eligible time, sequence): the sequence tiebreak keeps equally
    // eligible work in FIFO order instead of whatever order the heap produces.
    private readonly PriorityQueue<object, (double EligibleAt, long Sequence)> _parked = new();
    private readonly object _queueLock = new();

    private long _nextSequence = 1;
    private int _executed;
    private int _failed;
    private int _deferred;

    public DeferredExecutor(MultiWindowLimiter limiter, Action<object> handler, IClock clock = null)
    {
        _limiter = limiter;
        _handler = handler;
        _clock = clock ?? MonotonicClock.Instance;
    }

    public ExecutorStats Stats
    {
        get
        {
            lock (_queueLock)
                return new ExecutorStats(_executed, _failed, _deferred, _parked.Count);
        }
    }

    /// <summary>Attempt now; park for later if throttled. True if it ran.</summary>
    public bool Submit(object payload)
    {
        if (Attempt(payload))
            return true;

        Park(payload);
        return false;
    }

    /// <summary>
    /// Run every parked item whose eligible time has arrived; returns how many ran.
    /// Loops, because each success and each refund changes capacity for the items
    /// behind it.
    /// </summary>
    public int Pump()
    {
        int ran = 0;
        while (TryTakeEligible(out var payload))
        {
            if (Attempt(payload))
            {
                ran++;
            }
            else
            {
                // A rule that was not the reason for the wake-up is still full.
                // Re-park and stop: nothing behind this item can pass either, and
                // spinning the whole queue on every tick is how a retry loop turns
                // into a busy loop.
                Park(payload);
                break;
            }
        }

        return ran;
    }

    /// <summary>The reserve -&gt; run -&gt; commit/refund cycle.</summary>
    private bool Attempt(object payload)
    {
        if (!_limiter.TryAcquire(out var reservation))
            return false;

        try
        {
            // Deliberately outside the limiter's lock: a slow or hanging handler
            // must not stop unrelated callers from being rate limited correctly.
            _handler(payload);
        }
        catch (Exception)
        {
            _limiter.Refund(reservation);       // a failed request consumes no quota
            Interlocked.Increment(ref _failed);

            // That refund just freed a slot, so parked work may be runnable ahead
            // of the wake time it was given. Pull the head forward to be rechecked.
            RewakeHead();
            return true;                        // the attempt finished; it was not a throttle
        }

        _limiter.Commit(reservation);
        Interlocked.Increment(ref _executed);
        return true;
    }

    private void Park(object payload)
    {
        double eligibleAt = _clock.Now + _limiter.RetryAfter();
        lock (_queueLock)
        {
            _parked.Enqueue(payload, (eligibleAt, _nextSequence++));
            _deferred++;
        }
    }

    private bool TryTakeEligible(out object payload)
    {
        lock (_queueLock)
        {
            if (_parked.TryPeek(out _, out var priority) && priority.EligibleAt <= _clock.Now)
            {
                payload = _parked.Dequeue();
                return true;
            }
        }

        payload = null;
        return false;
    }

    /// <summary>Make the earliest parked item eligible immediately: capacity just
    /// came back from a refund, which its original wake time knew nothing about.</summary>
    private void RewakeHead()
    {
        lock (_queueLock)
        {
            if (_parked.TryDequeue(out var payload, out var priority))
                _parked.Enqueue(payload, (_clock.Now, priority.Sequence));
        }
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

internal static class RateLimiterDroppedTimesDemo
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"FAILED: {message}");
    }

    private static void Throws<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"FAILED: {message} (expected {typeof(TException).Name})");
    }

    private static string Show<T>(IEnumerable<T> values) => "[" + string.Join(", ", values) + "]";

    public static void Execute()
    {
        WorkedExample();
        EmptyAndSingle();
        AllAtTheSameTimestamp();
        BoundaryIsHalfOpen();
        RuleTwoIsTheBindingConstraint();
        DroppedRequestsDoNotConsumeQuota();
        CountingAllArrivalsDiffers();
        SameSecondBucketDiffers();
        FloatBoundaryHazardAndTheTickFix();
        AcceptedStreamIsSelfConsistent();

        OnlineMatchesOffline();
        CheckAndReserveIsAtomic();
        HandlerFailureRefundsQuota();
        ThrottledWorkIsRetainedNotDiscarded();
        DeferredWorkRechecksEveryRule();
        LateArrivalPolicies();

        var demo = new[] { 0.1, 0.2, 0.3, 0.4, 5.0 };
        Console.WriteLine($"Dropped({Show(demo)}) -> {Show(RateLimiterDroppedTimes.Dropped(demo))}");
        Console.WriteLine("All tests passed.");
    }

    // ---- offline

    private static void WorkedExample()
    {
        var dropped = RateLimiterDroppedTimes.Dropped(new[] { 0.1, 0.2, 0.3, 0.4, 5.0 });
        Check(dropped.SequenceEqual(new[] { 0.4 }), $"worked example, got {Show(dropped)}");
    }

    private static void EmptyAndSingle()
    {
        Check(RateLimiterDroppedTimes.Dropped(Array.Empty<double>()).Count == 0, "empty input");
        Check(RateLimiterDroppedTimes.Dropped(new[] { 7.5 }).Count == 0, "single request");
    }

    private static void AllAtTheSameTimestamp()
    {
        // Rule 1 admits exactly 3; the other 3 are dropped and consume nothing.
        var times = Enumerable.Repeat(2.0, 6);
        var (accepted, dropped) = RateLimiterDroppedTimes.Partition(times);
        Check(accepted.Count == 3 && dropped.Count == 3, $"6 at t=2.0 -> 3 accepted, got {accepted.Count}");
    }

    private static void BoundaryIsHalfOpen()
    {
        // The accept at 0.0 expires exactly at 1.0, so a steady 3/sec stream never
        // drops. A `<` comparison in Expire would drop every request at a whole
        // second, forever.
        var times = new[] { 0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 2.0, 2.0, 2.0 };
        Check(RateLimiterDroppedTimes.Dropped(times).Count == 0, "half-open boundary");
    }

    private static void RuleTwoIsTheBindingConstraint()
    {
        // 3 per second for 10 seconds = 30 requests, all fine by rule 1. Rule 2
        // caps it at 20, so 10 must drop -- and only rule 2 can explain them.
        // Spacing of 0.25 is exactly representable in binary, keeping this test
        // about the rules rather than about floating point (see the next test).
        var times = Enumerable.Range(0, 10)
            .SelectMany(s => new[] { s + 0.0, s + 0.25, s + 0.5 })
            .ToList();

        var (accepted, dropped) = RateLimiterDroppedTimes.Partition(times);
        Check(accepted.Count == 20, $"rule 2 admits 20, got {accepted.Count}");
        Check(dropped.Count == 10, $"rule 2 drops 10, got {dropped.Count}");
        Check(dropped[0] == 6.5, $"first drop at 6.5, got {dropped[0]}");

        // The same stream under rule 1 alone drops nothing. THIS is the assertion
        // a one-rule implementation fails.
        var ruleOneOnly = RateLimiterDroppedTimes.Dropped(times, new[] { new WindowRule(3, 1.0) });
        Check(ruleOneOnly.Count == 0, $"rule 1 alone drops nothing, got {Show(ruleOneOnly)}");
    }

    private static void DroppedRequestsDoNotConsumeQuota()
    {
        // A 100-request burst at t=0 must not poison t=1.0 -- only 3 were served,
        // and all 3 expire exactly at 1.0.
        var times = Enumerable.Repeat(0.0, 100).Concat(new[] { 1.0, 1.0, 1.0 });
        var (accepted, dropped) = RateLimiterDroppedTimes.Partition(times);

        Check(dropped.Count == 97, $"97 of the burst drop, got {dropped.Count}");
        Check(accepted.Count == 6 && accepted.Count(t => t == 1.0) == 3,
            "all three requests at t=1.0 get through");
    }

    private static void CountingAllArrivalsDiffers()
    {
        // Same input, two defensible readings of the rules -- which is exactly why
        // this is worth asking about before writing any code.
        var times = Enumerable.Repeat(0.0, 25).ToList();
        Check(RateLimiterDroppedTimes.Dropped(times).Count == 22, "accepted-only counting");
        Check(RateLimiterDroppedTimes.DroppedCountingAllArrivals(times).Count == 22,
            "all-arrivals counting agrees at a single instant");

        // They part company as soon as time passes: the accepted-only limiter has
        // served 3 and recovers at t=1.0; the all-arrivals one is still holding 25
        // arrivals in its 1-second window and keeps rejecting.
        var withRecovery = times.Concat(new[] { 1.0 }).ToList();
        Check(RateLimiterDroppedTimes.Dropped(withRecovery).Last() != 1.0,
            "accepted-only limiter recovers at t=1.0");
        Check(RateLimiterDroppedTimes.DroppedCountingAllArrivals(withRecovery).Last() == 1.0,
            "all-arrivals limiter is still saturated at t=1.0");
    }

    private static void SameSecondBucketDiffers()
    {
        var times = new[] { 0.9, 0.95, 0.99, 1.0 };
        Check(RateLimiterDroppedTimes.Dropped(times).SequenceEqual(new[] { 1.0 }),
            "rolling window drops the 4th");
        Check(RateLimiterDroppedTimes.DroppedSameSecond(times).Count == 0,
            "fixed calendar seconds drop nothing (3 in second 0, 1 in second 1)");
    }

    private static void FloatBoundaryHazardAndTheTickFix()
    {
        // 1.2 - 1.0 == 0.19999999999999996 < 0.2, so the timestamp that should
        // expire does not, and 1.2 is dropped for a reason invisible in decimal.
        var times = new[] { 0.0, 0.1, 0.2, 1.0, 1.1, 1.2 };
        var dropped = RateLimiterDroppedTimes.Dropped(times);
        Check(dropped.SequenceEqual(new[] { 1.2 }),
            $"binary float breaks the tie at 1.2, got {Show(dropped)}");
        Check(0.2 > 1.2 - 1.0, "…and here is the comparison that does it");

        // The same stream in integer milliseconds: exact, and nothing drops.
        var ticks = new long[] { 0, 100, 200, 1000, 1100, 1200 };
        var tickRules = new[] { new TickRule(3, 1000), new TickRule(20, 10_000) };
        var (_, droppedTicks) = RateLimiterDroppedTimes.PartitionTicks(ticks, tickRules);
        Check(droppedTicks.Count == 0, $"integer ticks drop nothing, got {Show(droppedTicks)}");
    }

    private static void AcceptedStreamIsSelfConsistent()
    {
        // A property rather than a magic number: replay only the ACCEPTED stream
        // and nothing may drop. If it does, the limiter admitted something its own
        // rules forbid. 200k arrivals also demonstrate the memory bound -- the
        // windows never exceed 3 + 20 entries no matter how long the stream is.
        var ticks = Enumerable.Range(0, 200_000).Select(i => (long)i * 10).ToList();
        var rules = new[] { new TickRule(3, 1000), new TickRule(20, 10_000) };

        var (accepted, dropped) = RateLimiterDroppedTimes.PartitionTicks(ticks, rules);
        Check(accepted.Count + dropped.Count == ticks.Count, "every arrival is accounted for");
        Check(accepted.Count > 0 && dropped.Count > 0, "the stream both admits and throttles");

        var (replayAccepted, replayDropped) = RateLimiterDroppedTimes.PartitionTicks(accepted, rules);
        Check(replayDropped.Count == 0 && replayAccepted.Count == accepted.Count,
            $"accepted stream replays cleanly, {replayDropped.Count} dropped on replay");
    }

    // ---- online

    private static void OnlineMatchesOffline()
    {
        var times = new[] { 0.1, 0.2, 0.3, 0.4, 5.0 };
        var clock = new FakeClock();
        var limiter = new MultiWindowLimiter(clock: clock);
        var dropped = new List<double>();

        foreach (var t in times)
        {
            clock.Advance(t - clock.Now);
            if (!limiter.TryAcquire(out _))
                dropped.Add(t);
        }

        Check(dropped.SequenceEqual(RateLimiterDroppedTimes.Dropped(times)),
            $"online agrees with offline, got {Show(dropped)}");
    }

    private static void CheckAndReserveIsAtomic()
    {
        // 20 threads race for 3 slots in a 60-second window. Without one lock over
        // check + append this over-admits; that assert is the whole point.
        var limiter = new MultiWindowLimiter(new[] { new WindowRule(3, 60.0) });
        int granted = 0;
        using var barrier = new Barrier(20);

        var threads = Enumerable.Range(0, 20).Select(worker => new Thread(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < 10; i++)
                if (limiter.TryAcquire(out _))
                    Interlocked.Increment(ref granted);
        })).ToList();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Check(granted == 3, $"exactly 3 grants under contention, got {granted}");
    }

    private static void HandlerFailureRefundsQuota()
    {
        var clock = new FakeClock();
        var limiter = new MultiWindowLimiter(clock: clock);
        int attempts = 0;

        var executor = new DeferredExecutor(limiter, _ =>
        {
            attempts++;
            throw new InvalidOperationException("downstream is down");
        }, clock);

        for (int i = 0; i < 5; i++)
            executor.Submit("req");

        // Every call failed, so every reservation was refunded: 5 requests at the
        // same instant, quota untouched, nothing throttled. Without the refund the
        // 4th and 5th would have been throttled by requests that were never served.
        Check(attempts == 5, $"all 5 attempts ran, got {attempts}");
        Check(executor.Stats.Failed == 5, $"5 failures recorded, got {executor.Stats.Failed}");
        Check(limiter.Snapshot().SequenceEqual(new[] { 0, 0 }),
            $"quota fully refunded, got {Show(limiter.Snapshot())}");
    }

    private static void ThrottledWorkIsRetainedNotDiscarded()
    {
        var clock = new FakeClock();
        var limiter = new MultiWindowLimiter(clock: clock);
        var served = new List<object>();
        var executor = new DeferredExecutor(limiter, served.Add, clock);

        for (int i = 0; i < 5; i++)
            executor.Submit($"req-{i}");

        Check(served.SequenceEqual(new object[] { "req-0", "req-1", "req-2" }),
            $"rule 1 admits 3, got {Show(served)}");
        Check(executor.Stats.Pending == 2, $"2 parked, not dropped, got {executor.Stats.Pending}");
        Check(Math.Abs(limiter.RetryAfter() - 1.0) < 1e-9, "retry hint is one window away");

        Check(executor.Pump() == 0, "still throttled -- and still retained");
        Check(executor.Stats.Pending == 2, "nothing was discarded on a failed pump");

        clock.Advance(1.0);                             // the first three expire
        Check(executor.Pump() == 2, "both parked requests run once capacity returns");
        Check(served.Count == 5 && executor.Stats.Pending == 0, "queue drains in FIFO order");
        Check(served.SequenceEqual(new object[] { "req-0", "req-1", "req-2", "req-3", "req-4" }),
            $"FIFO preserved, got {Show(served)}");
    }

    private static void DeferredWorkRechecksEveryRule()
    {
        // Rule 1 has room after 1s, but rule 2 (2 per 10s here) does not. Waking on
        // one rule's timer and running without re-checking the others breaches rule 2.
        var clock = new FakeClock();
        var limiter = new MultiWindowLimiter(
            new[] { new WindowRule(3, 1.0), new WindowRule(2, 10.0) }, clock);

        var served = new List<object>();
        var executor = new DeferredExecutor(limiter, served.Add, clock);

        for (int i = 0; i < 3; i++)
            executor.Submit(i);

        Check(served.Count == 2, $"rule 2 binds immediately, got {served.Count}");

        clock.Advance(1.5);                             // rule 1's window is empty now
        Check(executor.Pump() == 0, "rule 2 still says no");
        Check(executor.Stats.Pending == 1, "the request is still retained");

        clock.Advance(9.0);                             // rule 2 finally releases
        Check(executor.Pump() == 1, "runs once every rule agrees");
        Check(served.Count == 3, $"all 3 eventually served, got {served.Count}");
    }

    private static void LateArrivalPolicies()
    {
        var rejecting = new MultiWindowLimiter(clock: new FakeClock(), latePolicy: LatePolicy.Reject);
        Check(rejecting.TryAcquire(out _, now: 5.0), "in-order timestamp is admitted");
        Throws<LateArrivalException>(() => rejecting.TryAcquire(out _, now: 4.0),
            "Reject refuses a late timestamp");

        var clamping = new MultiWindowLimiter(clock: new FakeClock(), latePolicy: LatePolicy.Clamp);
        clamping.TryAcquire(out _, now: 5.0);
        Check(clamping.TryAcquire(out _, now: 4.0), "Clamp folds late data into now");
        Check(clamping.Snapshot()[0] == 2, "clamped entries still count against quota");

        var bounded = new MultiWindowLimiter(
            clock: new FakeClock(), latePolicy: LatePolicy.Bounded, maxLateness: 0.5);
        bounded.TryAcquire(out _, now: 5.0);
        Check(bounded.TryAcquire(out _, now: 4.8), "Bounded tolerates lateness within the bound");
        Throws<LateArrivalException>(() => bounded.TryAcquire(out _, now: 3.0),
            "Bounded rejects beyond the bound");
    }
}

// ---------------------------------------------------------------------------
// What gets asked next
// ---------------------------------------------------------------------------
//
// "Make it smooth instead of bursty -- token bucket / leaky bucket."
//     A sliding-window log admits 3 requests in the same microsecond and then
//     nothing for a second; downstream sees a spike, not a rate. A token bucket
//     replaces the log with two numbers -- tokens and lastRefill -- and refills
//     tokens += elapsed * rate, capped at capacity, so capacity is the burst
//     allowance and rate is the sustained throughput, tunable separately. It is
//     also O(1) memory per client instead of O(MaxRequests), which is what makes
//     it the production default. A leaky bucket is the same arithmetic with the
//     queue kept explicit: requests drain at a fixed rate, so output is perfectly
//     smooth and latency is the thing that grows under load. RateLimiter.cs in
//     this folder is the token-bucket implementation, with per-client buckets and
//     idle-bucket cleanup.
//
// "Per client, not global."
//     Dictionary<clientId, MultiWindowLimiter> plus per-client locks -- and now
//     the memory bound is per client, which is why the token bucket's two numbers
//     start to matter and why idle entries need sweeping. A one-request client
//     that never returns must not hold 23 doubles forever.
//
// "Now it runs on 50 servers."
//     Per-process state means the effective limit is 50x the configured one.
//     Options in the order they usually come up: (1) shard clients to servers by
//     consistent hash, so each client's state lives in one place -- cheap, but
//     rebalancing loses state and hot clients still hot-spot a server; (2) shared
//     state in Redis with the whole check-and-update in one Lua script, because
//     GET-then-SET from 50 servers is the same lost-update race as two threads,
//     just with a network in the middle; (3) approximate local limits (each server
//     gets 1/50th of the budget) with periodic reconciliation -- wrong at the
//     edges, but survives a Redis outage. RedisStore.cs covers (2).
//
// "What if the clock jumps?"
//     Stopwatch for elapsed time, always. If timestamps come from clients they are
//     not a clock at all, they are untrusted input: clamp them to a server-side
//     bound or the limiter can be defeated by sending t = now + 3600.
//
// "How would you test it?"
//     The block above is the answer, and its shape is the point. Three assertions
//     do most of the work: rule 2 binding where rule 1 would not, a burst proving
//     dropped requests consume no quota, and replaying the accepted stream through
//     a fresh limiter with zero drops -- a property, so it does not need a
//     hand-computed expected list. Concurrency needs different tools: a Barrier to
//     start 20 threads at the same instant against a known-exact expected count
//     (3, not "about 3"), an injected handler failure to prove the refund path,
//     and a FakeClock so deferred work can be checked deterministically instead of
//     with sleeps.
