/*
Transactional Key-Value Store (design classic)

    Set(key, value)
    Get(key) -> value | null
    Delete(key)
    Begin()      start a new (possibly nested) transaction
    Commit()     merge the current transaction into its parent, atomically
    Rollback()   discard the current transaction

Reads inside a transaction see the in-progress writes of every open ancestor,
falling back to the base store on a miss. Commit/Rollback with no open
transaction is an error. All operations O(1) amortized.

THE WORKED EXAMPLE FROM THE STATEMENT

    Set("a", 1)
    Begin()
    Set("a", 2)
    Get("a")     -> 2      the open transaction shadows the base store
    Begin()
    Set("a", 3)
    Rollback()             the inner frame is thrown away whole
    Get("a")     -> 2      back to what the outer transaction had written
    Commit()               the outer frame merges into the base store
    Get("a")     -> 2

---------------------------------------------------------------------------
THE MODEL: A TRANSACTION IS A DELTA, NOT A COPY
---------------------------------------------------------------------------

The instinct is to snapshot the store on Begin() and swap it back on Rollback().
That is correct and it is O(n) per Begin, which is disqualifying -- a transaction
that touches one key must not pay for the 10M keys it did not touch.

So each open transaction holds only what it CHANGED:

    base      {a: 1, b: 9}      committed state
    frame 0   {a: 2}            outer transaction: "a is 2 now"
    frame 1   {a: 3, b: DEL}    inner transaction: "a is 3, and b is gone"

    Get(k)     walk frames top-down, first frame that CONTAINS k wins; else base
    Set        write into the top frame (or straight into base if none is open)
    Delete     write a TOMBSTONE into the top frame
    Begin      push {}
    Commit     pop the top frame and fold it into the frame below (or into base)
    Rollback   pop the top frame and drop it

THE TRAP: DELETE NEEDS A TOMBSTONE, NOT A REMOVAL.

    Set("a", 1); Begin(); Delete("a"); Get("a")

`frame.Remove("a")` removes nothing -- "a" lives in the base store, not in the
frame -- so the Get falls through and returns 1. Deleting from the BASE is
worse: Rollback() can no longer restore it, because the value is gone. A delete
inside a transaction is a WRITE of "absent", so it needs a value that MEANS
absent and is distinguishable both from "no opinion" (key not in the frame) and
from a legitimately stored null. Hence a private sentinel object, not null:

    Set("a", null) and Delete("a") must not be the same thing, and a
    TryGetValue that returns null cannot tell them apart. Test membership with
    ContainsKey/TryGetValue, and compare against the sentinel by REFERENCE.

THE OTHER TRAP: TOMBSTONES MUST NOT REACH THE BASE STORE.
Committing the outermost frame has to translate tombstones back into real
removals. Skip that and the base store starts handing sentinels to callers.

---------------------------------------------------------------------------
COMPLEXITY, AND THE PART MOST WRITE-UPS SKIP
---------------------------------------------------------------------------

                      stack of deltas          undo log (Part 2)
    Get               O(depth)                 O(1)  <- always one dict lookup
    Set / Delete      O(1)                     O(1)
    Begin             O(1)                     O(1)
    Rollback          O(1)                     O(k), amortized O(1)
    Commit            O(k), amortized O(1)     O(k), amortized O(1)

k is the number of keys the transaction touched, and every one of them was paid
for by a Set/Delete that already happened, so the amortized cost per operation
is O(1) -- say that out loud, it is the whole answer to "is Commit really O(1)?".

Get is the honest weak spot: walking the stack is O(depth), not O(1). At a
nesting depth of 3 nobody cares, but "O(1) amortized" was in the requirements,
so Part 2 shows the shape that actually delivers it: keep ONE dictionary that is
always the current visible state, and push the OLD value onto an undo log
instead of pushing the new value onto a stack. Reads stop searching entirely,
and tombstones disappear -- deletion becomes a real removal, and it is the undo
entry that remembers the key used to exist.

The catch, and the reason the delta stack is still the right base answer: the
undo-log store mutates shared state before the commit, so its uncommitted writes
are visible to everyone. It is a single-writer design. The delta stack keeps the
base store clean until commit, which is exactly what makes the per-thread
stacks in Part 3 work.

WHAT TO ASK BEFORE WRITING CODE
  1. Do transactions nest? (Assume yes -- it is the whole problem. A single
     delta map plus a sentinel is the same code with a stack of depth 1.)
  2. Does Commit() close one level or all of them? One level here: an inner
     commit publishes to the enclosing transaction, not to the world. Nothing is
     durable until the outermost commit.
  3. Can a value legitimately be null? If yes -- and it usually is -- Get()
     returning null cannot separate "missing" from "stored null", so the
     sentinel matters internally and an Exists(key) is worth offering.
  4. Commit/Rollback with nothing open: throw, or no-op? Throw (SQL does).
  5. One store shared by many threads, or one per caller? That is Part 3, and
     the answer decides the whole design -- ask before, not after.

Time:  O(1) amortized for every operation (see the table).
Space: O(distinct keys written across open transactions), on top of the base.

Layout of this file:
    Part 1  TransactionalKeyValueStore  the canonical stack-of-deltas answer
    Part 2  UndoLogKeyValueStore        true O(1) reads via a rollback segment
    Part 3  ThreadSafeKeyValueStore     follow-up 1: per-thread transaction stacks
    Part 4  DiskBackedStore             follow-up 2: blobs on disk, per-key locks
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CodingPatterns.Concurrency;

/// <summary>Commit() or Rollback() with no open transaction.</summary>
public sealed class TransactionException : InvalidOperationException
{
    public TransactionException(string message) : base(message) { }
}

/// <summary>A concurrent transaction committed over a key this one had read.</summary>
public sealed class ConflictException : InvalidOperationException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>
/// Values that are not, and cannot be confused with, any value a caller could
/// store -- including null. Internal on purpose: nobody outside this file can
/// get hold of one, so nobody can store one.
/// </summary>
internal static class Sentinels
{
    private sealed class Sentinel
    {
        private readonly string _name;
        public Sentinel(string name) => _name = name;
        public override string ToString() => _name;
    }

    /// <summary>"This key is absent" -- written into a transaction frame.</summary>
    internal static readonly object Tombstone = new Sentinel("<deleted>");

    /// <summary>"There was no value here" -- recorded in an undo log.</summary>
    internal static readonly object Missing = new Sentinel("<missing>");
}

// ---------------------------------------------------------------------------
// Part 1 -- the canonical answer: a stack of delta maps
// ---------------------------------------------------------------------------

/// <summary>
/// Nested transactions over a dictionary. Each open transaction is one
/// dictionary of changes; a read walks them newest-first and falls through to
/// the committed base store.
/// </summary>
public class TransactionalKeyValueStore
{
    // Committed state. Never holds a tombstone.
    private readonly Dictionary<string, object> _base = new();

    // One delta per open transaction, outermost first.
    private readonly List<Dictionary<string, object>> _frames = new();

    public int Depth => _frames.Count;

    // ---- reads ----

    /// <summary>Newest opinion wins. O(depth), where depth is the nesting level.</summary>
    public object Get(string key)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            // TryGetValue, not "is the value null" -- null is a value a caller
            // is allowed to store.
            if (_frames[i].TryGetValue(key, out var value))
                return ReferenceEquals(value, Sentinels.Tombstone) ? null : value;
        }

        return _base.TryGetValue(key, out var committed) ? committed : null;
    }

    /// <summary>The distinction Get() cannot make: stored null vs never stored.</summary>
    public bool Exists(string key)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (_frames[i].TryGetValue(key, out var value))
                return !ReferenceEquals(value, Sentinels.Tombstone);
        }

        return _base.ContainsKey(key);
    }

    // ---- writes ----

    public void Set(string key, object value)
    {
        if (_frames.Count > 0)
            _frames[^1][key] = value;
        else
            _base[key] = value;                              // autocommit
    }

    public void Delete(string key)
    {
        if (_frames.Count > 0)
            _frames[^1][key] = Sentinels.Tombstone;           // a WRITE of "absent"
        else
            _base.Remove(key);                                // no transaction: really remove
    }

    // ---- transactions ----

    public void Begin() => _frames.Add(new Dictionary<string, object>());

    /// <summary>
    /// Publish the top frame one level down. Atomic: the merge runs to
    /// completion with no interleaving, so an observer never sees half a
    /// transaction. O(k) in the keys this transaction touched, and every one of
    /// them was paid for by the write that created it -- O(1) amortized.
    /// </summary>
    public void Commit()
    {
        if (_frames.Count == 0)
            throw new TransactionException("Commit() with no open transaction");

        var top = _frames[^1];
        _frames.RemoveAt(_frames.Count - 1);

        if (_frames.Count > 0)
        {
            // Into the parent: tombstones stay tombstones. The parent is still a
            // transaction, and "deleted" is now a fact it owns and can undo.
            var parent = _frames[^1];
            foreach (var (key, value) in top)
                parent[key] = value;
            return;
        }

        // Into the base store: tombstones become real removals here, or the base
        // starts handing sentinel objects back to callers.
        foreach (var (key, value) in top)
        {
            if (ReferenceEquals(value, Sentinels.Tombstone))
                _base.Remove(key);
            else
                _base[key] = value;
        }
    }

    /// <summary>Discard the top frame. Nothing to undo -- it was never applied.</summary>
    public void Rollback()
    {
        if (_frames.Count == 0)
            throw new TransactionException("Rollback() with no open transaction");

        _frames.RemoveAt(_frames.Count - 1);
    }

    /// <summary>Begin, run, commit -- and roll back if the body throws.</summary>
    public void InTransaction(Action body)
    {
        Begin();
        int depth = Depth;
        try
        {
            body();
        }
        catch
        {
            if (Depth >= depth)
                Rollback();
            throw;
        }

        Commit();
    }

    /// <summary>The visible state, flattened. O(n) -- tests and debugging only.</summary>
    public Dictionary<string, object> Snapshot()
    {
        var view = new Dictionary<string, object>(_base);
        foreach (var frame in _frames)
        {
            foreach (var (key, value) in frame)
            {
                if (ReferenceEquals(value, Sentinels.Tombstone))
                    view.Remove(key);
                else
                    view[key] = value;
            }
        }

        return view;
    }

    /// <summary>Tests only: proves no tombstone ever escaped into the base store.</summary>
    internal bool BaseIsClean() =>
        _base.Values.All(v => !ReferenceEquals(v, Sentinels.Tombstone));

    public static void Main() => TransactionalKeyValueStoreDemo.Execute();
}

// ---------------------------------------------------------------------------
// Part 2 -- true O(1) reads: keep the state current, log the way back
// ---------------------------------------------------------------------------

/// <summary>
/// Same API, opposite direction. One dictionary is always the current visible
/// state, so Get() never searches; each open transaction carries the old values
/// needed to walk it backwards. This is how real engines do it (a rollback
/// segment), and it is where the O(1) in "O(1) amortized" actually lands.
///
/// The trade: writes land in the shared dictionary immediately, so this shape
/// cannot host two concurrent transactions. See Part 3.
/// </summary>
public class UndoLogKeyValueStore
{
    // ALWAYS the current visible state.
    private readonly Dictionary<string, object> _store = new();

    // Per open transaction: the (key, previous value) pairs needed to undo it.
    private readonly List<List<KeyValuePair<string, object>>> _undo = new();

    public int Depth => _undo.Count;

    // ---- reads: one lookup, whatever the nesting depth ----

    public object Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public bool Exists(string key) => _store.ContainsKey(key);

    // ---- writes: apply now, remember how to take it back ----

    private void Record(string key)
    {
        if (_undo.Count == 0)
            return;

        // Missing, not null: "the key was absent" and "the key held null" must
        // roll back to different states.
        var previous = _store.TryGetValue(key, out var value) ? value : Sentinels.Missing;
        _undo[^1].Add(new KeyValuePair<string, object>(key, previous));
    }

    public void Set(string key, object value)
    {
        Record(key);
        _store[key] = value;
    }

    public void Delete(string key)
    {
        Record(key);
        _store.Remove(key);                 // a real removal -- no tombstone needed
    }

    // ---- transactions ----

    public void Begin() => _undo.Add(new List<KeyValuePair<string, object>>());

    /// <summary>
    /// The writes are already applied, so committing only decides who owns the
    /// right to undo them: the parent inherits the log, or -- at the outermost
    /// level -- it is dropped and the writes become permanent.
    /// </summary>
    public void Commit()
    {
        if (_undo.Count == 0)
            throw new TransactionException("Commit() with no open transaction");

        var top = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        if (_undo.Count > 0)
            _undo[^1].AddRange(top);
    }

    /// <summary>
    /// Replay the log BACKWARDS. Forwards restores intermediate values for any
    /// key written more than once -- an off-by-one that random testing catches
    /// instantly and a hand-written example usually misses.
    /// </summary>
    public void Rollback()
    {
        if (_undo.Count == 0)
            throw new TransactionException("Rollback() with no open transaction");

        var top = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        for (int i = top.Count - 1; i >= 0; i--)
        {
            var (key, previous) = (top[i].Key, top[i].Value);
            if (ReferenceEquals(previous, Sentinels.Missing))
                _store.Remove(key);
            else
                _store[key] = previous;
        }
    }

    public Dictionary<string, object> Snapshot() => new(_store);
}

// ---------------------------------------------------------------------------
// Part 3 -- follow-up 1: many threads, one store
// ---------------------------------------------------------------------------

/// <summary>
/// A shared base store plus a PER-THREAD transaction stack.
///
/// The insight is that the two halves of the state have completely different
/// sharing requirements, and one lock over everything gets both of them wrong:
///
///     the open frames   are private to one thread by definition -- an
///                       uncommitted write is nobody else's business, so they
///                       need no lock at all (ThreadLocal, zero contention)
///     the base store    is shared, so it needs one -- but only for the length
///                       of a lookup or a commit-time merge
///
/// So the lock is held for O(1) on a read and O(k) on a commit, and never while
/// the application code between Begin() and Commit() runs, which is where all
/// the time actually goes. Per-KEY locks on the base store are the tempting
/// refinement and they are the wrong tool here: Commit has to publish k keys
/// atomically, so it would need all k locks at once, taken in a global order to
/// avoid deadlock, and the moment two commits overlap on one key you are back to
/// serializing anyway. Say that out loud; interviewers ask.
///
/// Isolation: uncommitted writes are private and a commit is atomic, so no
/// thread ever sees half a transaction. Reads are not repeatable, though: two
/// reads of one key inside a transaction can differ if somebody commits in
/// between. detectConflicts: true closes that hole by validating, at commit,
/// that nothing this transaction READ has changed since it read it.
/// </summary>
public class ThreadSafeKeyValueStore : IDisposable
{
    // What a transaction wrote, and the version of every key it read straight
    // from the base store.
    private sealed class Frame
    {
        public readonly Dictionary<string, object> Delta = new();
        public readonly Dictionary<string, long> Reads = new();
    }

    private readonly Dictionary<string, object> _base = new();
    private readonly Dictionary<string, long> _versions = new();   // key -> commit counter
    private readonly object _lock = new();                         // guards those two, nothing else
    private readonly ThreadLocal<List<Frame>> _frames = new(() => new List<Frame>());
    private readonly bool _detectConflicts;

    public ThreadSafeKeyValueStore(bool detectConflicts = false) => _detectConflicts = detectConflicts;

    public int Depth => _frames.Value.Count;

    // ---- reads ----

    public object Get(string key)
    {
        var frames = _frames.Value;

        for (int i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].Delta.TryGetValue(key, out var pending))   // our own uncommitted write
                return ReferenceEquals(pending, Sentinels.Tombstone) ? null : pending;
        }

        object value;
        long version;
        lock (_lock)
        {
            value = _base.TryGetValue(key, out var committed) ? committed : null;
            version = _versions.TryGetValue(key, out var v) ? v : 0;
        }

        // First observation wins: validation asks "has this changed since I
        // first looked?", so a later re-read must not refresh the stamp.
        if (frames.Count > 0)
            frames[^1].Reads.TryAdd(key, version);

        return value;
    }

    /// <summary>
    /// Read several keys under ONE acquisition of the base lock, so the result
    /// cannot straddle a commit. Two separate Get() calls can: a commit is
    /// atomic against anything that reads atomically, and nothing else.
    /// </summary>
    public Dictionary<string, object> GetMany(params string[] keys)
    {
        var frames = _frames.Value;
        var result = new Dictionary<string, object>();
        var misses = new List<string>();

        foreach (var key in keys)
        {
            bool found = false;
            for (int i = frames.Count - 1; i >= 0 && !found; i--)
            {
                if (frames[i].Delta.TryGetValue(key, out var pending))
                {
                    result[key] = ReferenceEquals(pending, Sentinels.Tombstone) ? null : pending;
                    found = true;
                }
            }

            if (!found)
                misses.Add(key);
        }

        var stamps = new Dictionary<string, long>();
        lock (_lock)
        {
            foreach (var key in misses)
            {
                result[key] = _base.TryGetValue(key, out var committed) ? committed : null;
                stamps[key] = _versions.TryGetValue(key, out var v) ? v : 0;
            }
        }

        if (frames.Count > 0)
        {
            foreach (var (key, version) in stamps)
                frames[^1].Reads.TryAdd(key, version);
        }

        return result;
    }

    // ---- writes ----

    public void Set(string key, object value)
    {
        var frames = _frames.Value;
        if (frames.Count > 0)
        {
            frames[^1].Delta[key] = value;
            return;
        }

        lock (_lock)
        {
            _base[key] = value;
            Bump(key);
        }
    }

    public void Delete(string key)
    {
        var frames = _frames.Value;
        if (frames.Count > 0)
        {
            frames[^1].Delta[key] = Sentinels.Tombstone;
            return;
        }

        lock (_lock)
        {
            _base.Remove(key);
            Bump(key);
        }
    }

    // Caller holds _lock.
    private void Bump(string key) =>
        _versions[key] = (_versions.TryGetValue(key, out var v) ? v : 0) + 1;

    // ---- transactions ----

    public void Begin() => _frames.Value.Add(new Frame());

    /// <summary>
    /// Nested commit: hand the frame to the parent, no lock -- it is all
    /// thread-private. Outermost commit: take the lock once and publish.
    /// </summary>
    public void Commit()
    {
        var frames = _frames.Value;
        if (frames.Count == 0)
            throw new TransactionException("Commit() with no open transaction");

        var top = frames[^1];
        frames.RemoveAt(frames.Count - 1);

        if (frames.Count > 0)
        {
            var parent = frames[^1];
            foreach (var (key, value) in top.Delta)
                parent.Delta[key] = value;
            foreach (var (key, version) in top.Reads)
                parent.Reads.TryAdd(key, version);         // keep the EARLIER stamp
            return;
        }

        lock (_lock)
        {
            if (_detectConflicts)
            {
                var stale = top.Reads
                    .Where(r => (_versions.TryGetValue(r.Key, out var v) ? v : 0) != r.Value)
                    .Select(r => r.Key)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                if (stale.Count > 0)
                {
                    // The frame is already popped, so the transaction is gone: a
                    // failed commit aborts. Callers retry from Begin().
                    throw new ConflictException(
                        $"concurrent commit touched {string.Join(", ", stale)}");
                }
            }

            foreach (var (key, value) in top.Delta)
            {
                if (ReferenceEquals(value, Sentinels.Tombstone))
                    _base.Remove(key);
                else
                    _base[key] = value;
                Bump(key);
            }
        }
    }

    public void Rollback()
    {
        var frames = _frames.Value;
        if (frames.Count == 0)
            throw new TransactionException("Rollback() with no open transaction");

        frames.RemoveAt(frames.Count - 1);
    }

    public void InTransaction(Action body)
    {
        Begin();
        int depth = Depth;
        try
        {
            body();
        }
        catch
        {
            if (Depth >= depth)
                Rollback();
            throw;
        }

        Commit();
    }

    /// <summary>
    /// The other half of optimistic concurrency: detection is only useful if
    /// somebody retries. Re-runs the body from scratch on ConflictException.
    /// </summary>
    public T WithRetry<T>(Func<T> body, int attempts = 1000)
    {
        for (int attempt = 0; ; attempt++)
        {
            Begin();
            int depth = Depth;
            T result;
            try
            {
                result = body();
            }
            catch
            {
                if (Depth >= depth)
                    Rollback();
                throw;
            }

            try
            {
                Commit();
                return result;
            }
            catch (ConflictException) when (attempt < attempts - 1)
            {
                // Somebody else won the race. Back off a little so the losers do
                // not all retry in lockstep, then rebuild the transaction from
                // the new committed state.
                Thread.SpinWait(20 << Math.Min(attempt, 6));
            }
        }
    }

    public void WithRetry(Action body, int attempts = 1000) =>
        WithRetry<object>(() => { body(); return null; }, attempts);

    public Dictionary<string, object> Snapshot()
    {
        Dictionary<string, object> view;
        lock (_lock)
            view = new Dictionary<string, object>(_base);

        foreach (var frame in _frames.Value)
        {
            foreach (var (key, value) in frame.Delta)
            {
                if (ReferenceEquals(value, Sentinels.Tombstone))
                    view.Remove(key);
                else
                    view[key] = value;
            }
        }

        return view;
    }

    public void Dispose() => _frames.Dispose();
}

// ---------------------------------------------------------------------------
// Part 4 -- follow-up 2 (senior): the values live on disk
// ---------------------------------------------------------------------------

/// <summary>
/// One reader-writer lock per key, created on demand and reclaimed when idle.
///
/// The refcount is the part that is easy to get wrong: a lock cannot be dropped
/// from the table just because its holder released it, or a thread still queued
/// on it ends up waiting on an object nobody else will ever look up, and two
/// threads "holding the same key" hold two different locks. Count the
/// interested parties; remove only at zero.
/// </summary>
public sealed class KeyLockTable : IDisposable
{
    private sealed class Entry
    {
        // Non-recursive on purpose: re-entering on one thread should deadlock
        // loudly rather than quietly break the mutual exclusion it promises.
        public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.NoRecursion);
        public int RefCount;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly KeyLockTable _table;
        private readonly string _key;
        private readonly bool _exclusive;
        private bool _released;

        public Releaser(KeyLockTable table, string key, bool exclusive)
        {
            _table = table;
            _key = key;
            _exclusive = exclusive;
        }

        public void Dispose()
        {
            if (_released)
                return;
            _released = true;
            _table.Release(_key, _exclusive);
        }
    }

    private readonly object _tableLock = new();
    private readonly Dictionary<string, Entry> _locks = new();

    /// <summary>using (table.Hold(key, exclusive: true)) { ... }</summary>
    public IDisposable Hold(string key, bool exclusive)
    {
        Entry entry;
        lock (_tableLock)
        {
            if (!_locks.TryGetValue(key, out entry))
                _locks[key] = entry = new Entry();
            entry.RefCount++;                     // registered BEFORE blocking on the lock
        }

        if (exclusive)
            entry.Lock.EnterWriteLock();
        else
            entry.Lock.EnterReadLock();

        return new Releaser(this, key, exclusive);
    }

    private void Release(string key, bool exclusive)
    {
        Entry entry;
        lock (_tableLock)
            entry = _locks[key];

        if (exclusive)
            entry.Lock.ExitWriteLock();
        else
            entry.Lock.ExitReadLock();

        lock (_tableLock)
        {
            if (--entry.RefCount == 0)
            {
                _locks.Remove(key);
                entry.Lock.Dispose();             // refcount 0: nobody is waiting on it
            }
        }
    }

    /// <summary>Tests: the table must drain back to empty, or it is a leak.</summary>
    public int LiveLocks()
    {
        lock (_tableLock)
            return _locks.Count;
    }

    public void Dispose()
    {
        lock (_tableLock)
        {
            foreach (var entry in _locks.Values)
                entry.Lock.Dispose();
            _locks.Clear();
        }
    }
}

/// <summary>
/// Values are large blobs on disk; the in-memory part is only the index.
///
/// THE PSEUDO-CODE THE INTERVIEWER IS ASKING FOR
///
///     Update(key, mutate):
///         acquire EXCLUSIVE lock on key       &lt;-- around the whole sequence,
///         try:                                    not around each step
///             blob = ReadFromDisk(key)
///             blob = mutate(blob)
///             WriteToDisk(key, blob)
///         finally:
///             release lock on key
///
/// The lock has to span read-modify-write. Lock the read and the write
/// separately and two writers interleave as read/read/write/write: both compute
/// from the same old blob and the second write silently eats the first. That is
/// a lost update, and it is the bug this follow-up exists to find -- the same
/// shape as the Part 3 conflict, one layer down.
///
/// Reads take the lock SHARED, so concurrent readers do not serialize against
/// each other -- which matters here precisely because reads are the expensive
/// operation once values are blobs.
///
/// Durability per key is the temp-file + fsync + atomic-rename trick: a reader
/// sees the whole old blob or the whole new one, never a torn file, even if the
/// process dies mid-write. Note what that does NOT give you: atomicity across
/// KEYS. A commit touching three blobs is three renames, and a crash between
/// them leaves a half-applied transaction. Only a write-ahead log fixes that --
/// see the notes at the bottom.
/// </summary>
public sealed class DiskBackedStore : IDisposable
{
    private readonly string _directory;
    private readonly KeyLockTable _locks = new();

    private int _diskReads;
    private int _diskWrites;

    public DiskBackedStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public int DiskReads => Volatile.Read(ref _diskReads);
    public int DiskWrites => Volatile.Read(ref _diskWrites);
    public int LiveLocks() => _locks.LiveLocks();

    private string PathFor(string key)
    {
        var name = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                name.Append(c);
            else
                name.Append('%').Append(((int)c).ToString("x2"));
        }

        return Path.Combine(_directory, name.ToString());
    }

    // ---- unlocked primitives: the actual I/O ----

    private byte[] ReadBlob(string key)
    {
        try
        {
            var blob = File.ReadAllBytes(PathFor(key));
            Interlocked.Increment(ref _diskReads);
            return blob;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private void WriteBlob(string key, byte[] blob)
    {
        var path = PathFor(key);
        var temp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(blob, 0, blob.Length);
            stream.Flush(flushToDisk: true);      // the bytes hit the platter BEFORE the rename
        }

        File.Move(temp, path, overwrite: true);   // rename(2): atomic on the same volume
        Interlocked.Increment(ref _diskWrites);
    }

    private static void RemoveQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    // ---- the public API, each holding the right lock ----

    public byte[] Get(string key)
    {
        using (_locks.Hold(key, exclusive: false))
            return ReadBlob(key);
    }

    public void Set(string key, byte[] blob)
    {
        using (_locks.Hold(key, exclusive: true))
            WriteBlob(key, blob);
    }

    public void Delete(string key)
    {
        using (_locks.Hold(key, exclusive: true))
            RemoveQuietly(PathFor(key));
    }

    /// <summary>
    /// Read-modify-write under ONE exclusive lock -- the only safe way to
    /// compute a new blob from the old one. Returning null deletes the key.
    /// </summary>
    public byte[] Update(string key, Func<byte[], byte[]> mutate)
    {
        using (_locks.Hold(key, exclusive: true))
        {
            var updated = mutate(ReadBlob(key));
            if (updated is null)
                RemoveQuietly(PathFor(key));
            else
                WriteBlob(key, updated);
            return updated;
        }
    }

    /// <summary>
    /// The unsafe version, kept deliberately: Get and Set each take the lock,
    /// but the gap between them is unprotected, so concurrent callers lose
    /// updates. This is what the follow-up is really asking you to notice.
    /// </summary>
    public void UpdateUnsafe(string key, Func<byte[], byte[]> mutate)
    {
        var blob = Get(key);            // lock taken and released
        var updated = mutate(blob);     // <-- another writer can land right here
        Set(key, updated);              // lock taken again, on stale data
    }

    /// <summary>
    /// Apply a transaction's whole delta (a null blob means delete). Locks are
    /// taken in SORTED key order: two commits sharing keys {a, b} would
    /// otherwise take them in opposite orders and deadlock. A global order over
    /// lock acquisition is the cheapest deadlock prevention there is, and it
    /// costs one sort. Still not atomic across keys -- see the class summary.
    /// </summary>
    public void CommitMany(IReadOnlyDictionary<string, byte[]> writes)
    {
        var keys = writes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var held = new List<IDisposable>(keys.Count);

        try
        {
            foreach (var key in keys)
                held.Add(_locks.Hold(key, exclusive: true));

            foreach (var key in keys)
            {
                if (writes[key] is null)
                    RemoveQuietly(PathFor(key));
                else
                    WriteBlob(key, writes[key]);
            }
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--)
                held[i].Dispose();
        }
    }

    public void Dispose() => _locks.Dispose();
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

/// <summary>
/// The obvious, slow, obviously-correct model: copy the whole store on Begin.
/// O(n) per Begin -- disqualifying as an answer, perfect as an oracle, because
/// it shares no code with either real implementation.
/// </summary>
internal sealed class ReferenceStore
{
    private readonly List<Dictionary<string, object>> _stack =
        new() { new Dictionary<string, object>() };

    public int Depth => _stack.Count - 1;

    public void Set(string key, object value) => _stack[^1][key] = value;

    public void Delete(string key) => _stack[^1].Remove(key);

    public void Begin() => _stack.Add(new Dictionary<string, object>(_stack[^1]));

    public void Commit()
    {
        if (_stack.Count == 1)
            throw new TransactionException("Commit() with no open transaction");

        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        _stack[^1] = top;                       // the child's whole state replaces the parent's
    }

    public void Rollback()
    {
        if (_stack.Count == 1)
            throw new TransactionException("Rollback() with no open transaction");

        _stack.RemoveAt(_stack.Count - 1);
    }

    public Dictionary<string, object> Snapshot() => new(_stack[^1]);
}

internal static class TransactionalKeyValueStoreDemo
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

        throw new Exception($"FAILED: {message}");
    }

    private static bool SameState(Dictionary<string, object> left, Dictionary<string, object> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !Equals(value, other))
                return false;
        }

        return true;
    }

    private static string Show(Dictionary<string, object> state) =>
        state.Count == 0
            ? "{}"
            : "{" + string.Join(", ", state
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}: {kv.Value ?? "null"}")) + "}";

    // Not called Run(): the launcher discovers every public static Run() in the
    // assembly by reflection, and this file should register exactly one problem.
    public static void Execute()
    {
        WorkedExample();
        Tombstones();
        EdgeCases();
        AgainstTheOracle();
        ConcurrentTransactions();
        BlobsOnDisk();

        Console.WriteLine();
        Console.WriteLine("All tests passed.");
    }

    // -----------------------------------------------------------------------

    private static void WorkedExample()
    {
        Console.WriteLine("== the worked example, step by step ==");

        var store = new TransactionalKeyValueStore();
        store.Set("a", 1);
        store.Begin();
        store.Set("a", 2);
        Check(Equals(store.Get("a"), 2), "the open transaction shadows the base store");
        store.Begin();
        store.Set("a", 3);
        Check(Equals(store.Get("a"), 3), "the inner transaction shadows the outer one");
        store.Rollback();
        Check(Equals(store.Get("a"), 2), "rollback must restore the OUTER value, not the base");
        store.Commit();
        Check(Equals(store.Get("a"), 2) && store.Depth == 0, "commit publishes to the base store");
        Console.WriteLine("  stack of deltas:  set/begin/set/begin/set/rollback/commit -> a = 2");

        var undo = new UndoLogKeyValueStore();
        undo.Set("a", 1);
        undo.Begin();
        undo.Set("a", 2);
        Check(Equals(undo.Get("a"), 2), "undo log: transaction write is visible immediately");
        undo.Begin();
        undo.Set("a", 3);
        undo.Rollback();
        Check(Equals(undo.Get("a"), 2), "undo log: rollback restores the outer value");
        undo.Commit();
        Check(Equals(undo.Get("a"), 2) && undo.Depth == 0, "undo log: commit keeps the write");
        Console.WriteLine("  undo log:         same script, same answer, Get() never searches");
    }

    // -----------------------------------------------------------------------

    private static void Tombstones()
    {
        Console.WriteLine();
        Console.WriteLine("== tombstones: the bug this problem is built around ==");

        var store = new TransactionalKeyValueStore();
        store.Set("a", 1);
        store.Begin();
        store.Delete("a");
        Check(store.Get("a") is null, "the delete must shadow the committed value");
        Console.WriteLine("  delete inside a transaction hides the committed value ......... ok");

        store.Rollback();
        Check(Equals(store.Get("a"), 1), "rollback must bring the deleted key back");
        Console.WriteLine("  rollback brings it back (the base was never touched) .......... ok");

        store.Begin();
        store.Delete("a");
        store.Commit();
        Check(store.Get("a") is null && store.BaseIsClean() && store.Snapshot().Count == 0,
            "commit must translate the tombstone into a real removal");
        Console.WriteLine("  commit turns the tombstone into a real removal ................ ok");

        // A stored null and a deleted key are different facts.
        store = new TransactionalKeyValueStore();
        store.Set("a", null);
        Check(store.Get("a") is null && store.Exists("a"), "a stored null exists");
        store.Begin();
        store.Delete("a");
        Check(store.Get("a") is null && !store.Exists("a"), "a deleted key does not exist");
        store.Rollback();
        Check(store.Exists("a"), "rolling back a delete must restore the stored null");
        Console.WriteLine("  Set(k, null) and Delete(k) stay distinguishable ............... ok");

        // Deleting a key that was only ever written inside the transaction.
        store = new TransactionalKeyValueStore();
        store.Begin();
        store.Set("a", 1);
        store.Delete("a");
        Check(store.Get("a") is null, "delete of a key created in the same transaction");
        store.Commit();
        Check(store.Get("a") is null && store.BaseIsClean() && store.Snapshot().Count == 0,
            "committing that delete must leave the base store empty and clean");
        Console.WriteLine("  delete of a key created in the same transaction ............... ok");

        // The undo log needs no tombstone at all, and must not confuse a stored
        // null with an absent key either.
        var undo = new UndoLogKeyValueStore();
        undo.Set("a", null);
        undo.Begin();
        undo.Delete("a");
        Check(!undo.Exists("a"), "undo log: delete removes for real");
        undo.Rollback();
        Check(undo.Exists("a") && undo.Get("a") is null,
            "undo log: MISSING vs null is the same distinction, on the other side");
        Console.WriteLine("  undo log: MISSING plays the same role, on the undo side ....... ok");
    }

    // -----------------------------------------------------------------------

    private static void EdgeCases()
    {
        Console.WriteLine();
        Console.WriteLine("== edge cases ==");

        var empty = new TransactionalKeyValueStore();
        Throws<TransactionException>(() => empty.Commit(), "Commit() with nothing open must throw");
        Throws<TransactionException>(() => empty.Rollback(), "Rollback() with nothing open must throw");

        var emptyUndo = new UndoLogKeyValueStore();
        Throws<TransactionException>(() => emptyUndo.Commit(), "undo log: Commit() must throw");
        Throws<TransactionException>(() => emptyUndo.Rollback(), "undo log: Rollback() must throw");
        Console.WriteLine("  commit/rollback with no open transaction throw ................ ok");

        // Nested rollback after a sibling commit: the committed sibling survives,
        // because it was folded into the parent before the second frame opened.
        var store = new TransactionalKeyValueStore();
        store.Set("x", 0);
        store.Begin();
        store.Begin();
        store.Set("x", 1);
        store.Commit();                 // inner sibling commits into the outer frame
        store.Begin();
        store.Set("x", 2);
        store.Rollback();               // second sibling is discarded
        Check(Equals(store.Get("x"), 1), "the committed sibling must survive the rollback");
        store.Rollback();               // ...and now discard the parent too
        Check(Equals(store.Get("x"), 0), "rolling the parent back discards the sibling's commit");
        Console.WriteLine("  nested rollback after a sibling commit ........................ ok");

        // Get on a deleted key inside a nested transaction, three levels down.
        store = new TransactionalKeyValueStore();
        store.Set("k", "base");
        store.Begin();
        store.Set("k", "outer");
        store.Begin();
        store.Delete("k");
        store.Begin();
        Check(store.Get("k") is null, "the tombstone two frames up still shadows");
        store.Set("k", "inner");
        Check(Equals(store.Get("k"), "inner"), "the innermost write wins");
        store.Rollback();
        Check(store.Get("k") is null, "back to the tombstone");
        store.Rollback();
        Check(Equals(store.Get("k"), "outer"), "back to the outer write");
        Console.WriteLine("  a tombstone shadows through deeper frames ..................... ok");

        // Very deep nesting. The point is that Begin() and Rollback() are O(1):
        // a snapshot-per-Begin design dies here.
        var deep = new TransactionalKeyValueStore();
        deep.Set("d", -1);
        for (int i = 0; i < 10_000; i++)
        {
            deep.Begin();
            deep.Set("d", i);
        }

        Check(Equals(deep.Get("d"), 9_999), "deep nesting: the innermost write wins");
        for (int i = 0; i < 5_000; i++)
            deep.Rollback();
        Check(Equals(deep.Get("d"), 4_999), "deep nesting: 5,000 rollbacks");
        for (int i = 0; i < 5_000; i++)
            deep.Commit();
        Check(Equals(deep.Get("d"), 4_999) && deep.Depth == 0, "deep nesting: 5,000 commits");
        Console.WriteLine("  10,000 nested transactions, unwound both ways ................. ok");

        var deepUndo = new UndoLogKeyValueStore();
        for (int i = 0; i < 10_000; i++)
        {
            deepUndo.Begin();
            deepUndo.Set("d", i);
        }

        Check(Equals(deepUndo.Get("d"), 9_999), "undo log: deep nesting reads in O(1)");
        for (int i = 0; i < 10_000; i++)
            deepUndo.Rollback();
        Check(deepUndo.Snapshot().Count == 0,
            "10,000 rollbacks must unwind to exactly the starting state");
        Console.WriteLine("  ...and 10,000 undo-log rollbacks land on the starting state ... ok");

        // InTransaction, including the failure path.
        store = new TransactionalKeyValueStore();
        store.InTransaction(() => store.Set("committed", true));
        Throws<InvalidOperationException>(
            () => store.InTransaction(() =>
            {
                store.Set("aborted", true);
                throw new InvalidOperationException("boom");
            }),
            "InTransaction must propagate the body's exception");

        Check(store.Depth == 0 && SameState(store.Snapshot(),
                new Dictionary<string, object> { ["committed"] = true }),
            "a throwing body must roll back and leave nothing behind");
        Console.WriteLine("  InTransaction commits on exit, rolls back on exception ........ ok");
    }

    // -----------------------------------------------------------------------

    private static void AgainstTheOracle()
    {
        Console.WriteLine();
        Console.WriteLine("== both implementations vs. a copy-on-begin oracle ==");

        var rng = new Random(20260810);
        var keys = new[] { "a", "b", "c" };                    // tiny universe: forces collisions
        var values = new object[] { 0, 1, null, "x" };         // null is in there on purpose
        int scripts = 0, operations = 0;

        for (int script = 0; script < 3_000; script++)
        {
            var store = new TransactionalKeyValueStore();
            var undo = new UndoLogKeyValueStore();
            var oracle = new ReferenceStore();
            int depth = 0;

            for (int step = 0; step < 40; step++)
            {
                double roll = rng.NextDouble();
                string key = keys[rng.Next(keys.Length)];

                if (roll < 0.40)
                {
                    object value = values[rng.Next(values.Length)];
                    store.Set(key, value);
                    undo.Set(key, value);
                    oracle.Set(key, value);
                }
                else if (roll < 0.60)
                {
                    store.Delete(key);
                    undo.Delete(key);
                    oracle.Delete(key);
                }
                else if (roll < 0.80)
                {
                    store.Begin();
                    undo.Begin();
                    oracle.Begin();
                    depth++;
                }
                else if (roll < 0.90 && depth > 0)
                {
                    store.Commit();
                    undo.Commit();
                    oracle.Commit();
                    depth--;
                }
                else if (depth > 0)
                {
                    store.Rollback();
                    undo.Rollback();
                    oracle.Rollback();
                    depth--;
                }
                else
                {
                    continue;
                }

                operations++;

                // The invariant has to hold after EVERY operation, not just at the end.
                var expected = oracle.Snapshot();
                Check(SameState(store.Snapshot(), expected), "stack of deltas diverged from the oracle");
                Check(SameState(undo.Snapshot(), expected), "undo log diverged from the oracle");
                Check(store.Depth == depth && undo.Depth == depth && oracle.Depth == depth,
                    "depth diverged");

                foreach (var probe in keys)
                {
                    expected.TryGetValue(probe, out var want);
                    Check(Equals(store.Get(probe), want), "stack of deltas: Get disagreed");
                    Check(Equals(undo.Get(probe), want), "undo log: Get disagreed");
                    Check(store.Exists(probe) == expected.ContainsKey(probe),
                        "stack of deltas: Exists disagreed");
                    Check(undo.Exists(probe) == expected.ContainsKey(probe),
                        "undo log: Exists disagreed");
                }
            }

            // Unwind whatever is still open: rolling everything back must land on
            // the state as of the outermost Begin().
            while (depth-- > 0)
            {
                store.Rollback();
                undo.Rollback();
                oracle.Rollback();
            }

            Check(SameState(store.Snapshot(), oracle.Snapshot()), "unwound state diverged");
            Check(SameState(undo.Snapshot(), oracle.Snapshot()), "undo log unwound state diverged");
            Check(store.BaseIsClean(), "a tombstone escaped into the base store");
            scripts++;
        }

        Console.WriteLine($"  {scripts:N0} random scripts, {operations:N0} operations, checked after every step:");
        Console.WriteLine("    stack-of-deltas snapshot == oracle");
        Console.WriteLine("    undo-log snapshot        == oracle");
        Console.WriteLine("    Get/Exists agree with the oracle for every key, at every depth");
        Console.WriteLine("    no tombstone ever reaches the base store");
    }

    // -----------------------------------------------------------------------

    private static void ConcurrentTransactions()
    {
        Console.WriteLine();
        Console.WriteLine("== follow-up 1: concurrent transactions ==");

        // Isolation: two threads sit on uncommitted writes at the same instant.
        using (var shared = new ThreadSafeKeyValueStore())
        {
            shared.Set("shared", "committed");
            var barrier = new Barrier(2);
            var observed = new Dictionary<string, (object Shared, object Other)>();

            void Isolated(string name, string value)
            {
                shared.Begin();
                shared.Set("shared", value);
                shared.Set(name, value);
                barrier.SignalAndWait();          // both threads now hold uncommitted writes
                lock (observed)
                    observed[name] = (shared.Get("shared"), shared.Get("other"));
                barrier.SignalAndWait();
                shared.Rollback();
            }

            var threads = new[]
            {
                new Thread(() => Isolated("t1", "one")),
                new Thread(() => Isolated("t2", "two")),
            };
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Check(Equals(observed["t1"].Shared, "one") && observed["t1"].Other is null,
                "t1 must see only its own uncommitted writes");
            Check(Equals(observed["t2"].Shared, "two") && observed["t2"].Other is null,
                "t2 must see only its own uncommitted writes");
            Check(SameState(shared.Snapshot(),
                    new Dictionary<string, object> { ["shared"] = "committed" }),
                "two rollbacks must leave the base store untouched");

            Console.WriteLine("  each thread saw only its own uncommitted writes ............... ok");
            Console.WriteLine("  both rolled back; the base store is untouched ................. ok");
        }

        // Atomicity: a two-key commit is all-or-nothing against anything that
        // reads atomically -- and NOT against two separate Get() calls, which is
        // the honest half of the answer.
        using (var atomic = new ThreadSafeKeyValueStore())
        {
            atomic.Set("a", 0);
            atomic.Set("b", 0);

            int tornAtomic = 0, tornSplit = 0;
            var stop = new ManualResetEventSlim(false);

            var writer = new Thread(() =>
            {
                for (int i = 1; i <= 3_000; i++)
                {
                    atomic.Begin();
                    atomic.Set("a", i);
                    atomic.Set("b", i);
                    atomic.Commit();
                }

                stop.Set();
            });

            var reader = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    var pair = atomic.GetMany("a", "b");                 // one lock acquisition
                    if (!Equals(pair["a"], pair["b"]))
                        Interlocked.Increment(ref tornAtomic);

                    if (!Equals(atomic.Get("a"), atomic.Get("b")))       // two lock acquisitions
                        Interlocked.Increment(ref tornSplit);
                }
            });

            writer.Start();
            reader.Start();
            writer.Join();
            reader.Join();

            Check(tornAtomic == 0, $"GetMany saw {tornAtomic} partially applied commits");
            Check(SameState(atomic.Snapshot(),
                    new Dictionary<string, object> { ["a"] = 3_000, ["b"] = 3_000 }),
                "3,000 two-key commits must all land");

            Console.WriteLine("  1 writer / 1 reader, 3,000 two-key commits:");
            Console.WriteLine($"    GetMany (one lock, atomic read):   {tornAtomic} torn reads  <- the guarantee");
            Console.WriteLine($"    Get + Get (two locks, read skew):  {tornSplit} torn reads  <- not a guarantee");
            Console.WriteLine("  A commit is atomic against readers that read atomically. Two");
            Console.WriteLine("  separate reads straddle it -- that is read skew, and it is why");
            Console.WriteLine("  'atomic commit' and 'repeatable read' are two different promises.");
        }

        // Lost update: the anomaly the base design still allows, and the fix.
        (int Total, int Expected) Counter(ThreadSafeKeyValueStore store, bool retry)
        {
            const int threadCount = 4, perThread = 50;
            store.Set("count", 0);

            void Bump()
            {
                for (int i = 0; i < perThread; i++)
                {
                    if (retry)
                    {
                        store.WithRetry(() => store.Set("count", (int)store.Get("count") + 1));
                    }
                    else
                    {
                        store.Begin();
                        store.Set("count", (int)store.Get("count") + 1);
                        Thread.Yield();                 // widen the window; it races without this too
                        store.Commit();
                    }
                }
            }

            var workers = Enumerable.Range(0, threadCount).Select(_ => new Thread(Bump)).ToList();
            foreach (var t in workers) t.Start();
            foreach (var t in workers) t.Join();

            return ((int)store.Get("count"), threadCount * perThread);
        }

        using (var lossy = new ThreadSafeKeyValueStore())
        {
            var (total, expected) = Counter(lossy, retry: false);
            Check(total <= expected, "a counter cannot exceed the number of increments");
            Console.WriteLine();
            Console.WriteLine($"  read-modify-write on 4 threads, no detection: {total}/{expected}"
                              + $" ({expected - total} lost)");
        }

        using (var safe = new ThreadSafeKeyValueStore(detectConflicts: true))
        {
            var (total, expected) = Counter(safe, retry: true);
            Check(total == expected, $"conflict detection + retry must be exact: {total}/{expected}");
            Console.WriteLine($"  same load with conflict detection + retry:    {total}/{expected} (exact)");
            Console.WriteLine("  Detection is version validation at commit: 'has anything I READ");
            Console.WriteLine("  changed since I read it?'. Cheap -- and useless without the retry.");
        }

        // A conflict really does abort, deterministically.
        using (var occ = new ThreadSafeKeyValueStore(detectConflicts: true))
        {
            occ.Set("k", 1);
            occ.Begin();
            Check(Equals(occ.Get("k"), 1), "stamp the read");

            var interloper = new Thread(() => occ.Set("k", 99));   // another thread -> other frames
            interloper.Start();
            interloper.Join();

            occ.Set("k", 2);
            Throws<ConflictException>(() => occ.Commit(),
                "committing over a concurrently modified read must conflict");
            Check(Equals(occ.Get("k"), 99) && occ.Depth == 0,
                "a failed commit must abort the transaction");
            Console.WriteLine("  a commit over a concurrently modified read aborts ............. ok");
        }
    }

    // -----------------------------------------------------------------------

    private static void BlobsOnDisk()
    {
        Console.WriteLine();
        Console.WriteLine("== follow-up 2: values are blobs on disk ==");

        var scratch = Path.Combine(Path.GetTempPath(), "txn_kv_" + Guid.NewGuid().ToString("n"));

        try
        {
            using var disk = new DiskBackedStore(scratch);

            disk.Set("blob", Encoding.UTF8.GetBytes("0"));
            Check(Encoding.UTF8.GetString(disk.Get("blob")) == "0", "round-trip through the disk");
            Check(disk.Get("nothing") is null, "a missing key reads back as null");

            byte[] Increment(byte[] old) =>
                Encoding.UTF8.GetBytes((int.Parse(old is null ? "0" : Encoding.UTF8.GetString(old)) + 1)
                    .ToString());

            // 8 threads, 50 read-modify-writes each, all on ONE key. Every
            // increment must survive, because Update() holds the key's write lock
            // across the whole read/modify/write.
            const int crewSize = 8, perThread = 50;
            var crew = Enumerable.Range(0, crewSize).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < perThread; i++)
                    disk.Update("blob", Increment);
            })).ToList();

            foreach (var t in crew) t.Start();
            foreach (var t in crew) t.Join();

            int locked = int.Parse(Encoding.UTF8.GetString(disk.Get("blob")));
            Check(locked == crewSize * perThread,
                $"lock-spanning read-modify-write must not lose updates: {locked}");
            Console.WriteLine($"  8 threads x 50 read-modify-writes, Update():      {locked}/400 (exact)");

            // The same loop with the lock released between the read and the write.
            disk.Set("unsafe", Encoding.UTF8.GetBytes("0"));
            crew = Enumerable.Range(0, crewSize).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < perThread; i++)
                    disk.UpdateUnsafe("unsafe", Increment);
            })).ToList();

            foreach (var t in crew) t.Start();
            foreach (var t in crew) t.Join();

            int unlocked = int.Parse(Encoding.UTF8.GetString(disk.Get("unsafe")));
            Check(unlocked <= crewSize * perThread, "a counter cannot exceed its increments");
            Console.WriteLine($"  ...with the lock released between read and write: {unlocked}/400"
                              + $" ({crewSize * perThread - unlocked} lost)");
            Console.WriteLine("  Same bug as the Part 3 lost update, one layer down: the lock has to");
            Console.WriteLine("  span the read AND the write, not each of them separately.");

            // Concurrent readers take the lock SHARED and do not serialize.
            var seen = new List<string>();
            crew = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                var local = new List<string>();
                for (int i = 0; i < 100; i++)
                    local.Add(Encoding.UTF8.GetString(disk.Get("blob")));
                lock (seen)
                    seen.AddRange(local);
            })).ToList();

            foreach (var t in crew) t.Start();
            foreach (var t in crew) t.Join();

            Check(seen.Count == 400 && seen.All(v => v == "400"), "readers must agree with each other");
            Console.WriteLine("  4 concurrent readers, shared locks, consistent blobs ......... ok");

            // Multi-key commit: locks taken in sorted order, so two commits that
            // overlap on the same keys cannot deadlock.
            void CommitPairs(string a, string b)
            {
                for (int i = 0; i < 200; i++)
                {
                    disk.CommitMany(new Dictionary<string, byte[]>
                    {
                        ["alpha"] = Encoding.UTF8.GetBytes(a),
                        ["beta"] = Encoding.UTF8.GetBytes(b),
                    });
                }
            }

            crew = new List<Thread>
            {
                new(() => CommitPairs("A", "B")),
                new(() => CommitPairs("a", "b")),
            };

            foreach (var t in crew) t.Start();
            foreach (var t in crew)
                Check(t.Join(TimeSpan.FromSeconds(30)), "deadlock in CommitMany");

            Check(Encoding.UTF8.GetString(disk.Get("alpha")) is "A" or "a", "alpha survived");
            Check(Encoding.UTF8.GetString(disk.Get("beta")) is "B" or "b", "beta survived");
            Console.WriteLine("  two threads, overlapping 2-key commits, no deadlock .......... ok");

            disk.CommitMany(new Dictionary<string, byte[]> { ["alpha"] = null });
            Check(disk.Get("alpha") is null, "a null blob in a commit means delete");

            Check(disk.LiveLocks() == 0, $"the lock table leaked {disk.LiveLocks()} locks");
            Console.WriteLine("  the per-key lock table drains back to empty .................. ok");

            // Read amplification, made concrete.
            int before = disk.DiskReads;
            for (int i = 0; i < 20; i++)
                disk.Get("blob");
            Console.WriteLine($"  20 Gets of one unchanged blob -> {disk.DiskReads - before} disk reads"
                              + "  (this is the problem)");
            Console.WriteLine("  Every logical read is a physical read. A hot key served from a cache");
            Console.WriteLine("  of decoded blobs turns 20 reads into 1 -- see the notes for why");
            Console.WriteLine("  invalidating that cache is the hard part once commits exist.");
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (DirectoryNotFoundException) { }
        }
    }
}

// ---- Notes for the follow-up questions ----
//
// "Is Commit really O(1)?"
//     Per operation, amortized, yes. A commit moves k entries, but each of those
//     entries was created by a Set/Delete that already paid O(1), so charge the
//     move to the write that created it and every operation is O(1) amortized.
//     Worst case per call it is O(k), and that is unavoidable: publishing k
//     writes atomically has to touch k things. What you can avoid is O(n) -- the
//     copy-on-Begin design in ReferenceStore -- and that is the real distinction
//     the requirement is testing.
//
// "Get walks the stack -- that is O(depth), not O(1)."
//     Correct, and worth conceding immediately rather than defending. Three
//     answers, in order of how much they cost you: (a) depth is a property of
//     the program's structure, not of the data size, so O(depth) is O(1) in
//     practice; (b) UndoLogKeyValueStore, where the visible state is always
//     materialised and a read is one lookup; (c) keep a key -> version-stack
//     index alongside the frames, so a read is one hash lookup into a list whose
//     top is the answer, and Rollback pops one entry per key the transaction
//     wrote. (c) is what you build if you need Part 2's read cost AND Part 1's
//     clean base store at the same time.
//
// "Why not deep-copy the store on Begin?"
//     O(n) per Begin and O(n) memory per open transaction, for a transaction
//     that usually writes one key. It is also the wrong shape for concurrency: a
//     copy has to be reconciled at commit, so you end up rebuilding the delta
//     anyway just to know what changed. Persistent/immutable maps (HAMTs, or
//     System.Collections.Immutable) make the copy O(1) with structural sharing
//     and are the honest answer for a functional version -- worth naming, not
//     worth writing here.
//
// "Why one global lock on the base store rather than per-key locks?"
//     Because Commit is the operation that matters and it is inherently
//     multi-key: publishing k writes atomically under per-key locks means
//     holding k locks at once, acquired in a global order to avoid deadlock, and
//     released together. That is strictly more machinery than one lock, and it
//     only wins when commits rarely overlap. The scaling answer is in between:
//     stripe the base store into N shards, each with its own lock, and let a
//     commit take the shards it needs in index order. N = 64 turns one
//     contention point into 64 without changing the shape of the code. Per-key
//     locks DO become right at the disk layer (Part 4), because there the
//     critical section is long -- it contains I/O -- and usually single-key.
//
// "What isolation level is this?"
//     Part 3 as written gives atomicity and no dirty reads, but reads are not
//     repeatable: a key read twice inside one transaction can change underneath
//     you, which is exactly how the lost-update demo loses counts, and two
//     separate Gets can straddle a commit (read skew -- the GetMany contrast in
//     the tests). Two ways up: OPTIMISTIC (implemented) -- stamp every key you
//     read with its version, validate at commit, abort on mismatch, retry;
//     cheap under low contention, livelock-prone under high. PESSIMISTIC -- take
//     a shared lock on read and an exclusive one on write and hold both to
//     commit (two-phase locking), which buys serializability at the price of
//     deadlocks and a detector to break them. MVCC is the third door: keep a
//     version chain per key and hand each transaction a consistent snapshot as
//     of its start -- what Postgres does, and why readers never block writers.
//
// "The value blobs are huge -- what changes?"
//     Keep BLOBS OUT OF THE FRAMES. A frame should hold a handle (path, offset,
//     content hash), never bytes, so Begin/Commit/Rollback stay proportional to
//     the number of keys touched and never to the size of the data. A
//     transaction then writes new blobs to fresh files and Commit swaps
//     pointers, which is copy-on-write: Rollback is deleting the orphans, and a
//     background GC reclaims anything unreferenced. It also keeps Commit cheap,
//     because it moves pointers rather than megabytes.
//
// "Read amplification."
//     Four sources worth naming, in the order they usually bite:
//       1. Every logical read is a physical read. Fix: an LRU cache of decoded
//          blobs. The transactional wrinkle is invalidation -- a commit must
//          evict the keys it wrote BEFORE releasing the key lock, or a reader
//          repopulates the cache with the pre-commit value and it stays wrong.
//       2. Read-modify-write of one field pulls the whole blob. Fix: chunk the
//          blob and address chunks, or keep an append-only delta log per key and
//          fold it in at compaction time.
//       3. LSM-structured storage answers one read from several levels. Fix:
//          per-level bloom filters so the misses cost nothing, plus compaction.
//       4. Deep transaction stacks multiply the miss path if frames hold blobs
//          rather than handles -- which is, again, why they should hold handles.
//     The number to reason about out loud is bytes read per byte returned. State
//     the baseline before proposing a fix, because half of these buy read
//     amplification back with write amplification.
//
// "What if the disk write fails halfway through a commit?"
//     Per key, temp file + fsync + atomic rename means a reader sees the whole
//     old blob or the whole new one, never a torn file. ACROSS keys there is no
//     such guarantee: three renames, a crash after the second, and the
//     transaction is half applied. The fix is a write-ahead log:
//
//         1. append BEGIN + one record per intended write to the log; fsync
//         2. append COMMIT to the log; fsync    <-- the transaction is now durable
//         3. apply the writes to the data files, in any order
//         4. mark the log entry applied; the log can then be truncated
//
//     Recovery replays the log: any transaction with a COMMIT record is
//     re-applied (idempotently -- which is why the records carry the full new
//     value or a content hash), any transaction without one is discarded. The
//     commit point is a single fsync of a single append, which is also why a WAL
//     is FASTER than the thing it protects: one sequential write replaces k
//     random ones. Group commit -- batching several transactions into one fsync
//     -- is the next question after that.
//
// "How would you test it?"
//     The test block above is the answer, and its structure is the point: a slow
//     copy-on-Begin oracle that shares no code with either implementation, run
//     against random scripts drawn from a deliberately tiny key universe (3 keys,
//     4 values, one of them null) and checked after EVERY operation rather than
//     at the end. That catches all four classic bugs at once -- the missing
//     tombstone, the tombstone escaping into the base store, the rollback that
//     replays its undo log forwards, and Set(k, null) being confused with
//     Delete(k). Concurrency needs different tools: a barrier to pin two threads
//     at the interesting instant (isolation), a writer/reader pair hunting for a
//     torn multi-key commit (atomicity), and a counter under contention whose
//     expected total is known exactly (lost updates).
