# Transactional Key-Value Store (design classic)
# Difficulty: Medium base, Hard follow-ups
# Pattern: Stack of delta maps + a tombstone sentinel; undo log for true O(1) reads
#
#     set(key, value)
#     get(key) -> value | None
#     delete(key)
#     begin()      start a new (possibly nested) transaction
#     commit()     merge the current transaction into its parent, atomically
#     rollback()   discard the current transaction
#
# Reads inside a transaction see the in-progress writes of every open ancestor,
# falling back to the base store on a miss. commit/rollback with no open
# transaction is an error. All operations O(1) amortized.
#
# THE WORKED EXAMPLE FROM THE STATEMENT
#
#     set("a", 1)
#     begin()
#     set("a", 2)
#     get("a")     -> 2      the open transaction shadows the base store
#     begin()
#     set("a", 3)
#     rollback()             the inner frame is thrown away whole
#     get("a")     -> 2      back to what the outer transaction had written
#     commit()               outer frame merges into the base store
#     get("a")     -> 2
#
# ---------------------------------------------------------------------------
# THE MODEL: A TRANSACTION IS A DELTA, NOT A COPY
# ---------------------------------------------------------------------------
#
# The instinct is to snapshot the store on begin() and swap it back on rollback().
# That is correct and it is O(n) per begin, which is disqualifying -- a transaction
# that touches one key must not pay for the 10M keys it did not touch.
#
# So each open transaction holds only what it CHANGED:
#
#     base      {a: 1, b: 9}      committed state
#     frame 0   {a: 2}            outer transaction: "a is 2 now"
#     frame 1   {a: 3, b: DEL}    inner transaction: "a is 3, and b is gone"
#
#     get(k)   walk frames top-down, first frame that CONTAINS k wins; else base
#     set      write into the top frame (or straight into base if none is open)
#     delete   write a TOMBSTONE into the top frame
#     begin    push {}
#     commit   pop the top frame and fold it into the frame below (or into base)
#     rollback pop the top frame and drop it
#
# THE TRAP: DELETE NEEDS A TOMBSTONE, NOT A REMOVAL.
#
#     set("a", 1); begin(); delete("a"); get("a")
#
# `del frame["a"]` deletes nothing -- "a" lives in the base store, not in the
# frame -- and the get falls through and returns 1. Deleting from the BASE is
# worse: rollback() can no longer restore it, because the value is gone.
# A delete inside a transaction is a WRITE of "absent", so it needs a value that
# means absent, distinguishable from both "no opinion" (key not in the frame)
# and from a legitimately stored None. Hence a sentinel object, not None:
#
#     set("a", None) and delete("a") must not be the same thing, and
#     `if frame.get(k)` cannot tell 0, "", None and absent apart. Use
#     `if k in frame`, and compare against the sentinel with `is`.
#
# THE OTHER TRAP: TOMBSTONES MUST NOT REACH THE BASE STORE.
# Committing the outermost frame translates tombstones back into real deletions.
# Skip that and the base store starts handing out sentinel objects to callers.
#
# ---------------------------------------------------------------------------
# COMPLEXITY, AND THE PART MOST WRITE-UPS SKIP
# ---------------------------------------------------------------------------
#
#                       stack of deltas          undo log (Part 2)
#     get               O(depth)                 O(1)  <- always one dict lookup
#     set / delete      O(1)                     O(1)
#     begin             O(1)                     O(1)
#     rollback          O(1)                     O(k), amortized O(1)
#     commit            O(k), amortized O(1)     O(k), amortized O(1)
#
# k is the number of keys the transaction touched, and every one of them was
# paid for by a set/delete that already happened, so the amortized cost per
# operation is O(1) -- say that out loud, it is the whole answer to "is commit
# really O(1)?".
#
# get is the honest weak spot: walking the stack is O(depth), not O(1). With a
# nesting depth of 3 nobody cares, but "O(1) amortized" was in the requirements,
# so Part 2 gives the shape that actually delivers it: keep ONE dict that is
# always the current visible state, and push the OLD value onto an undo log
# instead of pushing the new value onto a stack. Reads stop searching entirely,
# and tombstones disappear -- deletion becomes a real deletion, and it is the
# undo entry that remembers the key used to exist.
#
# The catch, and the reason Part 1 is still the right base answer: the undo-log
# store mutates shared state before the commit, so its uncommitted writes are
# visible to everyone. It is a single-writer design. The delta stack keeps the
# base store clean until commit, which is exactly what makes the per-thread
# stacks in Part 3 work.
#
# WHAT TO ASK BEFORE WRITING CODE
#   1. Do transactions nest? (Assume yes -- it is the whole problem. A single
#      delta map plus a sentinel is the same code with a list of length 1.)
#   2. Does commit() close one level or all of them? One level here: an inner
#      commit publishes to the enclosing transaction, not to the world. Nothing
#      is durable until the outermost commit.
#   3. Can a value legitimately be None? If yes -- and it usually is -- get()
#      returning None cannot distinguish "missing" from "stored None", so the
#      sentinel matters internally and an `exists()`/`get(key, default)` may be
#      worth offering. get() -> None for both is what the statement asks for.
#   4. commit/rollback with nothing open: raise, or no-op? Raise (SQL does).
#   5. One store shared by many threads, or one per caller? That is Part 3, and
#      the answer decides the whole design -- ask before, not after.
#
# Time:  O(1) amortized for every operation (see the table).
# Space: O(distinct keys written across open transactions), on top of the base.

import os
import random
import threading
import time
from contextlib import contextmanager


