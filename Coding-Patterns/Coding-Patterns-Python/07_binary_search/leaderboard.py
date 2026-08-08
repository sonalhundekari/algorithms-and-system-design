# Design a Leaderboard
# Difficulty: Hard (design)
# Pattern: Fenwick tree over the score domain + binary lifting descent
#
# Problem: Design a leaderboard supporting score updates, the rank of an
# ARBITRARY player, and top-K / "players near me" windows.
#
# The two clarifying answers that pick this design:
#   1. Rank of an arbitrary player is required -> a top-K heap is not enough.
#   2. The score domain is bounded integers   -> we can index by SCORE rather
#      than by player, which is the whole trick here.
#
# Approach: The naive read is "keep players sorted", which costs O(log P) per
# op with P = player count, and forces a balanced BST / skip list (a Redis
# ZSET). But rank does not actually need the players ordered -- it only needs a
# COUNT of players scoring higher. So index the score axis instead:
#
#   tree[]    Fenwick tree of counts, one slot per possible score.
#             prefix(s) = how many players score <= s.
#   score{}   player -> current score
#   bucket{}  score  -> players at that score, in arrival order
#
# rank(p) = (players scoring strictly more than p) + 1
#         = total - prefix(score[p]) + 1
#
# Every cost is now O(log MaxScore) and INDEPENDENT of the player count -- 100
# players and 100 million players pay the same ~20 array touches. Memory is
# O(MaxScore) for the tree plus O(P) for the maps.
#
# Going the other way (rank -> player) is a binary lifting descent over the
# same tree: walk the Fenwick nodes high bit to low, greedily taking any step
# that keeps the running prefix below the target. That is the "binary search on
# the answer space" pattern, executed against a structure that is already
# shaped like the search tree.
#
# Time:  submit / remove / rank / count_in_score_range   O(log MaxScore)
#        top_k(k) / around(p, r)                         O(k log MaxScore)
# Space: O(MaxScore + P)
#
# Ties: competition ranking (1, 2, 2, 4) -- tied players share a rank. Within a
# tie group the order is earliest-to-reach-the-score first. See the note at the
# bottom for making the tiebreak part of the rank itself.

from itertools import islice
from typing import Iterable, List, Optional, Tuple

# (rank, player, score)
Entry = Tuple[int, str, int]


