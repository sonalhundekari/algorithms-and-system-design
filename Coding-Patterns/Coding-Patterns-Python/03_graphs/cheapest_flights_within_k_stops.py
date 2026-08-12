# Cheapest Flights Within K Stops (LeetCode #787)
# Difficulty: Medium (the constraint is what makes it Medium; without it, it is Dijkstra)
# Pattern: shortest path where HOP COUNT is part of the state -- Bellman-Ford by rounds
#
# n cities, a list of one-way flights [from, to, price], and:
#
#     src   the city you start in
#     dst   the city you want to reach
#     k     the most stops you are allowed
#
# Cheapest total price from src to dst using at most k stops, or -1.
#
#     n = 4, flights = [[0,1,100],[1,2,100],[2,0,100],[1,3,600],[2,3,200]]
#     src = 0, dst = 3, k = 1  ->  700   (0 ->1 ->3, one stop)
#     src = 0, dst = 3, k = 2  ->  400   (0 ->1 ->2 ->3, two stops -- cheaper, one more stop)
#
# The two examples are the whole problem in miniature: the cheapest route and the
# shortest route are DIFFERENT routes, and which one is legal depends on k. Any
# algorithm that collapses "cheapest" and "fewest hops" into one number per city
# is going to answer one of these two wrong.
#
# FIRST, RESTATE k. "At most k stops" means at most k INTERMEDIATE cities, which
# means at most k + 1 FLIGHTS. Say this out loud and write `k + 1` exactly once,
# at the top; every off-by-one in this problem is someone re-deriving it inline.
# It is also worth confirming with the interviewer -- "stops" is genuinely
# ambiguous English, and some phrasings of this problem mean k flights.
#
# WHY PLAIN DIJKSTRA IS WRONG, PRECISELY. Dijkstra rests on one claim: the first
# time a node is popped its distance is final, so it never needs looking at
# again. That claim dies here, because the answer is not a number per node -- it
# is a number per (node, flights-used) pair. Reaching city v for 100 in three
# flights does not dominate reaching it for 500 in one: if the budget runs out at
# v, the expensive route is the only one that can still finish.
#
#     0 --1--> 1 --1--> 2 --1--> 3          k = 1, so at most 2 flights
#     0 -----------5--> 2
#
#     Cheapest way to city 2 is 0->1->2 for 2, using 2 flights -- and then there
#     is no budget left for 2->3. The answer is 0->2->3 for 6, arriving at city 2
#     for more than double the price. A visited-set Dijkstra marks city 2 done at
#     cost 2 and returns -1. It is in the tests below.
#
# SO THE STATE IS (city, flights used). Once that is said, every correct solution
# in this file is the same recurrence, walked in a different order:
#
#     best[r][v] = cheapest cost to reach v using AT MOST r flights
#     best[0][src] = 0, everything else infinite
#     best[r][v] = min( best[r-1][v],  min over edges u->v of best[r-1][u] + w )
#
# and the answer is best[k+1][dst]. Layer r depends only on layer r - 1, so two
# rows of n ints are enough -- which is exactly Bellman-Ford stopped after k + 1
# rounds instead of run to convergence.
#
#   THE TRAP, and it is the only thing an interviewer is really watching for:
#   relaxing IN PLACE instead of against a snapshot of the previous round. With
#   one shared list, a round that happens to scan edge 0->1 before edge 1->2 uses
#   the brand-new dist[1] while relaxing 1->2, so a two-flight path lands in the
#   table after one round. The result is not garbage -- it is the cost of a REAL
#   route, just one that uses more flights than the budget allows -- so it never
#   looks wrong, it is just too small. On the example above with k = 1 the
#   in-place version returns 400 (the three-flight route) instead of 700, and
#   whether it does depends on the order the flights arrive in. Both are tested.
#
# WHY THE ROUNDS TERMINATE AT ALL, and a fact worth knowing: prices are >= 1, so
# there are no zero- or negative-weight cycles, so an optimal route never
# revisits a city (cutting a cycle out makes it both cheaper AND shorter, so it
# stays legal). That means:
#
#   * k >= n - 1 makes the constraint vacuous and the problem collapses to plain
#     Dijkstra -- worth noticing, since it caps the useful work at n - 1 rounds.
#   * "at most k + 1 flights" and "the best simple path" agree, so the brute force
#     in the tests can enumerate walks and still be a valid oracle.
#   * If prices could be negative, the round-bounded Bellman-Ford here still works
#     -- a bounded walk length is immune to negative cycles -- while every
#     Dijkstra variant in this file breaks. That is the honest reason to reach for
#     this shape even when it is not the fastest.
#
# Time:  O(k * E) -- k + 1 rounds over the flight list, ~500k operations at the
#                    stated limits (n <= 100, E <= 4950, k < 100)
# Space: O(n)     -- two rows; the full table is only needed to rebuild a route