class TransactionError(RuntimeError):
    """commit() or rollback() with no open transaction."""


class ConflictError(RuntimeError):
    """A concurrent transaction committed over a key this one had read."""


class _Sentinel:
    """A value that is not, and cannot be confused with, any value a caller
    could store -- including None."""

    __slots__ = ("_name",)

    def __init__(self, name):
        self._name = name

    def __repr__(self):
        return self._name


TOMBSTONE = _Sentinel("<deleted>")   # "this key is absent" written into a frame
MISSING = _Sentinel("<missing>")     # "there was no value here" in an undo log


# ---------------------------------------------------------------------------
# Part 1 -- the canonical answer: a stack of delta maps
# ---------------------------------------------------------------------------

class KeyValueStore:
    """Nested transactions over a dict. Each open transaction is one dict of
    changes; a read walks them newest-first and falls through to the base."""

    def __init__(self):
        self._base = {}       # committed state -- never holds a TOMBSTONE
        self._frames = []     # one delta dict per open transaction, outermost first

    # ---- reads ----

    def get(self, key):
        """Newest opinion wins. O(depth), and depth is the nesting level."""
        for frame in reversed(self._frames):
            if key in frame:                      # `in`, not .get() -- None is a value
                value = frame[key]
                return None if value is TOMBSTONE else value
        return self._base.get(key)

    def exists(self, key):
        """The distinction get() cannot make: stored None vs never stored."""
        for frame in reversed(self._frames):
            if key in frame:
                return frame[key] is not TOMBSTONE
        return key in self._base

    # ---- writes ----

    def set(self, key, value):
        if self._frames:
            self._frames[-1][key] = value
        else:
            self._base[key] = value               # autocommit

    def delete(self, key):
        if self._frames:
            self._frames[-1][key] = TOMBSTONE     # a WRITE of "absent"
        else:
            self._base.pop(key, None)             # no transaction: really delete

    # ---- transactions ----

    def begin(self):
        self._frames.append({})

    def commit(self):
        """Publish the top frame one level down. Atomic: the merge happens with
        no interleaving, so an observer never sees half a transaction."""
        if not self._frames:
            raise TransactionError("commit() with no open transaction")

        top = self._frames.pop()
        if self._frames:
            # Into the parent: tombstones stay tombstones. The parent is still
            # a transaction, and "deleted" is a fact it now owns and can undo.
            self._frames[-1].update(top)
        else:
            # Into the base store: tombstones become real deletions here, or the
            # base starts handing sentinel objects back to callers.
            for key, value in top.items():
                if value is TOMBSTONE:
                    self._base.pop(key, None)
                else:
                    self._base[key] = value

    def rollback(self):
        """Discard the top frame. Nothing to undo -- it was never applied."""
        if not self._frames:
            raise TransactionError("rollback() with no open transaction")
        self._frames.pop()

    # ---- conveniences ----

    @property
    def depth(self):
        return len(self._frames)

    @contextmanager
    def transaction(self):
        """with store.transaction(): ... -- commits on exit, rolls back on raise."""
        self.begin()
        depth = self.depth
        try:
            yield self
        except BaseException:
            if self.depth >= depth:
                self.rollback()
            raise
        else:
            self.commit()

    def snapshot(self):
        """The visible state, flattened. O(n) -- debugging and tests only."""
        view = dict(self._base)
        for frame in self._frames:
            for key, value in frame.items():
                if value is TOMBSTONE:
                    view.pop(key, None)
                else:
                    view[key] = value
        return view


# ---------------------------------------------------------------------------
# Part 2 -- true O(1) reads: keep the state current, log the way back
# ---------------------------------------------------------------------------

class UndoLogKeyValueStore:
    """Same API, opposite direction. One dict is always the current visible
    state, so get() never searches; each open transaction carries the old values
    needed to walk it backwards. This is how real engines do it (a rollback
    segment), and it is where the O(1) in "O(1) amortized" actually lands.

    Cost of the trade: writes land in the shared dict immediately, so this shape
    cannot host two concurrent transactions. See Part 3."""

    def __init__(self):
        self._store = {}      # ALWAYS the current visible state
        self._undo = []       # per open transaction: [(key, previous_value), ...]

    # ---- reads: one lookup, whatever the nesting depth ----

    def get(self, key):
        return self._store.get(key)

    def exists(self, key):
        return key in self._store

    # ---- writes: apply now, remember how to take it back ----

    def _record(self, key):
        if self._undo:
            # MISSING, not None: "the key was absent" and "the key held None"
            # must roll back to different states.
            self._undo[-1].append((key, self._store.get(key, MISSING)))

    def set(self, key, value):
        self._record(key)
        self._store[key] = value

    def delete(self, key):
        self._record(key)
        self._store.pop(key, None)     # a real deletion -- no tombstone needed

    # ---- transactions ----

    def begin(self):
        self._undo.append([])

    def commit(self):
        """The writes are already applied, so committing only decides who owns
        the right to undo them: the parent inherits the log, or -- at the
        outermost level -- it is dropped and the writes become permanent."""
        if not self._undo:
            raise TransactionError("commit() with no open transaction")

        top = self._undo.pop()
        if self._undo:
            self._undo[-1].extend(top)

    def rollback(self):
        """Replay the log BACKWARDS. Forwards restores intermediate values for
        any key written more than once -- an off-by-one that random testing
        catches instantly and a hand-written example usually misses."""
        if not self._undo:
            raise TransactionError("rollback() with no open transaction")

        for key, previous in reversed(self._undo.pop()):
            if previous is MISSING:
                self._store.pop(key, None)
            else:
                self._store[key] = previous

    @property
    def depth(self):
        return len(self._undo)

    def snapshot(self):
        return dict(self._store)