class Leaderboard:
    def __init__(self, max_score: int = 1_000_000):
        if max_score < 0:
            raise ValueError("max_score must be non-negative")

        self._max_score = max_score
        self._size = max_score + 1                # scores 0..max_score
        self._tree = [0] * (self._size + 1)       # Fenwick is 1-based: score s -> index s + 1
        self._span = 1 << (self._size.bit_length() - 1)   # largest power of two <= size

        self._score = {}       # player -> score
        self._bucket = {}      # score -> {player: None}, an insertion-ordered set
        self._total = 0

    # ---- Fenwick primitives ----

    def _add(self, score: int, delta: int) -> None:
        i = score + 1
        while i <= self._size:
            self._tree[i] += delta
            i += i & -i

    def _prefix(self, score: int) -> int:
        """How many players score <= `score`."""
        if score < 0:
            return 0
        i = min(score, self._max_score) + 1
        total = 0
        while i > 0:
            total += self._tree[i]
            i -= i & -i
        return total

    def _score_at_ascending_index(self, j: int) -> int:
        """The score of the j-th player counting up from the bottom (1-based).

        Binary lifting: `pos` tracks the largest Fenwick index whose prefix is
        still < j. Each candidate step is a power of two, so this is a binary
        search that happens to reuse the tree's own node layout.
        """
        pos = 0
        remaining = j
        step = self._span

        while step > 0:
            nxt = pos + step
            if nxt <= self._size and self._tree[nxt] < remaining:
                pos = nxt
                remaining -= self._tree[pos]
            step >>= 1

        # pos is the last index with prefix < j, so index pos + 1 is the answer,
        # and index pos + 1 maps back to score pos.
        return pos

    def _score_at_rank(self, rank: int) -> int:
        """The score sitting at 1-based descending rank (rank 1 = highest score)."""
        return self._score_at_ascending_index(self._total - rank + 1)

    # ---- Writes ----

    def submit(self, player: str, score: int) -> None:
        """Set `player`'s score absolutely. Inserts if unseen."""
        if not 0 <= score <= self._max_score:
            raise ValueError(f"score {score} outside 0..{self._max_score}")

        old = self._score.get(player)
        if old == score:
            return
        if old is not None:
            self._detach(player, old)

        self._score[player] = score
        self._bucket.setdefault(score, {})[player] = None
        self._add(score, 1)
        self._total += 1

    def increment(self, player: str, delta: int) -> int:
        """Bump a score by `delta`, clamped to the domain. Returns the new score."""
        new = min(max(self._score.get(player, 0) + delta, 0), self._max_score)
        self.submit(player, new)
        return new

    def remove(self, player: str) -> bool:
        """Drop a player. Returns whether they were present."""
        old = self._score.pop(player, None)
        if old is None:
            return False
        self._detach(player, old, popped=True)
        return True

    def _detach(self, player: str, score: int, popped: bool = False) -> None:
        """Unhook a player from their current score bucket and the count tree."""
        group = self._bucket[score]
        del group[player]
        if not group:
            del self._bucket[score]
        if not popped:
            del self._score[player]
        self._add(score, -1)
        self._total -= 1

    # ---- Reads ----

    def __len__(self) -> int:
        return self._total

    def score_of(self, player: str) -> Optional[int]:
        return self._score.get(player)

    def rank(self, player: str) -> int:
        """Competition rank: 1 = best, tied players share a rank. KeyError if absent."""
        score = self._score[player]
        return self._total - self._prefix(score) + 1

    def count_in_score_range(self, low: int, high: int) -> int:
        """Players whose score falls in [low, high]."""
        if high < low:
            return 0
        return self._prefix(high) - self._prefix(low - 1)

    def percentile_of(self, player: str) -> float:
        """Fraction of the field this player is ahead of, in [0, 1]."""
        if self._total <= 1:
            return 1.0
        behind = self._prefix(self._score[player] - 1)
        return behind / (self._total - 1)

    def slice(self, start_rank: int, count: int) -> List[Entry]:
        """`count` entries starting at 1-based rank `start_rank`, best first.

        One Fenwick descent per distinct score in the window, so O(count log
        MaxScore) -- not O(count) descents when players are tied.
        """
        rank = max(1, start_rank)
        out: List[Entry] = []

        while len(out) < count and rank <= self._total:
            score = self._score_at_rank(rank)
            group = self._bucket[score]
            group_rank = self._total - self._prefix(score) + 1   # rank shared by this tie group

            # `rank` can land mid-group when the window starts inside a tie.
            offset = rank - group_rank
            need = count - len(out)
            out.extend((group_rank, p, score) for p in islice(group, offset, offset + need))

            rank = group_rank + len(group)   # first slot past the whole group

        return out

    def top_k(self, k: int) -> List[Entry]:
        return self.slice(1, k)

    def around(self, player: str, radius: int) -> List[Entry]:
        """The player plus up to `radius` neighbours on each side."""
        return self.slice(self.rank(player) - radius, 2 * radius + 1)