import heapq
import random
from functools import lru_cache
from math import inf
from typing import Dict, List, Optional, Sequence, Tuple

UNREACHABLE = -1

Flight = Sequence[int]          # [from, to, price]


# ------------------------------------------- the answer to write: Bellman-Ford

def find_cheapest_price(
    n: int,
    flights: Sequence[Flight],
    src: int,
    dst: int,
    k: int,
) -> int:
    """Cheapest price from src to dst using at most k stops, or -1.

    k + 1 rounds of edge relaxation, each round reading a SNAPSHOT of the last.
    The invariant is the whole proof: after round r, best[v] is the cheapest cost
    to v over all routes of at most r flights. Reading `previous` is what enforces
    "at most r" -- an edge relaxed this round can only extend a route that was
    already complete at r - 1, so no round can ever add two flights.
    """
    _validate(n, flights, src, dst, k)

    if src == dst:
        return 0                                    # no flight needed, whatever k is

    best = [inf] * n
    best[src] = 0

    for _ in range(k + 1):                          # k + 1 rounds == k + 1 flights
        previous = best[:]                          # the snapshot. NOT optional.

        for frm, to, price in flights:
            if previous[frm] + price < best[to]:
                best[to] = previous[frm] + price

        # Nothing improved, so nothing can improve again -- later rounds read the
        # same snapshot. Cheap, and it is what makes a generous k free.
        if best == previous:
            break

    return UNREACHABLE if best[dst] == inf else best[dst]


def cheapest_prices_from_source(
    n: int,
    flights: Sequence[Flight],
    src: int,
    k: int,
) -> List[int]:
    """The cheapest price to EVERY city under the same budget, in one pass.

    The single-destination version above already computes this and throws all of
    it away; if the interviewer follows up with "now answer many queries from the
    same origin", this row IS the answer table and each query becomes an index.
    Entries stay -1 where there is no route within the budget.
    """
    _validate(n, flights, src, src, k)

    best = [inf] * n
    best[src] = 0

    for _ in range(k + 1):
        previous = best[:]

        for frm, to, price in flights:
            if previous[frm] + price < best[to]:
                best[to] = previous[frm] + price

        if best == previous:
            break

    return [UNREACHABLE if cost == inf else cost for cost in best]


# ------------------------------------------------ the same thing, as BFS levels

def find_cheapest_price_layered(
    n: int,
    flights: Sequence[Flight],
    src: int,
    dst: int,
    k: int,
) -> int:
    """Identical recurrence, walked as a level-by-level BFS over an adjacency list.

    Worth knowing because it is what the same idea looks like when the graph is
    big and the budget is small: only cities whose cost IMPROVED last round can
    improve anything this round, so the frontier collapses instead of rescanning
    all E flights k times. Same O(k * E) worst case, far less work in practice,
    and it dies out on its own when the frontier empties.

    Note this is a cost-relaxing BFS, not a shortest-hops BFS: a city can enter
    the frontier in several rounds, once per improvement. Popping a city does not
    finish it -- that is the same fact that broke Dijkstra, wearing a hat.
    """
    _validate(n, flights, src, dst, k)

    if src == dst:
        return 0

    adjacency = _adjacency(n, flights)
    best = [inf] * n
    best[src] = 0
    frontier = [src]

    for _ in range(k + 1):
        if not frontier:
            break

        previous = best[:]
        improved = set()                            # a city improved twice this round
                                                    # must only be expanded once
        for frm in frontier:
            for to, price in adjacency[frm]:
                if previous[frm] + price < best[to]:
                    best[to] = previous[frm] + price
                    improved.add(to)

        frontier = list(improved)

    return UNREACHABLE if best[dst] == inf else best[dst]