# ---------------------------------------------------------------------------
# Part 3 -- follow-up 1: many threads, one store
# ---------------------------------------------------------------------------

class ThreadSafeKeyValueStore:
    """A shared base store plus a PER-THREAD transaction stack.

    The insight is that the two halves of the state have completely different
    sharing requirements, and one lock over everything gets both of them wrong:

        the open frames   are private to one thread by definition -- an
                          uncommitted write is nobody else's business, so they
                          need no lock at all (threading.local, no contention)
        the base store    is shared, so it needs one, but only for the length of
                          a lookup or a commit-time merge

    So the lock is held for O(1) on a read and O(k) on a commit, and never while
    application code between begin() and commit() is running -- which is where
    all the time actually goes. Per-KEY locks on the base store are the tempting
    refinement and they are the wrong tool here: commit has to publish k keys
    atomically, so it would need all k locks at once, in a global order to avoid
    deadlock, and the moment two commits overlap on one key you are back to
    serializing anyway. Say that out loud; interviewers ask.

    Isolation: uncommitted writes are private, and a commit is atomic, so no
    thread ever sees half a transaction. Reads are not repeatable, though: two
    reads of the same key inside one transaction can differ if someone commits
    in between. detect_conflicts=True closes that hole -- see below."""

    def __init__(self, detect_conflicts=False):
        self._base = {}
        self._versions = {}                  # key -> commit counter, for validation
        self._lock = threading.Lock()        # guards _base and _versions, nothing else
        self._local = threading.local()      # per-thread frames, never shared
        self._detect_conflicts = detect_conflicts

    # Each frame is (delta, reads): what this transaction wrote, and the version
    # of every key it read straight from the base store.
    def _frames(self):
        frames = getattr(self._local, "frames", None)
        if frames is None:
            frames = self._local.frames = []
        return frames

    # ---- reads ----

    def get(self, key):
        frames = self._frames()

        for delta, _ in reversed(frames):
            if key in delta:                        # our own uncommitted write
                value = delta[key]
                return None if value is TOMBSTONE else value

        with self._lock:
            value = self._base.get(key)
            version = self._versions.get(key, 0)

        if frames:
            # First observation wins: validation asks "has this changed since I
            # first looked?", so a later re-read must not refresh the stamp.
            frames[-1][1].setdefault(key, version)
        return value

    # ---- writes ----

    def set(self, key, value):
        frames = self._frames()
        if frames:
            frames[-1][0][key] = value
        else:
            with self._lock:
                self._base[key] = value
                self._bump(key)

    def delete(self, key):
        frames = self._frames()
        if frames:
            frames[-1][0][key] = TOMBSTONE
        else:
            with self._lock:
                self._base.pop(key, None)
                self._bump(key)

    def _bump(self, key):
        self._versions[key] = self._versions.get(key, 0) + 1

    # ---- transactions ----

    def begin(self):
        self._frames().append(({}, {}))

    def commit(self):
        """Nested commit: hand the frame to the parent, no lock -- it is all
        thread-private. Outermost commit: take the lock once and publish."""
        frames = self._frames()
        if not frames:
            raise TransactionError("commit() with no open transaction")

        delta, reads = frames.pop()

        if frames:
            parent_delta, parent_reads = frames[-1]
            parent_delta.update(delta)
            for key, version in reads.items():
                parent_reads.setdefault(key, version)   # keep the EARLIER stamp
            return

        with self._lock:
            if self._detect_conflicts:
                stale = [key for key, version in reads.items()
                         if self._versions.get(key, 0) != version]
                if stale:
                    # The frame is already popped, so the transaction is gone --
                    # a failed commit aborts. Callers retry from begin().
                    raise ConflictError(
                        f"concurrent commit touched {sorted(stale)}")

            for key, value in delta.items():
                if value is TOMBSTONE:
                    self._base.pop(key, None)
                else:
                    self._base[key] = value
                self._bump(key)

    def rollback(self):
        frames = self._frames()
        if not frames:
            raise TransactionError("rollback() with no open transaction")
        frames.pop()

    @property
    def depth(self):
        return len(self._frames())

    @contextmanager
    def transaction(self):
        self.begin()
        depth = self.depth
        try:
            yield self
        except BaseException:
            if self.depth >= depth:
                self.rollback()
            raise
        else:
            self.commit()

    def snapshot(self):
        with self._lock:
            view = dict(self._base)
        for delta, _ in self._frames():
            for key, value in delta.items():
                if value is TOMBSTONE:
                    view.pop(key, None)
                else:
                    view[key] = value
        return view


def run_with_retry(store, body, attempts=50):
    """The other half of optimistic concurrency: detection is only useful if
    somebody retries. Returns body()'s result; re-runs it on ConflictError."""
    for attempt in range(attempts):
        try:
            with store.transaction():
                return body()
        except ConflictError:
            if attempt == attempts - 1:
                raise
            time.sleep(0.0005 * (attempt + 1))    # crude backoff


# ---------------------------------------------------------------------------
# Part 4 -- follow-up 2 (senior): the values live on disk
# ---------------------------------------------------------------------------

