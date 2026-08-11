# Pattern: Stack & Queue

## Key Techniques
- **Monotonic stack** — maintain increasing or decreasing order; pop when invariant breaks
- **Stack for matching** — push open brackets/elements, pop and verify on closing
- **Stack of frames** — one entry per open bracket holds the state to restore when it closes (nested parsing/decoding)
- **Min-tracking stack** — pair each element with the current min at that depth
- **Compile the frame, don't replay it** — when a body's effect has a closed form, store the closed form; composing two is O(1) where re-executing is exponential
- **Frame = (accumulator, pending operator)** — a parser frame holds the value built so far plus the one operator still waiting for its operand; a nested sub-expression is then not a special case, it is an operand the same loop happens to compute
- **Lazy deletion (tombstones)** — a heap can only address its root, so don't delete: mark dead and discard at pop. Each entry is pushed once and popped once, so it stays O(log n) amortized
- **Epoch/generation stamp** — invalidate every stale copy of a key with one O(1) counter bump instead of n deletions; execute, cancel and supersede all become the same operation

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Valid Parentheses | #20 | Easy | Stack matching |
| Daily Temperatures | #739 | Medium | Monotonic stack (decreasing) |
| Min Stack | #155 | Medium | Stack of (val, current_min) pairs |
| Largest Rectangle in Histogram | #84 | Hard | Monotonic stack (increasing) |
| Decode String | #394 | Medium | Stack of pending frames |
| Math Interpreter with Function Definitions | interpreter classic | Medium (Hard follow-up) | Command state machine; compile each body to an affine map |
| Priority Task Executor | design classic | Medium (Hard follow-ups) | Heap + lazy deletion; epoch stamp for the skip rule |
| String-Command Calculator | parser classic | Easy (Medium follow-up) | Tokenize; stack of (accumulator, pending op) frames |

## Pattern Cheat Sheet

