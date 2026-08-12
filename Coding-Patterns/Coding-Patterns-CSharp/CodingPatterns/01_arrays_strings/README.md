# Pattern: Arrays & Strings

## Key Techniques
- **HashMap / HashSet** — O(1) lookups for frequency counts, seen elements
- **Derived predicate, emit on its edges** — when output is driven by several independent bits of state, name the predicate they combine into and report only when it *flips*; every "when does this fire?" case collapses into one edge check
- **Two Pointers** — left/right pointers moving toward each other
- **Sliding Window** — variable or fixed window that expands/shrinks
- **Sorting** — enables grouping, binary search, or greedy approaches
- **Anchor at the changed cell** — after a single edit, only lines *through* that cell can be new; probing 4 axes from it is O(1) where a full-board rescan is O(m·n) **and answers a weaker question** ("a win exists" ≠ "this move won")
- **Validate a final state by peeling the last move** — "is this reachable?" needs no search: check the counts, then ask whether *one* cell lies on **every** winning line, since the last move is one cell and it had to end the game. Removing marks can never *create* a line, so a line-free board with legal counts unwinds for free
- **Inverted index** — when a query names no key, invert the map (`key → values` becomes `value → keys`) so an AND is a set intersection and an OR a union; cost then tracks the *rarest* term instead of the corpus size
- **Intervals instead of marks** — when each hit paints a `±k` window, painting costs `O(n·k)` and rewrites the same cells; emit `[i-k, i+k]` and merge into the previous interval instead for `O(n)`. Hits are found in ascending order, so the intervals arrive pre-sorted — *there is no sort*
- **Ring buffer + commit index for streaming context** — trailing context is an integer (`emitThrough`), leading context is a `k`-deep queue, and duplicates are killed by a monotone `nextEmit` counter rather than per-line flags: `O(k)` memory, `O(k)` latency
- **Match state is one integer** — "does pattern P end here?" is answered by the *matched prefix length*, and nothing else. Streaming exact-match reduces to advancing that integer per element; `j = fail[j-1]` on both mismatch **and** completion (reset-to-0 on completion silently drops overlapping occurrences). One integer per pattern is the flat case; a shared trie with suffix links is the same idea merged (Aho-Corasick)

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Two Sum | #1 | Easy | HashMap |
| Calculate Amount Paid in Taxes | #2303 | Easy | Banded linear sweep (carry prev boundary) |
| Best Time to Buy and Sell Stock | #121 | Easy | Single pass / min tracker |
| Reverse Alphanumeric Segments | #917 (variant) | Easy | Two pointers scoped to each maximal run |
| Group Anagrams | #49 | Medium | HashMap + sort key |
| Rotating the Box | #1861 | Medium | Two pointers (gravity) + index remap |
| Connect Four — Can Play Win | — | Medium | 4-axis two-sided run count anchored at the placed cell |
| Valid Tic-Tac-Toe State (N×N, K in a row) | #794 (extended) | Medium (Hard extension) | Count invariants + intersect the winner's K-windows via run cores |
| RecordCollection Sort Colors | #75 (variant) | Medium | Dutch National Flag, via a GetColor/Swap object API |
| Longest Substring Without Repeating Characters | #3 | Medium | Sliding window |
| Find All Anagrams in a String | #438 | Medium | Fixed-size sliding window + `matches` counter |
| Filter System with Dynamic Blacklist | design classic | Easy (Medium follow-up) | Two HashSets; emit on the edges of `seen && !blocked` |
| Document Store with Predicate Query | design classic | Easy (Medium follow-ups) | Dictionary of HashSets + sum-of-products predicate + inverted index |
| Grep With Context Lines | design classic | Medium (4 parts) | Mark array → ring buffer (streaming) → merged intervals → map-reduce over chunks |
| Recipe as Contiguous Ingredient Subsequence | #28 (multi-pattern variant) | Medium (2 follow-ups) | KMP → prefix rolling hash → two pointers (O(1) space) → per-recipe match state → Aho-Corasick |
| Minimum Window Substring | #76 | Hard | Sliding window + frequency map |