class ReadWriteLock:
    """Many readers or one writer. Writer-preferring: once a writer is waiting,
    arriving readers queue behind it, so a steady read load cannot starve a
    write. Not reentrant -- taking it twice on one thread deadlocks."""

    def __init__(self):
        self._cond = threading.Condition()
        self._readers = 0
        self._writer = False
        self._waiting_writers = 0

    @contextmanager
    def read(self):
        with self._cond:
            while self._writer or self._waiting_writers:
                self._cond.wait()
            self._readers += 1
        try:
            yield
        finally:
            with self._cond:
                self._readers -= 1
                if self._readers == 0:
                    self._cond.notify_all()

    @contextmanager
    def write(self):
        with self._cond:
            self._waiting_writers += 1
            while self._writer or self._readers:
                self._cond.wait()
            self._waiting_writers -= 1
            self._writer = True
        try:
            yield
        finally:
            with self._cond:
                self._writer = False
                self._cond.notify_all()


class KeyLockTable:
    """One lock per key, created on demand and reclaimed when idle.

    The refcount is the part that is easy to get wrong: a lock cannot be dropped
    from the table just because its holder released it, or a thread still queued
    on it ends up waiting on an object nobody else will ever see, and two threads
    "holding the same key" hold two different locks. Count the interested
    parties; delete only at zero."""

    def __init__(self):
        self._table_lock = threading.Lock()
        self._locks = {}                     # key -> [ReadWriteLock, refcount]

    @contextmanager
    def hold(self, key, exclusive):
        with self._table_lock:
            entry = self._locks.get(key)
            if entry is None:
                entry = self._locks[key] = [ReadWriteLock(), 0]
            entry[1] += 1
            lock = entry[0]

        try:
            with (lock.write() if exclusive else lock.read()):
                yield
        finally:
            with self._table_lock:
                entry = self._locks[key]
                entry[1] -= 1
                if entry[1] == 0:
                    del self._locks[key]

    def live_locks(self):
        with self._table_lock:
            return len(self._locks)


class DiskBackedStore:
    """Values are large blobs on disk; the in-memory part is only the index.

    THE PSEUDO-CODE THE INTERVIEWER IS ASKING FOR

        update(key, mutate):
            acquire EXCLUSIVE lock on key       <-- around the whole sequence,
            try:                                    not around each step
                blob = read_from_disk(key)
                blob = mutate(blob)
                write_to_disk(key, blob)
            finally:
                release lock on key

    The lock has to span read-modify-write. Lock the read and the write
    separately and two writers interleave as read/read/write/write: both compute
    from the same old blob and the second write silently eats the first. That is
    a lost update, and it is the bug this follow-up exists to find -- it is the
    same shape as the Part 3 conflict, one layer down.

    Reads take the lock SHARED, so concurrent readers do not serialize against
    each other -- which matters here precisely because reads are the expensive
    operation once values are blobs.

    Durability per key is the temp-file + atomic-rename trick: a reader either
    sees the whole old blob or the whole new one, never a torn file, even if the
    process dies mid-write. Note what that does NOT give you: atomicity across
    KEYS. A commit touching three blobs is three renames, and a crash between
    them leaves a half-applied transaction. Only a write-ahead log fixes that --
    see the notes."""

    def __init__(self, directory):
        self._dir = directory
        os.makedirs(self._dir, exist_ok=True)
        self._locks = KeyLockTable()
        self.disk_reads = 0                  # for the read-amplification demo
        self.disk_writes = 0

    def _path(self, key):
        safe = "".join(c if c.isalnum() or c in "-_." else f"%{ord(c):02x}"
                       for c in key)
        return os.path.join(self._dir, safe)

    # ---- unlocked primitives: the actual I/O ----

    def _read_blob(self, key):
        try:
            with open(self._path(key), "rb") as handle:
                self.disk_reads += 1
                return handle.read()
        except FileNotFoundError:
            return None

    def _write_blob(self, key, blob):
        path = self._path(key)
        temp = f"{path}.{os.getpid()}.{threading.get_ident()}.tmp"
        with open(temp, "wb") as handle:
            handle.write(blob)
            handle.flush()
            os.fsync(handle.fileno())        # the bytes, before the rename
        os.replace(temp, path)               # atomic: readers never see a torn file
        self.disk_writes += 1

    # ---- the public API, each holding the right lock ----

    def get(self, key):
        with self._locks.hold(key, exclusive=False):
            return self._read_blob(key)

    def set(self, key, blob):
        with self._locks.hold(key, exclusive=True):
            self._write_blob(key, blob)

    def delete(self, key):
        with self._locks.hold(key, exclusive=True):
            try:
                os.remove(self._path(key))
            except FileNotFoundError:
                pass

    def update(self, key, mutate):
        """Read-modify-write under ONE exclusive lock. The only safe way to
        compute a new blob from the old one."""
        with self._locks.hold(key, exclusive=True):
            blob = self._read_blob(key)
            new_blob = mutate(blob)
            if new_blob is None:
                try:
                    os.remove(self._path(key))
                except FileNotFoundError:
                    pass
            else:
                self._write_blob(key, new_blob)
            return new_blob

    def commit_many(self, writes):
        """Apply a transaction's whole delta. Locks are taken in SORTED key
        order: two commits sharing keys {a,b} would otherwise take them in
        opposite orders and deadlock. A global order over lock acquisition is
        the cheapest deadlock prevention there is, and it costs one sort.

        Still not atomic across keys -- see the class docstring."""
        held = []
        try:
            for key in sorted(writes):
                context = self._locks.hold(key, exclusive=True)
                context.__enter__()
                held.append(context)

            for key in sorted(writes):
                blob = writes[key]
                if blob is TOMBSTONE:
                    try:
                        os.remove(self._path(key))
                    except FileNotFoundError:
                        pass
                else:
                    self._write_blob(key, blob)
        finally:
            for context in reversed(held):
                context.__exit__(None, None, None)