```python
# Monotonic decreasing stack (next greater element)
stack = []  # stores indices
result = [-1] * len(nums)
for i, val in enumerate(nums):
    while stack and nums[stack[-1]] < val:
        idx = stack.pop()
        result[idx] = val
    stack.append(i)

# Valid parentheses template
stack = []
pairs = {')': '(', '}': '{', ']': '['}
for ch in s:
    if ch in pairs:
        if not stack or stack[-1] != pairs[ch]:
            return False
        stack.pop()
    else:
        stack.append(ch)
return not stack

# Nested decoding: one stack frame per open bracket
counts, parents, current = [], [], []
for ch in s:
    if ch.isdigit():
        k = k * 10 + int(ch)      # multi-digit counts
    elif ch == '[':               # descend: park the parent state
        counts.append(k); parents.append(current)
        k, current = 0, []
    elif ch == ']':               # ascend: fold the finished segment in
        segment = current
        current = parents.pop()
        current.append(''.join(segment) * counts.pop())
    else:
        current.append(ch)

# Interpreter with function definitions: an ADD/MUL body is an AFFINE map
# x -> a*x + b, and affine maps compose, so a whole function -- however long,
# however many other functions it calls -- collapses to one pair (a, b).
#
#   ADD k     -> (1, k)          MUL k -> (k, 0)
#   then      -> (a2*a1, a2*b1 + b2)      # apply (a1,b1), then (a2,b2)
#
# Every command yields one step; there are only two places to put it.
for op, arg in commands:
    if op == 'FUN':   frames.append([arg, (1, 0)]); continue   # stack -> nesting is free
    if op == 'END':   name, body = frames.pop(); funcs[name] = body; continue
    step = (1, arg) if op == 'ADD' else (arg, 0) if op == 'MUL' else funcs[arg]
    if frames: frames[-1][1] = compose(frames[-1][1], step)     # defining: COMPOSE
    else:      x = step[0] * x + step[1]                        # else:     APPLY

# Replaying stored bodies instead is O(2^depth): f_k calling f_(k-1) twice is
# 4k commands and 2^k operations. Compiling makes every INV two multiplies.
# Compiling also binds EARLY -- a redefined f cannot reach into a g that already
# baked it in. Overflow is not a problem if the ints wrap: composition is a ring
# identity, so it holds exactly in Z/2^64. Recursion or IF kills the closed form
# (not affine) and forces you back to interpreting.

# "ADD 2", "MULT 3", ... against one accumulator, left to right, where an operand
# may itself be a parenthesized command list. Reading left to right, a frame is
# only ever waiting for one of two things, so a frame is (accumulator, pending op)
# and there are exactly four token kinds:
#
#   operator -> record as pending    (frame must be idle)
#   number   -> apply, clear pending (frame must be pending)
#   '('      -> push a frame         (frame must be pending)
#   ')'      -> pop; the popped accumulator IS the operand
#
# That last line is the whole nesting follow-up -- a sub-expression is an operand
# the same loop computes. Recursive descent writes the identical thing on the call
# stack: shorter in the room, dies on deeply nested input.
frames = [[start, None]]                       # [accumulator, pending op]
for tok in tokens:
    acc, pending = frames[-1]
    if tok == '(':      frames.append([SUB_SEED, None])          # pending required
    elif tok == ')':    frames.pop(); apply(frames[-1], acc)     # pop, then fold in
    elif tok in OPS:    frames[-1][1] = tok                      # must be idle
    else:               apply(frames[-1], int(tok))              # must be pending

# Ask FOUR things before writing code, all of them silent in the prompt:
#   1. start at 0 or 1?      0 is ADD's identity, not MULT's -- MULT-first stays 0
#   2. what is DIV?          exact / truncate-toward-zero / floor: -7 DIV 2 is
#                            -7/2, -3, or -4, and all three are defensible
#   3. sub-expression seed?  fresh 0, or inherit the enclosing accumulator
#   4. precedence?           left-to-right needs no parser; precedence makes this
#                            a shunting-yard / precedence-climbing problem instead
#
# Over the RATIONALS all four ops are affine, acc -> a*acc + b:
#   ADD k -> (1, k)   SUB k -> (1, -k)   MULT k -> (k, 0)   DIV k -> (1/k, 0)
# so with fresh sub-expression seeds the whole program collapses to one (a, b) --
# cache it and a repeated sub-expression costs O(1). Both preconditions bite:
# inheriting the seed makes MULT (expr) compute acc*(a*acc+b), which is quadratic,
# and integer DIV is not linear. Either one, and you are back to interpreting.

# Priority queue where the same key can be added many times but runs ONCE:
# do NOT try to remove the dead copies. A heap can only address its root, so
# deleting an interior entry is O(n). Leave them in and drop them at the pop --
# the one moment they are addressable and the one moment liveness matters.
#
#   epoch[id]  = how many times id has been retired (executed or cancelled)
#   an entry is live  <=>  the epoch it was pushed under is still current
#
# One O(1) bump kills every sibling copy, and execute / cancel / supersede all
# collapse into that same bump.
heap, epoch, executed = [], {}, set()

def add_task(task_id, priority, timestamp):
    seq = next(counter)                       # ties: priority, timestamp, then insertion
    if task_id in executed: return            # once-ever: dead on arrival (a fast path,
                                              # NOT the mechanism -- the pop check is)
    heapq.heappush(heap, (-priority, timestamp, seq, task_id, epoch.get(task_id, 0)))

def execute_task():
    while heap:
        *_, task_id, stamp = heapq.heappop(heap)
        if stamp != epoch.get(task_id, 0): continue        # tombstone: skip it
        executed.add(task_id)
        epoch[task_id] = stamp + 1                         # retires every sibling copy
        return task_id
    return None

# Re-adding is a free DECREASE-key: both copies sit in the heap and the better
# one surfaces first. It is not a SET-priority -- a worse re-add changes nothing,
# so lowering a priority means cancel-then-add. peek() must prune the tombstones
# above the first live entry, so peek MUTATES the heap.
#
# Cost: O(log m) amortized -- each entry is pushed once and popped at most once.
# Memory: O(total adds), and the tombstones can outlive every future pop, so
# compact when they pass ~50% of the heap, or switch to an indexed heap (one
# entry per key + a position map) for O(distinct) memory and O(log n) worst case.
```