# ------------------------------------------------------ Dijkstra, done correctly

def find_cheapest_price_dijkstra(
    n: int,
    flights: Sequence[Flight],
    src: int,
    dst: int,
    k: int,
) -> int:
    """Best-first search over (city, flights used), popping by cost.

    The fix for the broken version is dominance, not a bigger visited set. Pop
    order is by cost, so when (v, e) comes out, any state for v already popped was
    at most as expensive. It dominates the new one only if it ALSO used at most as
    many flights -- so the thing to remember per city is the fewest flights any
    popped state used, and a state is discardable exactly when it arrives no
    earlier in hops than that. Nothing else about v matters.

    The first pop of dst is the answer: costs come out non-decreasing and every
    queued state already respects the budget.

    When this beats the Bellman-Ford above: a large sparse graph with a generous
    k, where it stops the moment dst surfaces instead of grinding through k full
    passes. When it loses: everywhere else -- it is O(E * k log(E * k)), it is
    three times the code, and it cannot survive a negative price. Write the
    rounds; mention this.
    """
    _validate(n, flights, src, dst, k)

    adjacency = _adjacency(n, flights)
    fewest_flights = [inf] * n                      # over states already POPPED
    heap: List[Tuple[int, int, int]] = [(0, src, 0)]

    while heap:
        cost, city, used = heapq.heappop(heap)

        if city == dst:
            return cost                             # cheapest, and legal by construction

        if used >= fewest_flights[city]:
            continue                                # dominated: costs more AND hops more
        fewest_flights[city] = used

        if used == k + 1:
            continue                                # out of budget; the city still counts

        for to, price in adjacency[city]:
            heapq.heappush(heap, (cost + price, to, used + 1))

    return UNREACHABLE


# --------------------------------------------------------------- top-down DP

def find_cheapest_price_memo(
    n: int,
    flights: Sequence[Flight],
    src: int,
    dst: int,
    k: int,
) -> int:
    """The recurrence read backwards: cheapest from a city to dst within a flight
    budget, memoised on (city, budget).

    Same O(n * k) states and O(E * k) work as the rounds; it is here because it is
    the form that falls out if you start from "just DFS it" and add a cache, and
    because the memo key makes the two-dimensional state impossible to miss. The
    recursion depth is bounded by the budget, and the budget is only useful up to
    n - 1 -- but n is 100 here and Python's limit is 1000, so mention the bound
    rather than relying on it if n could grow.
    """
    _validate(n, flights, src, dst, k)

    adjacency = _adjacency(n, flights)

    @lru_cache(maxsize=None)
    def cheapest(city: int, budget: int) -> float:
        if city == dst:
            return 0
        if budget == 0:
            return inf
        return min(
            (price + cheapest(to, budget - 1) for to, price in adjacency[city]),
            default=inf,
        )

    answer = cheapest(src, k + 1)
    cheapest.cache_clear()                          # the cache is per CALL, not per module
    return UNREACHABLE if answer == inf else int(answer)


# ------------------------------------------------------------- the itinerary

CARRIED = -1                                        # "this layer did not improve on the last"


def cheapest_itinerary(
    n: int,
    flights: Sequence[Flight],
    src: int,
    dst: int,
    k: int,
) -> Optional[List[int]]:
    """The cheapest route itself, src..dst inclusive, or None when there is none.

    Reconstruction is the one place the two-row optimisation has to be given up:
    the layer that produced a city's final cost is exactly the information two
    rows throw away. Keeping the whole (k + 2) x n table costs 10,000 entries at
    the stated limits, which is nothing, and each cell records either the city it
    was reached from at this layer, or CARRIED.

    The walk back alternates: a carried cell steps down a layer, an edge cell
    steps down a layer AND back a city. It always lands on src, because src starts
    at 0 and every price is >= 1, so nothing ever improves it.
    """
    _validate(n, flights, src, dst, k)

    best = [[inf] * n for _ in range(k + 2)]
    came_from = [[CARRIED] * n for _ in range(k + 2)]
    best[0][src] = 0

    for r in range(1, k + 2):
        best[r] = best[r - 1][:]

        for frm, to, price in flights:
            if best[r - 1][frm] + price < best[r][to]:
                best[r][to] = best[r - 1][frm] + price
                came_from[r][to] = frm

    if best[k + 1][dst] == inf:
        return None

    route = [dst]
    city = dst
    for r in range(k + 1, 0, -1):
        predecessor = came_from[r][city]
        if predecessor == CARRIED:
            continue                                # this layer changed nothing here
        route.append(predecessor)
        city = predecessor

    return route[::-1]