# ---- Tests ----

def _reference_snapshot(ops):
    """The obvious, slow, obviously-correct model: copy the whole store on
    begin. O(n) per begin -- disqualifying as an answer, perfect as an oracle,
    because it shares no code with either real implementation."""
    stack = [{}]
    for op, key, value in ops:
        if op == "set":
            stack[-1][key] = value
        elif op == "delete":
            stack[-1].pop(key, None)
        elif op == "begin":
            stack.append(dict(stack[-1]))
        elif op == "commit" and len(stack) > 1:
            top = stack.pop()
            stack[-1] = top
        elif op == "rollback" and len(stack) > 1:
            stack.pop()
    return stack[-1]


def _apply(store, op, key, value):
    if op == "set":
        store.set(key, value)
    elif op == "delete":
        store.delete(key)
    elif op == "begin":
        store.begin()
    elif op == "commit":
        try:
            store.commit()
        except TransactionError:
            pass
    elif op == "rollback":
        try:
            store.rollback()
        except TransactionError:
            pass


if __name__ == "__main__":
    # --- the worked example from the statement ------------------------------
    print("== the worked example, step by step ==")
    for cls in (KeyValueStore, UndoLogKeyValueStore):
        store = cls()
        store.set("a", 1)
        store.begin()
        store.set("a", 2)
        assert store.get("a") == 2
        store.begin()
        store.set("a", 3)
        assert store.get("a") == 3
        store.rollback()
        assert store.get("a") == 2, "rollback must restore the OUTER value, not the base"
        store.commit()
        assert store.get("a") == 2
        assert store.depth == 0
        print(f"  {cls.__name__:<24} set/begin/set/begin/set/rollback/commit -> a = 2")

    # --- tombstones: the bug this problem is built around --------------------
    print()
    print("== tombstones ==")
    store = KeyValueStore()
    store.set("a", 1)
    store.begin()
    store.delete("a")
    assert store.get("a") is None, "the delete must shadow the base value"
    print("  delete inside a transaction hides the committed value ......... ok")
    store.rollback()
    assert store.get("a") == 1, "rollback must bring the deleted key back"
    print("  rollback brings it back (the base was never touched) .......... ok")

    store.begin()
    store.delete("a")
    store.commit()
    assert store.get("a") is None and "a" not in store._base, \
        "commit must translate the tombstone into a real deletion"
    print("  commit turns the tombstone into a real deletion ............... ok")

    # A stored None and a deleted key are different facts.
    store = KeyValueStore()
    store.set("a", None)
    assert store.get("a") is None and store.exists("a")
    store.begin()
    store.delete("a")
    assert store.get("a") is None and not store.exists("a")
    store.rollback()
    assert store.exists("a"), "rolling back a delete must restore the stored None"
    print("  set(k, None) and delete(k) stay distinguishable ............... ok")

    # Deleting a key that was only ever written inside the transaction.
    store = KeyValueStore()
    store.begin()
    store.set("a", 1)
    store.delete("a")
    assert store.get("a") is None
    store.commit()
    assert store.get("a") is None and "a" not in store._base
    print("  delete of a key created in the same transaction ............... ok")

    # --- the edge cases worth naming ----------------------------------------
    print()
    print("== edge cases ==")

    for cls in (KeyValueStore, UndoLogKeyValueStore):
        empty = cls()
        for method in ("commit", "rollback"):
            try:
                getattr(empty, method)()
            except TransactionError:
                pass
            else:
                raise AssertionError(f"{cls.__name__}.{method}() must raise when nothing is open")
    print("  commit/rollback with no open transaction raise ................ ok")

    # Nested rollback after a sibling commit: the committed sibling survives,
    # because it was folded into the parent before the second frame opened.
    store = KeyValueStore()
    store.set("x", 0)
    store.begin()
    store.begin()
    store.set("x", 1)
    store.commit()               # inner sibling commits into the outer frame
    store.begin()
    store.set("x", 2)
    store.rollback()             # second sibling is discarded
    assert store.get("x") == 1, "the committed sibling must survive the rollback"
    store.rollback()             # ...and now discard the parent too
    assert store.get("x") == 0, "rolling the parent back discards the sibling's commit"
    print("  nested rollback after a sibling commit ........................ ok")

    # get on a deleted key inside a nested transaction, three levels down.
    store = KeyValueStore()
    store.set("k", "base")
    store.begin()
    store.set("k", "outer")
    store.begin()
    store.delete("k")
    store.begin()
    assert store.get("k") is None, "the tombstone two frames up still shadows"
    store.set("k", "inner")
    assert store.get("k") == "inner"
    store.rollback()
    assert store.get("k") is None
    store.rollback()
    assert store.get("k") == "outer"
    print("  a tombstone shadows through deeper frames ..................... ok")

    # Very deep nesting: 10k frames, then unwind. The point is that begin() and
    # rollback() are O(1) -- a snapshot-per-begin design dies here.
    deep = KeyValueStore()
    deep.set("d", 0)
    for i in range(10_000):
        deep.begin()
        deep.set("d", i)
    assert deep.get("d") == 9_999
    for _ in range(5_000):
        deep.rollback()
    assert deep.get("d") == 4_999
    for _ in range(5_000):
        deep.commit()
    assert deep.get("d") == 4_999 and deep.depth == 0
    print("  10,000 nested transactions, unwound both ways ................. ok")

    # Same depth on the undo-log store, where reads do not care about depth.
    deep = UndoLogKeyValueStore()
    for i in range(10_000):
        deep.begin()
        deep.set("d", i)
    assert deep.get("d") == 9_999
    for _ in range(10_000):
        deep.rollback()
    assert deep.get("d") is None and deep.snapshot() == {}
    print("  ...and 10,000 rollbacks unwind to exactly the starting state .. ok")

    # The context manager, including the failure path.
    store = KeyValueStore()
    with store.transaction():
        store.set("committed", True)
    try:
        with store.transaction():
            store.set("aborted", True)
            raise ValueError("boom")
    except ValueError:
        pass
    assert store.snapshot() == {"committed": True} and store.depth == 0
    print("  with-block commits on exit, rolls back on exception ........... ok")

    # --- both implementations against the slow oracle ------------------------
    rng = random.Random(20260810)
    scripts = operations = 0

    for _ in range(3000):
        keys = ["a", "b", "c"]                    # tiny universe: forces collisions
        values = [0, 1, None, "x"]                # None is in there on purpose
        ops, depth = [], 0

        for _ in range(40):
            roll = rng.random()
            if roll < 0.40:
                ops.append(("set", rng.choice(keys), rng.choice(values)))
            elif roll < 0.60:
                ops.append(("delete", rng.choice(keys), None))
            elif roll < 0.80:
                ops.append(("begin", None, None))
                depth += 1
            elif roll < 0.90 and depth:
                ops.append(("commit", None, None))
                depth -= 1
            elif depth:
                ops.append(("rollback", None, None))
                depth -= 1

        stack_store, undo_store = KeyValueStore(), UndoLogKeyValueStore()
        prefix = []

        for op, key, value in ops:
            _apply(stack_store, op, key, value)
            _apply(undo_store, op, key, value)
            prefix.append((op, key, value))

            # The invariant has to hold after EVERY operation, not just at the end.
            expected = _reference_snapshot(prefix)
            assert stack_store.snapshot() == expected, (prefix, stack_store.snapshot(), expected)
            assert undo_store.snapshot() == expected, (prefix, undo_store.snapshot(), expected)
            for key_ in keys:
                assert stack_store.get(key_) == expected.get(key_)
                assert undo_store.get(key_) == expected.get(key_)
                assert stack_store.exists(key_) == (key_ in expected)
                assert undo_store.exists(key_) == (key_ in expected)

        # Unwind whatever is still open: rolling everything back must land on
        # the state as of the outermost begin().
        while stack_store.depth:
            stack_store.rollback()
            undo_store.rollback()
        assert stack_store.snapshot() == undo_store.snapshot()
        assert all(v is not TOMBSTONE for v in stack_store._base.values()), \
            "a tombstone escaped into the base store"

        scripts += 1
        operations += len(ops)

    print()
    print("== both implementations vs. a copy-on-begin oracle ==")
    print(f"  {scripts:,} random scripts, {operations:,} operations, checked after every step:")
    print("    stack-of-deltas snapshot == oracle")
    print("    undo-log snapshot        == oracle")
    print("    get/exists agree with the oracle for every key, at every depth")
    print("    no TOMBSTONE ever reaches the base store")

    # --- follow-up 1: threads ------------------------------------------------
    print()
    print("== follow-up 1: concurrent transactions ==")

    shared = ThreadSafeKeyValueStore()
    shared.set("shared", "committed")
    barrier = threading.Barrier(2)
    observed = {}

    def isolated(name, value):
        shared.begin()
        shared.set("shared", value)
        shared.set(name, value)
        barrier.wait()                      # both threads now hold uncommitted writes
        observed[name] = (shared.get("shared"), shared.get("other"))
        barrier.wait()
        shared.rollback()

    threads = [threading.Thread(target=isolated, args=(n, v))
               for n, v in (("t1", "one"), ("t2", "two"))]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert observed["t1"] == ("one", None), observed
    assert observed["t2"] == ("two", None), observed
    assert shared.snapshot() == {"shared": "committed"}, shared.snapshot()
    print("  each thread saw only its own uncommitted writes ............... ok")
    print("  both rolled back; the base store is untouched ................. ok")

    # A commit is atomic: no thread ever observes half of one.
    atomic = ThreadSafeKeyValueStore()
    atomic.set("a", 0)
    atomic.set("b", 0)
    torn = []
    stop = threading.Event()

    def writer():
        for i in range(1, 400):
            atomic.begin()
            atomic.set("a", i)
            atomic.set("b", i)
            atomic.commit()
        stop.set()

    def reader():
        while not stop.is_set():
            atomic.begin()                  # read both under one transaction
            a, b = atomic.get("a"), atomic.get("b")
            atomic.rollback()
            if a != b:
                torn.append((a, b))

    hands = [threading.Thread(target=writer), threading.Thread(target=reader)]
    for t in hands:
        t.start()
    for t in hands:
        t.join()
    assert not torn, f"observed a partially applied commit: {torn[:3]}"
    assert atomic.snapshot() == {"a": 399, "b": 399}
    print("  1 writer / 1 reader, 399 two-key commits: no torn read ........ ok")

    # Lost update: the anomaly the base design still allows, and the fix.
    def counter_run(store, threads_count=8, per_thread=40, retry=False):
        store.set("count", 0)

        def bump():
            for _ in range(per_thread):
                if retry:
                    def body():
                        store.set("count", store.get("count") + 1)
                    run_with_retry(store, body)
                else:
                    store.begin()
                    store.set("count", store.get("count") + 1)
                    time.sleep(0)           # widen the window; no sleep still races
                    store.commit()

        workers = [threading.Thread(target=bump) for _ in range(threads_count)]
        for w in workers:
            w.start()
        for w in workers:
            w.join()
        return store.get("count"), threads_count * per_thread

    lossy, expected_total = counter_run(ThreadSafeKeyValueStore())
    assert lossy <= expected_total
    print(f"  read-modify-write across 8 threads, no detection: {lossy}/{expected_total}"
          f" ({expected_total - lossy} lost)")

    safe, expected_total = counter_run(ThreadSafeKeyValueStore(detect_conflicts=True),
                                       retry=True)
    assert safe == expected_total, f"expected {expected_total}, got {safe}"
    print(f"  same load with conflict detection + retry:        {safe}/{expected_total} (exact)")
    print("  detection is version validation at commit: 'has anything I READ")
    print("  changed since I read it?'. Cheap, and it needs a retry loop to be useful.")

    # A conflict really does abort, deterministically.
    occ = ThreadSafeKeyValueStore(detect_conflicts=True)
    occ.set("k", 1)
    occ.begin()
    assert occ.get("k") == 1                # stamp the read

    def interloper():
        occ.set("k", 99)                    # different thread -> different frames
    other = threading.Thread(target=interloper)
    other.start()
    other.join()

    occ.set("k", 2)
    try:
        occ.commit()
    except ConflictError:
        pass
    else:
        raise AssertionError("commit over a concurrently modified read must conflict")
    assert occ.get("k") == 99 and occ.depth == 0, "a failed commit must abort the transaction"
    print("  a commit over a concurrently modified read aborts ............. ok")

    # --- follow-up 2: blobs on disk -----------------------------------------
    print()
    print("== follow-up 2: values are blobs on disk ==")

    import shutil
    import tempfile

    scratch = tempfile.mkdtemp(prefix="txn_kv_")
    try:
        disk = DiskBackedStore(scratch)
        disk.set("blob", b"0")
        assert disk.get("blob") == b"0"
        assert disk.get("nothing") is None

        # 8 threads, 50 read-modify-writes each, all on ONE key. Every increment
        # must survive, because update() holds the key's write lock across the
        # whole read/modify/write.
        def hammer():
            for _ in range(50):
                disk.update("blob", lambda old: str(int(old or b"0") + 1).encode())

        crew = [threading.Thread(target=hammer) for _ in range(8)]
        for t in crew:
            t.start()
        for t in crew:
            t.join()
        assert disk.get("blob") == b"400", disk.get("blob")
        print("  8 threads x 50 read-modify-writes on one key -> 400 ......... ok")
        print("  (the same loop as get()+set() loses updates -- the lock has to")
        print("   span the read AND the write, which is the point of the follow-up)")

        # Concurrent readers do not serialize: the lock is shared for reads.
        readers_seen = []

        def read_many():
            for _ in range(100):
                readers_seen.append(disk.get("blob"))

        crew = [threading.Thread(target=read_many) for _ in range(4)]
        for t in crew:
            t.start()
        for t in crew:
            t.join()
        assert all(v == b"400" for v in readers_seen)
        print("  4 concurrent readers, shared locks, consistent blobs ........ ok")

        # Multi-key commit: locks taken in sorted order, so overlapping commits
        # from two threads cannot deadlock.
        def commit_pair(a_value, b_value):
            for _ in range(100):
                disk.commit_many({"alpha": a_value, "beta": b_value})

        crew = [threading.Thread(target=commit_pair, args=(b"A", b"B")),
                threading.Thread(target=commit_pair, args=(b"a", b"b"))]
        for t in crew:
            t.start()
        for t in crew:
            t.join(timeout=30)
        assert not any(t.is_alive() for t in crew), "deadlock in commit_many"
        assert disk.get("alpha") in (b"A", b"a") and disk.get("beta") in (b"B", b"b")
        print("  two threads, overlapping 2-key commits, no deadlock ......... ok")

        disk.delete("alpha")
        assert disk.get("alpha") is None

        # The lock table does not leak: every lock is refcounted and dropped.
        assert disk._locks.live_locks() == 0, disk._locks.live_locks()
        print("  the per-key lock table is empty again once nobody holds one .. ok")

        # Read amplification, made concrete.
        before = disk.disk_reads
        for _ in range(20):
            disk.get("blob")
        print(f"  20 gets of one unchanged blob -> {disk.disk_reads - before} disk reads"
              " (this is the problem)")
        print("  Every logical read is a physical read. A hot key served from a")
        print("  cache of decoded blobs turns 20 reads into 1; see the notes for")
        print("  what makes that hard once transactions are in the picture.")
    finally:
        shutil.rmtree(scratch, ignore_errors=True)

    print()
    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "Is commit really O(1)?"
