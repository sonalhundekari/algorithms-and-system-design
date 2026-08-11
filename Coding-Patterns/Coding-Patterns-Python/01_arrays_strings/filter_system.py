# Filter System with a Dynamic Blacklist (design classic)
# Difficulty: Easy base, Medium follow-up
# Pattern: HashSet membership + a derived visibility predicate; emit on its edges
#
# A stream of integers arrives one at a time. A blacklist ("the filter") is
# maintained alongside it and can change at any moment. Three methods:
#
#     process_filter(value)   add or remove `value` from the filter
#     process_input(value)    one value arrives on the input stream
#     emit(value)             the system's output hook -- you call it
#
# BASE RULE. When a value arrives via process_input and is not currently in the
# filter, call emit(value). The whole base problem is one set:
#
#     def process_input(self, v):
#         if v not in self.filter:
#             self.emit(v)
#
# FOLLOW-UP. Add an update_flag and re-emit on filter edits:
#
#     process_filter(update_flag, value)
#     emit(update_flag, value)
#
#     emit(False, v)  an already-seen value has become newly BLOCKED
#     emit(True,  v)  a blocked-but-already-seen value has become VISIBLE again
#
# The published trace, in time order, is the entire specification:
#
#     process_filter   +1   +2        -1   +3
#     process_input           1   3
#     emit                              +3   +1   -3
#
# Decoded event by event -- worth doing out loud, because the trace is terse
# enough that two people can read it two different ways:
#
#     process_filter(+1)   filter={1}      nothing seen yet, no emit
#     process_filter(+2)   filter={1,2}    nothing seen yet, no emit
#     process_input(1)     seen={1}        1 IS filtered -> swallowed, no emit
#     process_input(3)     seen={1,3}      3 is not filtered -> emit(True, 3)
#     process_filter(-1)   filter={2}      1 seen and now visible -> emit(True, 1)
#     process_filter(+3)   filter={2,3}    3 seen and now blocked -> emit(False, 3)
#
#     emits: +3, +1, -3    -- exactly the trace
#
# THE TRAP: THE FLAG MEANS OPPOSITE THINGS ON THE WAY IN AND ON THE WAY OUT.
#
#     process_filter(True, v)   ADD v to the filter   -> v becomes INVISIBLE
#     emit(True, v)             v is VISIBLE
#
# so process_filter(True, 3) produces emit(False, 3). The inversion is forced by
# the trace, not chosen, and writing `self.emit(update_flag, value)` is the most
# likely bug in this problem. Confirm the polarity before writing a line.
#
# THE SECOND THING THE TRACE PINS DOWN. Input 1 arrived while 1 was filtered, so
# it was never emitted -- and un-filtering it emits it anyway. "Seen" therefore
# means EVER ARRIVED ON THE INPUT, not "was previously emitted". A swallowed
# value is still remembered. Get this wrong and the +1 in the trace disappears.
#
# THE MODEL. Two independent bits per value and one derived predicate:
#
#     seen(v)      v has appeared on the input at least once   (never un-set)
#     blocked(v)   v is in the filter right now                (toggles freely)
#
#     visible(v) = seen(v) and not blocked(v)
#
# `visible` is exactly what the emit stream reports: the last emit for a value is
# its current visibility. Both mutating methods then have the same three-line
# body -- read visible, flip one bit, emit iff visible flipped -- and there are
# no special cases, because every case in the statement is one edge of that
# predicate:
#
#     seen False->True while unblocked   -> emit(True, v)    first sighting
#     blocked False->True while seen     -> emit(False, v)   newly blocked
#     blocked True->False while seen     -> emit(True, v)    visible again
#     anything at all while not seen(v)  -> silence          nobody was ever told
#
# WHAT TO ASK BEFORE WRITING CODE
#   1. Does the base process_filter(value) TOGGLE, or is it add-only? A one-arg
#      mutator that must do both can only be a toggle -- and the follow-up bolting
#      a flag onto it is good evidence the base really is one. Toggle here, with
#      set_filter(flag, value) exposed so callers can skip tracking parity.
#   2. Repeated input of a currently visible value: emit again, or stay quiet?
#      The base rule ("emit when it arrives and is not filtered") is per-arrival;
#      the follow-up is phrased in terms of state CHANGES, which is per-transition.
#      The trace never repeats a value, so it cannot settle it. Both are
#      implemented below; per-arrival is the default because it is what the base
#      rule literally says.
#   3. Is a redundant filter edit -- adding what is already filtered -- an event?
#      No. No state change, no emit. Falls out of the model for free.
#   4. Can emit re-enter the system? State is mutated BEFORE emit is called, so a
#      callback that turns around and calls process_input sees a consistent object.
#
# Time:  O(1) expected per call, all three methods.
# Space: O(distinct values filtered + distinct values seen). The seen set only
#        grows -- see the notes at the bottom for what to do about that.

