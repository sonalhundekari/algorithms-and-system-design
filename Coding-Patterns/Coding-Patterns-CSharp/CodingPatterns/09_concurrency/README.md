# Pattern: Concurrency

## Key Techniques
- **Locking for atomicity** — a single lock (or a lock per key/bucket) protects the read-modify-write sequences that would otherwise race under concurrent callers
- **Monotonic clocks over wall clocks** — `Stopwatch` / `time.monotonic()` never jump backwards or forwards (NTP sync, DST, manual clock changes), which matters for anything measuring elapsed time like rate limits or TTLs
- **Background sweep thread (active expiration)** — a timer/thread periodically reclaims state (idle buckets, expired keys) so memory stays bounded even if nobody ever touches that key again
- **Lazy expiration** — a read that lands on stale/expired state reclaims it on the spot, so callers never see data past its TTL even between sweeps
- **HashMap + doubly linked list for O(1) LRU** — same structure as the LRU Cache pattern, reused here to evict the least-recently-used entry once a cache is over capacity
- **Thread-private state needs no lock** — split the state by who can see it: per-thread transaction frames live in a `ThreadLocal` with zero contention, and the lock covers only the shared committed store, held for a lookup or a merge rather than for the whole transaction
- **Optimistic concurrency (version validation)** — stamp every key a transaction reads with its version, re-check the stamps at commit, abort and retry on a mismatch; turns a silent lost update into a visible conflict, and is useless without the retry loop
- **Per-key locks with refcounted reclamation** — one `ReaderWriterLockSlim` per key, created on demand and removed only when nobody holds or waits on it; acquire multi-key sets in a global (sorted) order so overlapping acquisitions cannot deadlock
- **Sliding-window log, one queue per rule** — keep only the *accepted* timestamps still inside each rule's window; expire from the front, then admit only if every rule has room. Amortized O(1) per rule per request, and memory is capped at the rule's own limit no matter how long the stream is
- **Provisional reservation + refund** — quota is taken inside the lock so concurrent callers see it, but the handler runs *outside* the lock (never hold a limiter lock across user code); if the handler throws, refund the reservation so a failed request consumes no quota
- **Delay queue for throttled work** — park rejected work keyed by its next eligible time instead of dropping it, and re-run *every* rule when it wakes, since elapsed time and refunds change capacity independently
- **Event-time policy for out-of-order arrivals** — clamp, reject, or bound the lateness; feeding a late timestamp straight into a FIFO window breaks the "front is oldest" invariant and silently corrupts every later count
- **Atomic file replace for durability** — write a temp file, `Flush(flushToDisk: true)`, then rename over the target, so a reader sees the whole old value or the whole new one; atomic per file, which is exactly why multi-key commits still need a write-ahead log

## Problems

| Problem | Difficulty | Technique |
|---------|------------|-----------|
| Rate Limiter | Medium | Token bucket per client + background idle-bucket cleanup |
| Rate Limiter — Dropped Times | Medium (Hard follow-up) | One sliding-window log per rule over **accepted** timestamps; lock-guarded check+reserve, refund on handler failure, delay queue for throttled work |
| Redis Cache | Medium | HashMap + doubly linked list (LRU) + TTL expiration, thread-safe |
| Redis Rate Limiter | Hard | Token bucket, state shared in Redis + atomic Lua script (EVAL) for multi-server correctness |
| Transactional KV Store | Hard | Stack of delta maps + tombstone sentinel; undo log for O(1) reads; per-thread frames + version validation; per-key disk locks |

## Pattern Cheat Sheet

```python
# Lock-guarded read-modify-write (avoids lost updates under concurrency)
with lock:
    value = read_current_state()
    new_value = compute(value)
    write_new_state(new_value)

# Lazy + active expiration for TTL-based state
def get(key):
    with lock:
        entry = store.get(key)
        if entry is None:
            return None
        if entry.expires_at <= time.monotonic():
            del store[key]      # lazy expiration
            return None
        return entry.value

def sweep_loop():                # active expiration, runs on a background thread
    while not stop.wait(interval):
        with lock:
            for key, entry in list(store.items()):
                if entry.expires_at <= time.monotonic():
                    del store[key]
```

```csharp
// Nested transactions: one delta map per open transaction, newest opinion wins.
// Delete is a WRITE of "absent" (a sentinel), never a removal -- removing from
// the base store makes the rollback impossible.
object Get(string key)
{
    for (int i = frames.Count - 1; i >= 0; i--)
        if (frames[i].TryGetValue(key, out var v))          // ContainsKey, not "is null"
            return ReferenceEquals(v, Tombstone) ? null : v;
    return baseStore.TryGetValue(key, out var c) ? c : null;
}

void Commit()                                                // O(k), amortized O(1)
{
    var top = Pop();
    if (frames.Count > 0)
        foreach (var (k, v) in top) frames[^1][k] = v;        // tombstones stay tombstones
    else
        foreach (var (k, v) in top)                           // ...and become real removals
            if (ReferenceEquals(v, Tombstone)) baseStore.Remove(k); else baseStore[k] = v;
}

// Sliding-window rate limit: expire EVERY window first, then let every rule
// vote, then append. Only ACCEPTED timestamps go in -- pushing a dropped
// request throttles traffic that was never served.
for (int i = 0; i < rules.Count; i++)
    while (windows[i].Count > 0 && windows[i].Peek() <= t - rules[i].WindowSeconds)
        windows[i].Dequeue();

if (rules.Select((r, i) => windows[i].Count < r.MaxRequests).All(ok => ok))
    foreach (var w in windows) w.Enqueue(t);                  // accepted
else
    dropped.Add(t);                                           // appended nowhere

// Online: check + reserve inside ONE lock, handler OUTSIDE it, refund on throw.
if (!limiter.TryAcquire(out var reservation)) { Park(payload); return; }
try { handler(payload); }                                     // slow handler blocks nobody
catch { limiter.Refund(reservation); throw; }                 // failure consumes no quota

// Read-modify-write on disk: ONE exclusive lock around read + modify + write.
// Locking them separately interleaves as read/read/write/write -- a lost update.
using (locks.Hold(key, exclusive: true))
{
    var blob = ReadFromDisk(key);
    WriteToDisk(key, Mutate(blob));
}
```