# ------------------------------------------------------------------ plumbing

def _adjacency(n: int, flights: Sequence[Flight]) -> List[List[Tuple[int, int]]]:
    adjacency: List[List[Tuple[int, int]]] = [[] for _ in range(n)]
    for frm, to, price in flights:
        adjacency[frm].append((to, price))
    return adjacency


def _validate(n: int, flights: Sequence[Flight], src: int, dst: int, k: int) -> None:
    if n < 1:
        raise ValueError("need at least one city")
    if k < 0:
        raise ValueError("a negative stop budget is meaningless")
    if not 0 <= src < n or not 0 <= dst < n:
        raise ValueError("src and dst must name real cities")

    for flight in flights:
        if len(flight) != 3:
            raise ValueError("every flight is [from, to, price]")
        frm, to, price = flight
        if not 0 <= frm < n or not 0 <= to < n:
            raise ValueError(f"flight [{frm}, {to}] leaves the map")
        if price < 0:
            raise ValueError("negative prices break every Dijkstra in this file")


# ----------------------------------------------------- the two wrong versions
#
# Both are kept runnable. They are not strawmen -- each one is what the natural
# first draft looks like, each returns a plausible number, and the tests below
# pin down exactly which input separates it from the truth.

def find_cheapest_price_in_place(n, flights, src, dst, k) -> int:
    """DELIBERATELY WRONG: Bellman-Ford relaxing in place, with no snapshot.

    Every round reads costs the same round has already written, so a single round
    can chain arbitrarily many flights -- how many depends entirely on the order
    the flight list happens to be in. It never returns the cost of a route that
    does not exist; it returns the cost of a route that uses too many flights,
    which is why it under-reports and why the example in the problem statement
    (400 where 700 is correct) catches it.
    """
    best = [inf] * n
    best[src] = 0

    for _ in range(k + 1):
        for frm, to, price in flights:
            best[to] = min(best[to], best[frm] + price)   # <- reads what it just wrote

    return UNREACHABLE if best[dst] == inf else best[dst]


def find_cheapest_price_dijkstra_naive(n, flights, src, dst, k) -> int:
    """DELIBERATELY WRONG: textbook Dijkstra with a visited set on the CITY.

    "First pop is final" is a theorem about a graph whose only state is the node.
    Here the state is (city, flights used), so settling a city at its cheapest
    cost silently discards the pricier-but-shorter arrival that was the only one
    with budget left to finish. It fails by returning -1 on a graph where a route
    plainly exists, which is the most confusing possible symptom to debug from.
    """
    adjacency = _adjacency(n, flights)
    settled = [False] * n
    heap = [(0, src, 0)]

    while heap:
        cost, city, used = heapq.heappop(heap)

        if city == dst:
            return cost
        if settled[city]:                           # <- the city is NOT the state
            continue
        settled[city] = True

        if used == k + 1:
            continue

        for to, price in adjacency[city]:
            heapq.heappush(heap, (cost + price, to, used + 1))

    return UNREACHABLE


# ---------------------------------------------------------------------- oracle

def brute_force(n, flights, src, dst, k) -> int:
    """Every walk of at most k + 1 flights, enumerated. Exponential and only
    usable on toy graphs -- which is the point: it assumes nothing the four real
    implementations assume, so it is a genuine oracle rather than a fifth copy of
    the same idea. Walks, not paths, on purpose: the fact that an optimal route is
    simple is a CONCLUSION, not something to bake into the thing checking it."""
    adjacency = _adjacency(n, flights)
    best = inf

    def walk(city: int, used: int, cost: int) -> None:
        nonlocal best
        if city == dst:
            best = min(best, cost)
        if used == k + 1:
            return
        for to, price in adjacency[city]:
            walk(to, used + 1, cost + price)

    walk(src, 0, 0)
    return UNREACHABLE if best == inf else best