import random

EVERY_ARRIVAL = "every_arrival"      # emit on every unfiltered arrival, repeats included
ON_STATE_CHANGE = "on_state_change"  # emit only when visibility actually flips


# ---------------------------------------------------------------------------
# Part 1 -- the base problem
# ---------------------------------------------------------------------------

class FilterSystem:
    """Emit a value when it arrives and is not currently blacklisted."""

    def __init__(self, sink=None):
        self.filter = set()
        self.output = []          # everything emit() was called with, in order
        self._sink = sink         # optional external hook

    def process_filter(self, value):
        """Toggle `value`'s membership in the filter."""
        if value in self.filter:
            self.filter.remove(value)
        else:
            self.filter.add(value)

    def set_filter(self, update_flag, value):
        """Explicit form: True adds to the filter (blocks), False removes."""
        if update_flag:
            self.filter.add(value)
        else:
            self.filter.discard(value)

    def process_input(self, value):
        if value not in self.filter:
            self.emit(value)

    def emit(self, value):
        """The system's output hook. Override to send emissions somewhere real."""
        self.output.append(value)
        if self._sink:
            self._sink(value)


# ---------------------------------------------------------------------------
# Part 2 -- the follow-up
# ---------------------------------------------------------------------------

class FilterSystemWithUpdates:
    """Filter edits replay their effect on values that have already appeared, so
    the emit stream always describes the current visibility of every value the
    system knows about."""

    def __init__(self, policy=EVERY_ARRIVAL, sink=None):
        self.filter = set()       # blocked right now
        self.seen = set()         # ever arrived on the input
        self.output = []          # list of (visible, value)
        self._policy = policy
        self._sink = sink

    def is_visible(self, value):
        """visible(v) = seen(v) and not blocked(v) -- the only thing either
        mutator has to watch."""
        return value in self.seen and value not in self.filter

    def process_filter(self, update_flag, value):
        """True adds to the filter, False removes. Note the inversion: adding to
        the filter emits False."""
        was_visible = self.is_visible(value)

        if update_flag:
            self.filter.add(value)
        else:
            self.filter.discard(value)

        # Covers all three "no event" cases at once: a redundant edit (visibility
        # unchanged), and any edit to a value never seen (is_visible is False on
        # both sides, because seen(v) is False).
        if self.is_visible(value) != was_visible:
            self.emit(not was_visible, value)

    def process_input(self, value):
        was_visible = self.is_visible(value)

        self.seen.add(value)                    # one-way: a value is never un-seen

        if not self.is_visible(value):
            return                              # filtered -- swallowed, but remembered

        # Visible now. Either this is the flip (first sighting while unfiltered),
        # or it was already visible and the policy decides whether to repeat.
        if not was_visible or self._policy == EVERY_ARRIVAL:
            self.emit(True, value)

    def emit(self, update_flag, value):
        """The system's output hook. Override to send emissions somewhere real."""
        self.output.append((update_flag, value))
        if self._sink:
            self._sink(update_flag, value)


# ---------------------------------------------------------------------------
# Part 3 -- an independent reference model, for cross-checking
# ---------------------------------------------------------------------------