#     Per operation, amortized, yes. A commit moves k entries, but each of those
#     entries was created by a set/delete that already paid O(1), so charge the
#     move to the write that created it and every operation is O(1) amortized.
#     Worst-case per-call it is O(k), and that is unavoidable: publishing k
#     writes atomically has to touch k things. What you can avoid is O(n) -- the
#     copy-on-begin design in _reference_snapshot -- and that is the real
#     distinction the requirement is testing.
#
# "get() walks the stack -- that is O(depth), not O(1)."
#     Correct, and worth conceding immediately rather than defending. Three
#     answers, in order of how much they cost you: (a) depth is a program-
#     structure constant, not a data size, so O(depth) is O(1) in practice;
#     (b) the undo-log store in Part 2, where the visible state is always
#     materialised and reads are one lookup; (c) keep a key -> version-stack
#     index alongside the frames, so a read is one hash lookup into a list whose
#     top is the answer -- same O(1) read, and rollback pops one entry per key
#     it wrote. (c) is what you build if you need Part 2's read cost AND Part 1's
#     clean base store at the same time.
#
# "Why not deep-copy the store on begin?"
#     O(n) per begin and O(n) memory per open transaction, for a transaction
#     that usually writes one key. It is also the wrong shape for concurrency:
#     a copy has to be reconciled at commit, so you end up rebuilding the delta
#     anyway to know what changed. Persistent/immutable maps (HAMTs) make the
#     copy O(1) with structural sharing, and are the honest answer for a
#     functional-language version -- worth naming, not worth writing here.
#
# "Why one global lock on the base store rather than per-key locks?"
#     Because commit is the operation that matters and it is inherently
#     multi-key: publishing k writes atomically under per-key locks means
#     holding k locks at once, acquired in a global order to avoid deadlock, and
#     released together. That is strictly more machinery than one lock and it
#     only wins when commits rarely overlap. The scaling answer is somewhere in
#     between: stripe the base store into N shards, each with its own lock, and
#     a commit takes the shards it needs in index order. N=64 turns one
#     contention point into 64 without changing the code shape. Per-key locks do
#     become right at the disk layer (Part 4), because there the critical
#     section is long -- it contains I/O -- and it is usually single-key.
#
# "What isolation level is this?"
#     Part 3 as written gives atomicity and no dirty reads, but reads are not
#     repeatable: a key read twice inside one transaction can change underneath
#     you, which is exactly how the lost-update demo loses counts. Two ways up:
#     OPTIMISTIC (implemented) -- stamp every key you read with its version,
#     validate at commit, abort on mismatch, retry; cheap under low contention,
#     livelock-prone under high. PESSIMISTIC -- take a shared lock on read and
#     an exclusive one on write, hold to commit (two-phase locking), which gives
#     serializability at the price of deadlocks and a deadlock detector. MVCC is
#     the third door: keep a version chain per key and hand each transaction a
#     consistent snapshot as of its start, which is what Postgres does and what
#     makes readers never block writers.
#
# "The value blobs are huge -- what changes?"
#     Keep BLOBS OUT OF THE FRAMES. A frame should hold a handle (path, offset,
#     content hash), never bytes, so begin/commit/rollback stay proportional to
#     the number of keys touched and never to the size of the data. Then a
#     transaction writes new blobs to fresh files and commit swaps pointers,
#     which is copy-on-write: rollback is deleting the orphans, and a background
#     GC reclaims anything unreferenced. This also makes commit cheap again,
#     because it moves pointers, not megabytes.
#
# "Read amplification."
#     Four sources worth naming, in the order they usually bite:
#       1. Every logical read is a physical read. Fix: an LRU cache of decoded
#          blobs. The transactional wrinkle is invalidation -- a commit must
#          evict the keys it wrote BEFORE releasing the lock, or a reader
#          repopulates the cache with the pre-commit value and it stays wrong.
#       2. Read-modify-write of one field pulls the whole blob. Fix: chunk the
#          blob and address chunks, or keep an append-only delta log per key and
#          fold it in at compaction time.
#       3. LSM-structured storage answers one read from several levels. Fix:
#          per-level bloom filters so the misses cost nothing, plus compaction.
#       4. Deep transaction stacks multiply the miss path if frames hold blobs
#          rather than handles -- which is (again) why they should hold handles.
#     The number to reason about is bytes read per byte returned; state the
#     baseline out loud before proposing a fix, because half of these make write
#     amplification worse in exchange.
#
# "What if the disk write fails halfway through a commit?"
#     Per key, temp-file + fsync + atomic rename means a reader sees the whole
#     old blob or the whole new one -- never a torn file. ACROSS keys there is
#     no such guarantee: three renames, a crash after the second, and the
#     transaction is half applied. The fix is a write-ahead log:
#
#         1. append BEGIN + one record per intended write to the log; fsync
#         2. append COMMIT to the log; fsync    <-- the transaction is now durable
#         3. apply the writes to the data files, in any order
#         4. mark the log entry applied; the log can be truncated
#
#     Recovery replays the log: any transaction with a COMMIT record is re-
#     applied (idempotently -- that is why the records carry the full new value
#     or a content hash), any transaction without one is discarded. The commit
#     point is a single fsync of a single append, which is also why WAL is
#     faster than the thing it protects: one sequential write replaces k random
#     ones. Group commit -- batching several transactions' fsyncs into one --
#     is the next question after that.
#
# "How would you test it?"
#     The block above is the answer, and the structure is the point: a slow
#     copy-on-begin oracle that shares no code with either implementation, run
#     against random scripts drawn from a deliberately tiny key universe (3 keys,
#     4 values, one of which is None) and checked after EVERY operation, not just
#     at the end. That catches all four classic bugs at once -- the missing
#     tombstone, the tombstone escaping into the base store, the rollback that
#     replays its undo log forwards, and set(k, None) being confused with
#     delete(k). Concurrency needs different tools: a barrier to pin two threads
#     at the interesting instant (isolation), a writer/reader pair looking for a
#     torn multi-key commit (atomicity), and a counter under contention where the
#     expected total is known exactly (lost updates).