# ---- Tests ----
if __name__ == "__main__":
    SOLVERS = {
        "Bellman-Ford": find_cheapest_price,
        "layered BFS": find_cheapest_price_layered,
        "Dijkstra": find_cheapest_price_dijkstra,
        "top-down DP": find_cheapest_price_memo,
    }

    # ---- the problem's own examples ----
    example = [[0, 1, 100], [1, 2, 100], [2, 0, 100], [1, 3, 600], [2, 3, 200]]

    for name, solve in SOLVERS.items():
        assert solve(4, example, 0, 3, 1) == 700, name
        assert solve(4, example, 0, 3, 2) == 400, name

    assert cheapest_itinerary(4, example, 0, 3, 1) == [0, 1, 3]
    assert cheapest_itinerary(4, example, 0, 3, 2) == [0, 1, 2, 3]
    assert cheapest_itinerary(4, example, 0, 3, 0) is None

    # ---- edge cases ----
    for name, solve in SOLVERS.items():
        assert solve(4, example, 0, 3, 0) == -1, f"{name}: k = 0, no direct flight"
        assert solve(4, example, 0, 0, 0) == 0, f"{name}: src == dst, no budget"
        assert solve(4, example, 3, 0, 5) == -1, f"{name}: flights are ONE-WAY"
        assert solve(4, example, 0, 1, 0) == 100, f"{name}: k = 0 still allows one flight"
        assert solve(1, [], 0, 0, 0) == 0, f"{name}: one city, no flights"
        assert solve(3, [], 0, 2, 5) == -1, f"{name}: no flights at all"
        assert solve(4, example, 0, 3, 50) == 400, f"{name}: budget past n - 1 is just Dijkstra"

    # A cycle must not become a cheaper answer or a hang: the example's
    # 0 -> 1 -> 2 -> 0 loop costs 300 and is never worth flying.
    assert find_cheapest_price(4, example, 0, 0, 3) == 0

    # Parallel edges: the constraints forbid them, real schedules are full of
    # them, and every version here takes the cheaper without a special case.
    parallel = [[0, 1, 500], [0, 1, 100], [1, 2, 100]]
    for name, solve in SOLVERS.items():
        assert solve(3, parallel, 0, 2, 1) == 200, name

    # A self-loop is a legal [from, to, price] and is always a wasted hop.
    self_loop = [[0, 0, 1], [0, 1, 50]]
    for name, solve in SOLVERS.items():
        assert solve(2, self_loop, 0, 1, 3) == 50, name

    # ---- the two wrong versions, on the inputs that expose them ----
    in_place = find_cheapest_price_in_place(4, example, 0, 3, 1)
    assert in_place == 400, "in-place relaxation should under-report here"
    assert find_cheapest_price(4, example, 0, 3, 1) == 700, "the snapshot version does not"

    trap = [[0, 1, 1], [1, 2, 1], [2, 3, 1], [0, 2, 5]]
    naive = find_cheapest_price_dijkstra_naive(4, trap, 0, 3, 1)
    assert naive == -1, "visited-set Dijkstra loses the route entirely"
    for name, solve in SOLVERS.items():
        assert solve(4, trap, 0, 3, 1) == 6, name
    assert cheapest_itinerary(4, trap, 0, 3, 1) == [0, 2, 3]

    # ---- the answer table (many queries, one origin) ----
    row = cheapest_prices_from_source(4, example, 0, 2)
    assert row == [0, 100, 200, 400]
    assert all(row[d] == find_cheapest_price(4, example, 0, d, 2) for d in range(4))

    # ---- randomized cross-checks against brute force ----
    rng = random.Random(787)
    cases = 0

    for _ in range(120):
        n = rng.randint(1, 5)
        k = rng.randint(0, 3)
        flights = [
            [frm, to, rng.randint(1, 20)]
            for frm in range(n)
            for to in range(n)
            if frm != to and rng.random() < 0.45
        ]

        for src in range(n):
            table = cheapest_prices_from_source(n, flights, src, k)

            for dst in range(n):
                want = brute_force(n, flights, src, dst, k)
                cases += 1

                for name, solve in SOLVERS.items():
                    got = solve(n, flights, src, dst, k)
                    assert got == want, f"{name}: {flights} {src}->{dst} k={k}: {got} != {want}"

                assert table[dst] == want

                # The itinerary must be a real sequence of flights, must fit the
                # budget, and must cost exactly what was quoted -- three separate
                # things, and "it returned a list" is none of them.
                route = cheapest_itinerary(n, flights, src, dst, k)
                assert (route is None) == (want == -1)

                if route is None:
                    continue

                assert route[0] == src and route[-1] == dst
                assert len(route) - 2 <= k                  # cities - 2 == stops

                total = 0
                for a, b in zip(route, route[1:]):
                    legs = [p for f, t, p in flights if f == a and t == b]
                    assert legs, "the itinerary invented a flight"
                    total += min(legs)
                assert total == want

    # ---- validation ----
    for bad in (
        lambda: find_cheapest_price(3, [[0, 9, 100]], 0, 2, 1),     # off the map
        lambda: find_cheapest_price(3, [], 0, 2, -1),               # negative budget
        lambda: find_cheapest_price(3, [], 0, 9, 1),                # dst does not exist
    ):
        try:
            bad()
            raise AssertionError("bad input should have raised")
        except ValueError:
            pass

    print(f"k = 1 -> {find_cheapest_price(4, example, 0, 3, 1)}  "
          f"route {cheapest_itinerary(4, example, 0, 3, 1)}   (expect 700, 0->1->3)")
    print(f"k = 2 -> {find_cheapest_price(4, example, 0, 3, 2)}  "
          f"route {cheapest_itinerary(4, example, 0, 3, 2)}   (expect 400, 0->1->2->3)")
    print(f"in-place relaxation at k = 1 -> {in_place} (wrong: that route uses 3 flights)")
    print(f"visited-set Dijkstra on the trap -> {naive} (wrong: the route costs 6)")
    print(f"{cases} random src/dst/k combinations agreed with enumerated walks")
    print("All tests passed.")