def replay_reference(ops):
    """Recompute the emit stream the slow, obvious way: after every operation take
    a full snapshot of which values are visible and diff it against the previous
    snapshot. Pure state-change semantics (ON_STATE_CHANGE).

    Shares no logic with the incremental implementation, which is what makes
    agreement between them evidence. `ops` is a list of ("filter", flag, value)
    or ("input", None, value)."""
    universe = sorted({value for _, _, value in ops})
    filtered, seen, emits = set(), set(), []

    def snapshot():
        return {v: v in seen and v not in filtered for v in universe}

    before = snapshot()

    for kind, flag, value in ops:
        if kind == "filter":
            if flag:
                filtered.add(value)
            else:
                filtered.discard(value)
        else:
            seen.add(value)

        after = snapshot()
        emits.extend((after[v], v) for v in universe if after[v] != before[v])
        before = after

    return emits


def collapse(emits):
    """Drop each emit that repeats the previous verdict for the same value.
    Collapsing an EVERY_ARRIVAL stream must reproduce the ON_STATE_CHANGE stream
    exactly -- the two policies differ only by redundant repeats."""
    last, kept = {}, []
    for visible, value in emits:
        if last.get(value) is visible:
            continue
        last[value] = visible
        kept.append((visible, value))
    return kept


# ---- Tests ----
def _show(emits):
    return " ".join(f"{'+' if v else '-'}{x}" for v, x in emits) or "(nothing)"


def _script(steps, policy=EVERY_ARRIVAL):
    system = FilterSystemWithUpdates(policy)
    steps(system)
    return system.output