# ---- Tests ----
if __name__ == "__main__":
    import random
    import time

    def brute_rank(pairs, player):
        """Reference implementation: competition rank by direct comparison."""
        score = dict(pairs)[player]
        return sum(1 for _, s in pairs if s > score) + 1

    # -- basics --
    lb = Leaderboard(max_score=1000)
    for name, score in [("ana", 500), ("bo", 900), ("cy", 500), ("dee", 100), ("eli", 750)]:
        lb.submit(name, score)

    assert len(lb) == 5
    assert lb.rank("bo") == 1
    assert lb.rank("eli") == 2
    assert lb.rank("ana") == 3      # tied with cy
    assert lb.rank("cy") == 3       # competition ranking: both are 3rd
    assert lb.rank("dee") == 5      # ...so nobody is 4th
    assert lb.score_of("bo") == 900
    assert lb.score_of("nobody") is None

    assert lb.top_k(3) == [(1, "bo", 900), (2, "eli", 750), (3, "ana", 500)]
    # Within the tie, arrival order decides: ana submitted before cy.
    assert lb.top_k(4) == [(1, "bo", 900), (2, "eli", 750), (3, "ana", 500), (3, "cy", 500)]
    assert len(lb.top_k(99)) == 5                    # asking past the end is fine

    # -- windows --
    assert lb.around("eli", 1) == [(1, "bo", 900), (2, "eli", 750), (3, "ana", 500)]
    assert lb.around("bo", 2)[0] == (1, "bo", 900)   # clamps at the top
    assert lb.slice(3, 2) == [(3, "ana", 500), (3, "cy", 500)]
    assert lb.slice(4, 2) == [(3, "cy", 500), (5, "dee", 100)]   # starts mid-tie

    # -- range counts --
    assert lb.count_in_score_range(500, 900) == 4
    assert lb.count_in_score_range(0, 99) == 0
    assert lb.count_in_score_range(100, 100) == 1

    # -- updates move a player without disturbing anyone else --
    lb.submit("dee", 1000)
    assert lb.rank("dee") == 1
    assert lb.rank("bo") == 2
    assert len(lb) == 5                              # update, not insert

    assert lb.increment("dee", -600) == 400
    assert lb.rank("dee") == 5
    assert lb.increment("dee", -99999) == 0          # clamps at the floor

    assert lb.remove("dee") is True
    assert lb.remove("dee") is False
    assert len(lb) == 4
    assert lb.rank("bo") == 1

    # -- randomized cross-check against brute force --
    random.seed(7)
    lb = Leaderboard(max_score=200)
    truth = {}
    for step in range(3000):
        player = f"p{random.randrange(60)}"
        if truth and random.random() < 0.15:
            victim = random.choice(list(truth))
            lb.remove(victim)
            del truth[victim]
        else:
            score = random.randrange(201)
            lb.submit(player, score)
            truth[player] = score

        if step % 100 == 0 and truth:
            pairs = list(truth.items())
            for who in truth:
                assert lb.rank(who) == brute_rank(pairs, who), (step, who)

            # Tie order is arrival-based rather than name-based, so compare the
            # score sequence (which is fully determined) and the rank labels.
            expected = sorted(pairs, key=lambda kv: -kv[1])[:10]
            got = lb.top_k(10)
            assert [s for _, _, s in got] == [s for _, s in expected], step
            assert [r for r, _, _ in got] == [brute_rank(pairs, p) for _, p, _ in got], step

            lo, hi = sorted(random.sample(range(201), 2))
            assert lb.count_in_score_range(lo, hi) == sum(1 for s in truth.values() if lo <= s <= hi)

    # -- the point of the design: cost does not track player count --
    big = Leaderboard(max_score=1_000_000)
    random.seed(11)
    for i in range(200_000):
        big.submit(f"player{i}", random.randrange(1_000_001))

    start = time.perf_counter()
    for i in range(0, 200_000, 200):
        big.rank(f"player{i}")
    elapsed = time.perf_counter() - start
    print(f"1,000 rank queries over 200,000 players: {elapsed * 1000:.1f} ms")
    print(f"top 3: {big.top_k(3)}")
    print(f"around player0 (rank {big.rank('player0')}): {len(big.around('player0', 2))} entries")

    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "What if scores were unbounded 64-bit?"
#     The O(MaxScore) tree becomes impossible. Switch to an order-statistic
#     tree -- a skip list or balanced BST with subtree counts -- which gives the
#     same API at O(log P) instead of O(log MaxScore). That is exactly what a
#     Redis ZSET is. Or keep this structure over COMPRESSED coordinates if the
#     set of distinct scores is known up front.
#
# "I want tied players to have distinct ranks."
#     Fold the tiebreak into the key: key = score * 2^k + (T_MAX - reached_at).
#     Every operation here works unchanged, but the domain grows by 2^k, so this
#     only stays affordable while the widened domain is still small enough to
#     allocate. Beyond that, the order-statistic tree is the answer.
#
# "Make it survive a restart / scale past one box."
#     Writes are a commutative +1/-1 on a score slot, which shards cleanly:
#     give each shard its own tree, then rank(p) = 1 + sum over shards of
#     (shard_total - shard_prefix(score)). That is one fan-out read per query,
#     and shards never need to agree on player placement -- only on counts.
#
# "Rolling window (last 7 days) instead of all-time."
#     Scores now expire, so pair this with a queue of (player, score, timestamp)
#     and apply the inverse update as entries age out. The tree handles removal
#     at the same O(log MaxScore), so expiry is just a background drain.