## Pattern Cheat Sheet

```
# Sliding Window template
left = 0
for right in range(len(s)):
    # expand window
    while <window invalid>:
        # shrink from left
        left += 1
    # update answer

# FIXED-size window: no shrink loop. Every step adds one and drops one, so the
# window size is an invariant, not something you maintain. (Contrast Minimum
# Window Substring, where the window is variable and the while loop shrinks it.)
for right in range(len(s)):
    add(s[right])
    left = right - k
    if left >= 0: remove(s[left])       # only starts evicting once it overgrows
    if left >= -1: check_answer(left + 1)

# Anagram windows: ONE diff array = need[c] - window[c], plus a `matches` count
# of how many letters are at diff 0. All 26 at zero <=> anagram. Update it on
# the EDGES of the touched letter and each step is O(1), not O(26):
if diff[c] == 0: matches -= 1           # was correct, about to break
diff[c] -= 1                            # (+= 1 when evicting instead)
if diff[c] == 0: matches += 1           # just became correct
# Traps:
#   - seed `matches` with the 26-|distinct(p)| letters that start at zero, or
#     it can never reach 26.
#   - the two edge checks must bracket the mutation; checking only after it
#     misses the letter that just left zero.
#   - guard len(p) > len(s) up front -- the eviction index goes negative.

# Banded sweep over sorted boundaries (progressive tax, tiered pricing, histograms)
# Carry the PREVIOUS boundary; each band only owns the slice above it.
prev = 0
for upper, rate in bands:
    if amount <= prev: break          # amount exhausted, rest of the bands are empty
    total += (min(amount, upper) - prev) * rate
    prev = upper

# Gravity / compaction toward one end, with blockers (falling stones, sliding tiles)
# Carry `write` = lowest free landing slot. Blockers reset it; segments between
# blockers then compact on their own, no explicit segment boundaries needed.
write = n - 1
for j in range(n - 1, -1, -1):
    if row[j] == BLOCKER:
        write = j - 1
    elif row[j] == MOVABLE:
        row[write] = MOVABLE
        if write != j: row[j] = EMPTY
        write -= 1

# Dutch National Flag: partition into three groups in ONE pass, in place.
# [0,low)=lows  [low,mid)=mids  [mid,high]=unknown  (high,n)=highs
low = mid = 0; high = n - 1
while mid <= high:
    c = get(mid)
    if   c == LOW:  swap(low, mid); low += 1; mid += 1
    elif c == MID:  mid += 1
    else:           swap(mid, high); high -= 1   # do NOT advance mid
# O(n) because the unknown window (high - mid + 1) shrinks by exactly one every
# iteration -- no branch leaves both pointers still. Re-reading mid is free.

# Segment-scoped two pointers: transform each maximal run, separators are FIXED.
# The scoping is the whole point -- confining the swap to [i, j) is what keeps
# separators at their original index, no post-check needed.
i = 0
while i < n:
    if not is_member(s[i]):
        i += 1                      # fixed point, scan past it
    else:
        j = i
        while j < n and is_member(s[j]): j += 1   # [i, j) is the run
        reverse(s, i, j - 1)                      # or any in-run transform
        i = j                                     # resume past the run

# Walls vs holes -- the distinction that decides the answer:
#   walls (segment-scoped): "ab-cd" -> "ba-dc"   nothing crosses a separator
#   holes (LeetCode 917):   "ab-cd" -> "dc-ba"   members reverse as ONE global list
# For holes, run left/right over the whole string and skip non-members instead.

# Rotate a grid 90 deg clockwise: row i -> column m-1-i, m x n -> n x m
#   rotated[j][m - 1 - i] = grid[i][j]
# Rotation only relabels coordinates, so do the physics in whichever
# orientation is row-wise (cache-friendly), then rotate.

# Emit on the EDGES of a derived predicate (dynamic blacklist / feed filter).
# Two independent bits per key, one predicate they combine into:
#     seen(v)     v has arrived at least once      (one-way)
#     blocked(v)  v is filtered right now          (toggles)
#     visible(v) = seen(v) and not blocked(v)
# Then EVERY mutator is the same three lines, and the case analysis vanishes:
was = visible(v)
flip_one_bit()
if visible(v) != was: emit(not was, v)       # not `was` == the new state
# Two traps worth saying out loud:
#   - the flag inverts. Adding to the filter (True) makes the value INVISIBLE,
#     so it emits False. Passing update_flag straight through to emit is wrong.
#   - "seen" means EVER ARRIVED, not "was emitted". A value swallowed while
#     filtered must still be remembered, or un-filtering it emits nothing.
# Test it with the invariant, not with fixtures: for every value, the LAST emit
# about it == visible(v), asserted after every single operation.

# Boolean predicate over terms: `a && b || c` with no parens and && binding
# tighter is ALWAYS a sum of products -- an OR of AND-groups. So there is no
# parser: one pass where && extends the current group and || opens a new one.
groups = [[]]                        # a && b || c  ->  [[a, b], [c]]
expect_term = True
for tok in text.split():             # whitespace tokens, NOT text.split("||")
    if expect_term:                  # a bare word, else malformed
        groups[-1].append(validate(tok))
    elif tok == "||": groups.append([])
    elif tok != "&&": raise Malformed
    expect_term = not expect_term
if expect_term: raise Malformed      # ended on an operator
match = any(all(t in words for t in g) for g in groups)
# Three traps:
#   - split on the LOOSER operator first. Splitting on && first computes
#     a && (b || c) && d -- wrong precedence, and it agrees with the right
#     answer on most small inputs, so a quick manual test will not catch it.
#   - tokenize on whitespace, not on the operator strings: split("||") happily
#     accepts `a b` as one term and `a ||` as a term plus an empty one.
#   - "no such document" != "document does not match" -- ERROR vs false.

# Inverted index: answering a query that names no document.
# forward:  filename -> {words}     one named doc, O(terms)      -> CheckContains
# inverted: word     -> {filenames} no named doc, no full scan   -> GetAllFiles
for group in groups:                          # OR of ANDs, over posting sets
    postings = [index[t] for t in group]      # a MISSING term = EMPTY posting
    if any absent: continue                   # ...it kills the whole group
    postings.sort(key=len)                    # rarest term drives the work
    hits |= intersect(postings)
# The bug: `if t in index: postings.append(index[t])` silently drops unknown
# terms from the AND, so `apple && nonsense` returns every apple document.
# Keep an insertion-rank map if results must come back in insertion order --
# iterating the forward map to order them puts the corpus back into the cost.

# grep -C: print every match plus k lines around it, each line ONCE, in order.
# The whole problem is one predicate:  keep(j) <=> some i in [j-k, j+k] matches.
# So the output is a UNION OF WINDOWS -- dedupe by INDEX, never by line text
# (identical lines at different indices are different lines).
#
# Part 1, mark and sweep -- O(n*k) writes, the bool[] IS the dedupe and the order:
for i, line in enumerate(lines):
    if target in line:
        for j in range(max(0, i - k), min(n, i + k + 1)): keep[j] = True
#
# Part 2, streaming -- O(k) memory, O(k) latency (a line is only ruled OUT k
# lines later). Trailing context needs no buffer, just an index you commit to:
ring = deque(maxlen=k)          # leading context
next_emit = 0                   # dedupe: lowest index not yet emitted -- NO flags
emit_through = -1               # trailing context: print everything up to here
for i, line in enumerate(stream):
    if target in line:
        emit_through = i + k
        base = i - len(ring)
        for off, buffered in enumerate(ring):            # replay leading context
            if base + off >= max(next_emit, i - k): emit(base + off, buffered)
        emit(i, line)
    elif i <= emit_through:
        emit(i, line)
    ring.append(line)
#
# Part 3, intervals -- O(n): overlapping windows rewrite the same cells, so
# merge instead of mark. Matches ascend => intervals arrive sorted => NO SORT.
if ranges and start <= ranges[-1].end + 1:                # +1 merges ADJACENT too
    ranges[-1].end = max(ranges[-1].end, end)             # (that's what makes a
else: ranges.append(Range(start, end))                    #  `--` separator right)
# Same complexity, less machinery: a DIFFERENCE ARRAY. delta[start] += 1,
# delta[end+1] -= 1, prefix sum > 0 means keep. Intervals still win when the
# output is sparse -- the final loop touches only the lines actually printed.
#
# Part 4, multithreading -- map-reduce over CONTIGUOUS chunks:
#   map     worker c scans [start_c, end_c) and returns MERGED INTERVALS, with
#           windows clamped to the WHOLE document so they may overhang the chunk
#   reduce  concatenate per-chunk lists IN CHUNK ORDER and merge -> O(m), no sort
# Returning intervals (not text) is what makes the chunk boundary a non-event:
# a match on a chunk's first line needs context the previous worker owns.
# Results go in results[c], one slot per chunk -- no lock, and reading slots in
# id order is why the output is deterministic rather than luckily ordered.
# Traps: i + k overflows int when k is huge (clamp in 64-bit); and in C#
# `line.IndexOf(target)` is CULTURE-sensitive -- pass StringComparison.Ordinal.

# Did THIS move complete a line of K? (Connect Four, gomoku, tic-tac-toe)
# Anchor at the placed cell -- a line it does not touch was already there.
# FOUR AXES, each walked BOTH ways; the placed piece is shared, so add 1 once.
AXES = ((0, 1), (1, 0), (1, 1), (1, -1))     # -, |, \, /
def run(board, r, c, dr, dc, who, K):        # cap at K-1: more cannot change it
    n = 0
    for step in range(1, K):
        nr, nc = r + dr*step, c + dc*step
        if not (0 <= nr < len(board) and 0 <= nc < len(board[nr])): break
        if board[nr][nc] != who: break
        n += 1
    return n
def wins(board, x, y, who, K=4):
    return any(1 + run(board, x, y, dr, dc, who, K)
                 + run(board, x, y, -dr, -dc, who, K) >= K
               for dr, dc in AXES)
# The four bugs, in the order people hit them:
#   - eight directions instead of four axes (double-counts every line)
#   - forgetting the +1, or adding it in both half-runs
#   - scanning forward only -- "a a . a" is a win when you fill the GAP
#   - `== K` instead of `>= K` -- five in a row is still a win
# O(1) per move (<= 4*2*(K-1) cells), vs O(m*n) for a rescan that answers the
# WEAKER question "does a win exist". Gravity variant: the caller gives a COLUMN
# and the row is derived (lowest empty), so keep a per-column height for O(1).

# Exact CONTIGUOUS match of a pattern list inside a text list ("is this recipe a
# contiguous run of these ingredients?"). Substring search with str for char --
# NOT a subsequence and NOT a multiset containment. Duplicates in the text are
# the whole difficulty, so any HashSet-of-ingredients answer is a wrong question.
#
# O(1) EXTRA SPACE (no failure array): two pointers, and ONE line matters --
i = j = 0
while i < n:
    if text[i] == pat[j]:
        i += 1; j += 1
        if j == m: return i - m
    else:
        i = i - j + 1; j = 0      # BACK THE TEXT UP to start+1. `i += 1` here is
        # the bug that looks like an optimization: text [a,a,a,b], pat [a,a,b] --
        # 2 matched, 3rd failed, resuming at index 3 skips the LIVE window at 1.
# O(n*m) worst case, and the backup is exactly what KMP precomputes away.
#
# KMP -- the text pointer NEVER moves backwards. fail[i] = longest proper
# prefix of pat[0..i] that is also a suffix. Built by running pat against ITSELF
# with the identical loop:
k = 0
for i in range(1, m):
    while k > 0 and pat[i] != pat[k]: k = fail[k-1]
    if pat[i] == pat[k]: k += 1
    fail[i] = k
# Search is the same three lines with `text[i]` in place of `pat[i]`.
# NOT O(1) space -- fail is O(m). The two follow-ups genuinely conflict; the
# resolution (Two-Way / Galil-Seiferas, what glibc memmem uses) is worth NAMING
# and not worth writing.
#
# STREAMING, many patterns: state per pattern is ONE INTEGER, the matched prefix
# length. Ingredients are dropped as they arrive.
for r, pat in enumerate(pats):
    j = state[r]
    while j > 0 and x != pat[j]: j = fail[r][j-1]
    if x == pat[j]: j += 1
    if j == m: emit(r, pos - m + 1); j = fail[r][j-1]   # NOT j = 0 -- resetting
    state[r] = j    # drops OVERLAPPING hits: [a,b,a] in a b a b a matches 0 AND 2
# O(R) per element. Aho-Corasick merges the R chains into one trie + suffix links
# for O(1) per element: build trie, BFS the links (a link always points shallower,
# so it is final by the time you need it), and give each node an OUTPUT LINK to
# the nearest terminal ancestor -- that is what lets [sugar,egg] and [egg] both
# report at the same position. A node owns a LIST of pattern ids (duplicates).
# Add/remove patterns live => stay with the flat version; adding one pattern
# invalidates suffix links across the whole trie.
#
# MULTI-PATTERN via ROLLING HASH: prefix hashes make any window O(1) --
#   pre[i+1] = pre[i]*B + id(text[i]);  hash(i,L) = pre[i+L] - pre[i]*B^L
# Bucket patterns by LENGTH, sweep windows once per distinct length, look up.
# O(n*D + total), D = distinct lengths -- NOT O(n + total); that is Aho-Corasick.
# Non-negotiable: mod 2^61-1 (Mersenne => reduction is shifts), RANDOM base (a
# fixed B makes collisions constructible), and VERIFY the hit element-by-element
# so a collision costs time instead of correctness.
#
# Which to reach for is decided by which side is stable:
#   patterns stable, text changes  -> Aho-Corasick (preprocess patterns)
#   text stable, patterns change   -> suffix automaton over the text: O(n) build,
#                                     then O(m) per query with NO n dependence

# IS THIS FINISHED BOARD REACHABLE? (Valid Tic-Tac-Toe, N x N, K in a row)
#   (a) x == o or x == o + 1              X moves first
#   (b) at most one player has a K-line
#   (c) the winner is the LAST MOVER      X wins => x == o+1;  O wins => x == o
#   (d) ONE cell lies on ALL the winner's lines   <- the whole extension
# (a)-(c) are the LeetCode 794 answer and are COMPLETE at 3x3 only. Larger boards
# break them: two DISJOINT X triples pass every count check and cannot exist,
# because whichever X went last, the other triple had already ended the game.
# (d) is why: the last move is one cell, so deleting it must kill every line.
# Per maximal run of length L >= K along an axis, all its K-windows share exactly
#     run[L-K : K]        non-empty iff L <= 2K-1
# so intersect those cores over the 4 axes and stop when the intersection empties.
#   L == K     any cell of the run could be last
#   L == 2K-1  ONLY the middle cell        (5 in a row at K=3 is legal)
#   L >= 2K    EMPTY -> the board is a bug (6 in a row at K=3 is not)
# O(N^2), no search: removing a mark can never CREATE a line, so any line-free
# board with legal counts peels back to empty one mark at a time.
```