if __name__ == "__main__":
    # --- base version -------------------------------------------------------
    basic = FilterSystem()
    basic.process_input(7)       # nothing filtered yet -> emits
    basic.process_filter(7)      # toggle on
    basic.process_input(7)       # swallowed
    basic.process_input(8)       # emits
    basic.process_filter(7)      # toggle off again
    basic.process_input(7)       # emits
    basic.process_input(7)       # emits AGAIN -- per-arrival, by the base rule
    assert basic.output == [7, 8, 7, 7], basic.output

    # --- the published trace, event by event --------------------------------
    traced = FilterSystemWithUpdates()
    script = [
        ("process_filter(+1)", lambda f: f.process_filter(True, 1)),
        ("process_filter(+2)", lambda f: f.process_filter(True, 2)),
        ("process_input(1)", lambda f: f.process_input(1)),
        ("process_input(3)", lambda f: f.process_input(3)),
        ("process_filter(-1)", lambda f: f.process_filter(False, 1)),
        ("process_filter(+3)", lambda f: f.process_filter(True, 3)),
    ]

    print("== the published trace, event by event ==")
    consumed = 0
    for label, step in script:
        step(traced)
        fresh, consumed = traced.output[consumed:], len(traced.output)
        print(f"  {label:<20} filter={sorted(traced.filter)}"
              f"  seen={sorted(traced.seen)}  emit: {_show(fresh)}")

    assert traced.output == [(True, 3), (True, 1), (False, 3)], traced.output
    print(f"  full emit stream: {_show(traced.output)}   (expect +3 +1 -3)")
    print("  +1 is the interesting one: 1 arrived while filtered, so it was never")
    print("  emitted -- and un-filtering it emits it anyway. 'Seen' is not 'was emitted'.")

    # --- the polarity inversion ---------------------------------------------
    polarity = _script(lambda f: (f.process_input(5),
                                  f.process_filter(True, 5),
                                  f.process_filter(False, 5)))
    assert polarity == [(True, 5), (False, 5), (True, 5)], polarity
    print()
    print("== the polarity inversion ==")
    print(f"  input(5), filter(True, 5), filter(False, 5) -> {_show(polarity)}")
    print("  process_filter(TRUE, 5) emitted FALSE. Adding to the filter hides the")
    print("  value; the emit flag reports visibility. Passing the flag through is wrong.")

    # --- every edge case worth naming ---------------------------------------
    cases = [
        ("filtered then unfiltered, never input",
         lambda f: (f.process_filter(True, 4), f.process_filter(False, 4)), []),
        ("unfilter a value nobody ever filtered",
         lambda f: f.process_filter(False, 4), []),
        ("filter a value twice (redundant edit)",
         lambda f: (f.process_input(4), f.process_filter(True, 4), f.process_filter(True, 4)),
         [(True, 4), (False, 4)]),
        ("unfilter twice after a real block",
         lambda f: (f.process_input(4), f.process_filter(True, 4),
                    f.process_filter(False, 4), f.process_filter(False, 4)),
         [(True, 4), (False, 4), (True, 4)]),
        ("input BEFORE it is filtered",
         lambda f: (f.process_input(4), f.process_filter(True, 4)),
         [(True, 4), (False, 4)]),
        ("input AFTER it is filtered",
         lambda f: (f.process_filter(True, 4), f.process_input(4)), []),
        ("blocked input, then unblocked",
         lambda f: (f.process_filter(True, 4), f.process_input(4), f.process_filter(False, 4)),
         [(True, 4)]),
        ("repeated input, EVERY_ARRIVAL (default)",
         lambda f: (f.process_input(4), f.process_input(4), f.process_input(4)),
         [(True, 4), (True, 4), (True, 4)]),
    ]

    print()
    print("== every edge case worth naming ==")
    for label, steps, expected in cases:
        actual = _script(steps)
        assert actual == expected, (label, actual, expected)
        print(f"  {label:<44}{_show(actual)}")

    repeats = _script(lambda f: (f.process_input(4), f.process_input(4), f.process_input(4)),
                      ON_STATE_CHANGE)
    assert repeats == [(True, 4)], repeats
    print(f"  {'repeated input, ON_STATE_CHANGE':<44}{_show(repeats)}")

    def _cycles(f):
        f.process_input(4)
        for _ in range(3):
            f.process_filter(True, 4)
            f.process_filter(False, 4)

    cycles = _script(_cycles)
    assert cycles == [(True, 4), (False, 4)] * 3 + [(True, 4)], cycles
    print(f"  {'three block/unblock cycles after one input':<44}{_show(cycles)}")
    print("  A value the system has never seen on the input is invisible to filter")
    print("  edits, however many of them there are -- there is no one to tell.")

    # --- the invariant, checked continuously --------------------------------
    rng = random.Random(20260810)
    trials = ops_run = 0

    for _ in range(2000):
        universe = 1 + rng.randrange(4)      # tiny on purpose: collisions hide the bugs
        ops = []
        for _ in range(20):
            value = rng.randrange(universe)
            ops.append(("input", None, value) if rng.random() < 0.5
                       else ("filter", rng.random() < 0.5, value))

        every = FilterSystemWithUpdates(EVERY_ARRIVAL)
        changes = FilterSystemWithUpdates(ON_STATE_CHANGE)

        for kind, flag, value in ops:
            for system in (every, changes):
                if kind == "filter":
                    system.process_filter(flag, value)
                else:
                    system.process_input(value)

            # The invariant has to hold after EVERY operation, not just at the end.
            for system in (every, changes):
                last = {}
                for visible, v in system.output:
                    last[v] = visible
                for v in range(universe):
                    if v in last:
                        assert last[v] == system.is_visible(v), (ops, v)
                    else:
                        assert not system.is_visible(v), (ops, v)

        reference = replay_reference(ops)
        assert changes.output == reference, (ops, changes.output, reference)
        assert collapse(every.output) == reference, (ops, every.output, reference)

        # ON_STATE_CHANGE must never say the same thing twice in a row about a value.
        previous = {}
        for visible, v in changes.output:
            assert previous.get(v, not visible) != visible, (ops, v)
            previous[v] = visible

        trials += 1
        ops_run += len(ops)

    print()
    print("== the invariant, checked continuously ==")
    print("  For every value: the LAST thing emitted about it == seen(v) and not blocked(v).")
    print(f"  {trials:,} random scripts, {ops_run:,} operations -- all of:")
    print("    on_state_change == snapshot-diff reference")
    print("    collapse(every_arrival) == on_state_change")
    print("    last emit per value == its current visibility, after every operation")
    print("    a never-input value is never visible and never emitted")
    print("    on_state_change never repeats a verdict for a value")

    # --- the base version is the follow-up with the replay switched off ------
    for _ in range(1000):
        basic, full, expected = FilterSystem(), FilterSystemWithUpdates(), []
        for _ in range(20):
            value = rng.randrange(4)
            if rng.random() < 0.5:
                basic.process_input(value)
                full.process_input(value)
                if full.is_visible(value):
                    expected.append(value)
            else:
                flag = rng.random() < 0.5
                basic.set_filter(flag, value)
                full.process_filter(flag, value)
        assert basic.output == expected, (basic.output, expected)

    print()
    print("  base output == the follow-up's input-triggered emissions, always.")
    print("  Same predicate, two different amounts of memory: the follow-up's whole")
    print("  cost is the seen set -- and that is also its only scaling problem.")

    # --- the output hook is a hook ------------------------------------------
    routed = []
    wired = FilterSystemWithUpdates(
        sink=lambda visible, value: routed.append(f"{'show' if visible else 'hide'} {value}"))
    wired.process_input(9)
    wired.process_filter(True, 9)
    assert routed == ["show 9", "hide 9"], routed

    print()
    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "The stream is unbounded -- the seen set grows forever."