# ---- Notes for the follow-up questions ----
#
# "Now each flight also has a duration, and the whole trip must fit in T hours."
#     A second budget is a third state dimension: best[r][v][t]. It stays a DP
#     only while t is small and discrete; with continuous time the honest answer
#     is that this becomes a resource-constrained shortest path, NP-hard in
#     general, and you fall back to Lagrangian relaxation or labelling with
#     Pareto-dominance (keep every (cost, time) pair where neither dominates).
#     The dominance rule in the Dijkstra above is the one-dimensional case of
#     exactly that, which is a good way to say it.
#
# "Many queries, same source." -> cheapest_prices_from_source: one O(k * E) pass,
#     then O(1) per query. "Many queries, same DESTINATION" is the same trick on
#     the reversed graph. "All pairs, no stop limit" is Floyd-Warshall at O(n^3) =
#     10^6 here; with a stop limit it is repeated squaring of the min-plus
#     adjacency matrix -- O(n^3 log k) -- which is the answer that actually
#     impresses, since best[2r] = best[r] (x) best[r] under (min, +).
#
# "What if k is huge -- say 10^9?" The constraint is vacuous past n - 1 flights
#     (no optimal route revisits a city, since prices are positive), so clamp k to
#     n - 1 and run plain Dijkstra. Notice this before writing the loop, not after
#     it times out.
#
# "What if some prices are negative -- refunds, subsidies?" The round-bounded
#     Bellman-Ford above is the ONLY version in this file that survives: a bounded
#     number of flights means a negative cycle cannot be milked forever. Every
#     Dijkstra here breaks, because a later, cheaper arrival can no longer be
#     ruled out by pop order. Say which of your solutions the change kills.
#
# "The graph is enormous and dst is nearby." Bidirectional or A* -- but the stop
#     budget has to be split across the two halves, which is fiddly enough that it
#     is worth pricing before promising. A better first move on a huge sparse
#     graph is the layered BFS above: it touches only cities that improved.
#
# "Why not just BFS by hops and take the cheapest at depth <= k + 1?" That is
#     precisely the layered version -- as long as "BFS" means relaxing costs per
#     level, not visiting each city once. A visited-once BFS answers "fewest
#     flights", which the first example already shows is a different question.