#     It does, and it is the follow-up's only real cost: the base version holds
#     |filter| entries, the follow-up holds |filter| + |distinct inputs ever|. You
#     cannot drop a value from `seen` on correctness grounds, because any value can
#     be filtered at any future moment and would then owe an emit. The honest
#     answers all weaken the contract instead:
#       - Bound the memory and the promise together: keep `seen` as an LRU of the
#         last N distinct values and document that filter edits only replay over a
#         recent window. Says what it does, and it is what a real system wants.
#       - A Bloom filter is tempting and is the wrong shape: false positives mean
#         emitting state changes for values that never arrived, and inventing
#         output is worse than dropping it.
#       - If values are dense and bounded, two bitsets beat two hash sets on both
#         space and cache behaviour, with the code unchanged.
#
# "process_filter should take a RANGE, or a predicate, not a single value."
#     The follow-up that actually changes the algorithm. A single-value edit
#     touches one value, so the replay is O(1); a range edit must find every SEEN
#     value inside the range. Keep `seen` sorted and the replay costs O(log n + k)
#     for the k values it reports, which is output-optimal. An arbitrary predicate
#     cannot be indexed and forces an O(|seen|) scan -- the point to make out loud
#     is that the emit count is the lower bound, so you want a structure whose scan
#     cost is proportional to the number of emits, not to the size of the state.
#
# "Filters as counts, not booleans (several owners each filtering a value)."
#     Replace the filter set with a Counter. Blocked means count > 0, so an emit
#     fires only on the 0->1 and 1->0 crossings and nothing else changes. Worth
#     raising unprompted: a toggle-based API breaks the moment two subsystems both
#     filter the same value -- the second toggle un-filters it.
#
# "Make it thread-safe."
#     Two separable problems. The state is two sets, so one lock covers the
#     mutations; the hard part is that emissions must stay ORDERED and
#     non-overlapping, or a consumer sees +v / -v out of order and believes the
#     wrong thing forever, since nothing ever re-sends the truth. Emit under the
#     lock (simple, and emit had better be fast), or take a sequence number under
#     the lock and let one drain thread emit in sequence order.
#
# "Persist / restore the system."
#     `seen` plus `filter` is the entire state, both plain sets. The interesting
#     question is what a reconnecting subscriber should be told: a snapshot of
#     visible(v) for every seen v, not a replay of the emit log. That is the
#     level- vs edge-triggered distinction the policy flag already makes concrete.
#
# "How would you test it?"
#     The block above is the answer. The strongest check is not a fixture, it is
#     the invariant: for every value, the LAST emit about it must equal
#     seen(v) and not blocked(v), asserted after every single operation on random
#     scripts drawn from a deliberately tiny value universe. That one property
#     catches the polarity inversion, the missing seen set, the redundant-edit
#     emit, and the un-filtering of a never-seen value -- every bug this problem
#     has -- and it is a sentence long.
