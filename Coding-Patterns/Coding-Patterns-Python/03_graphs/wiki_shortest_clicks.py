# Wiki Page Shortest-Click Path (+ crawl-all-reachable, + threaded crawl)
# Difficulty: Easy-Medium base case, Medium-Hard follow-up
# Pattern: BFS over an IMPLICIT graph exposed only through an API
#
# You are handed one helper and nothing else:
#
#     get_linked_pages(uri: str) -> List[str]      # every page `uri` links out to
#
# Task 1: fewest clicks from start_uri to target_uri.
# Task 2 (the other half of the loop): every page reachable from a start page.
# Task 3 (follow-up): do Task 2 on several threads without crawling a page twice.
#
#     get_linked_pages("A") -> ["B", "C"]
#     get_linked_pages("B") -> ["D"]
#     get_linked_pages("C") -> ["D", "E"]
#     get_linked_pages("D") -> []
#     shortest_clicks("A", "D") == 2
#
# WHAT TO PIN DOWN BEFORE WRITING ANYTHING. The API hides the whole graph, so
# every one of these is a real question and none is pedantry:
#
#   1. Are links directed? On a wiki, yes -- A linking to B says nothing about B
#      linking to A. That kills "just walk both ways" and it is why the
#      bidirectional trick at the bottom needs a SECOND API.
#   2. Is the URI the identity? "/wiki/Cat", "/wiki/cat#Anatomy" and
#      "https://…/wiki/Cat" are one page and three strings. If the API does not
#      canonicalize, YOU must, or the visited set stops deduping and the crawl
#      never ends.
#   3. What comes back for a page that does not exist (a red link)? Empty list,
#      or an exception? Here: empty list -- see PageGraph.
#   4. Is the reachable set finite / small enough to hold? "All of Wikipedia" is
#      ~7M nodes; the visited set is the memory bound and the honest answer is
#      "bounded crawl" (max_clicks / max_pages), not "BFS the internet".
#   5. Unreachable target: -1, None, or raise? Pick one and say it out loud.
#      Here: -1, matching the rest of this tree.
#   6. Is the API rate-limited or slow? That is the entire reason Tasks 2 and 3
#      exist, and it changes the cost model below.
#
# THE COST MODEL IS API CALLS, NOT COMPARISONS. Edges are only readable by
# spending a network round trip, so the number that matters is how many times
# get_linked_pages is called. BFS with the visited set marked AT ENQUEUE TIME
# calls it exactly once per reachable page: O(V) calls, O(V + E) local work,
# O(V) memory.
#
#   THE TRAP: marking visited when a node is DEQUEUED instead. It still returns
#   the right answer, so it passes the example -- but a page linked from k others
#   is enqueued k times and expanded k times, so the API call count blows up from
#   V to E. On a wiki that is the difference between 6 requests and 6,000. This
#   is the single thing an interviewer is watching for in this problem.
#
# WHY BFS AND NOT DFS FOR TASK 1. BFS pops in non-decreasing distance order, so
# the first arrival at a page is already along a shortest path and can never be
# improved. DFS commits to one branch to its end, so any distance it records is
# provisional -- you would have to revisit pages every time a cheaper route shows
# up, which is exhaustive search, not traversal. "First touch is final" is
# exactly what BFS buys, and it holds only because every click costs the same 1.
#
# Time:  O(V + E) local work, O(V) API calls
# Space: O(V) for visited + frontier

import queue
import threading
import time
from collections import Counter, deque
from concurrent.futures import ThreadPoolExecutor
from typing import Callable, Dict, Iterable, List, Optional, Sequence, Set

LinkApi = Callable[[str], Sequence[str]]

UNREACHABLE = -1


# --------------------------------------------------------------- Task 1: BFS

def shortest_clicks(
    start_uri: str,
    target_uri: str,
    get_linked_pages: LinkApi,
    max_clicks: Optional[int] = None,
) -> int:
    """Fewest clicks from start_uri to target_uri, or UNREACHABLE (-1).

    `max_clicks` bounds the search for the real-world case where the reachable
    set is too big to exhaust; None means "search until the frontier dies".
    """
    if start_uri == target_uri:
        return 0                      # the case everyone forgets

    frontier = deque([(start_uri, 0)])
    visited = {start_uri}             # marked on ENQUEUE -- one API call per page

    while frontier:
        uri, clicks = frontier.popleft()
        if max_clicks is not None and clicks >= max_clicks:
            continue                  # cannot afford to expand this one

        for link in get_linked_pages(uri) or ():
            if link in visited:
                continue
            if link == target_uri:
                return clicks + 1     # first touch is already shortest
            visited.add(link)
            frontier.append((link, clicks + 1))

    return UNREACHABLE


def shortest_path(
    start_uri: str,
    target_uri: str,
    get_linked_pages: LinkApi,
) -> Optional[List[str]]:
    """The clicks themselves, start..target inclusive, or None if unreachable.

    Same BFS; the only addition is a parent pointer written at the moment a page
    is first enqueued -- which is the moment its distance is decided, so the
    chain it forms is a genuine shortest path. len(path) - 1 == shortest_clicks.
    """
    if start_uri == target_uri:
        return [start_uri]

    parent: Dict[str, Optional[str]] = {start_uri: None}
    frontier = deque([start_uri])

    while frontier:
        uri = frontier.popleft()
        for link in get_linked_pages(uri) or ():
            if link in parent:
                continue
            parent[link] = uri
            if link == target_uri:
                path = [link]
                while parent[path[-1]] is not None:
                    path.append(parent[path[-1]])
                return path[::-1]
            frontier.append(link)

    return None


# ------------------------------------------------- Task 2: crawl everything

def crawl_all(start_uri: str, get_linked_pages: LinkApi) -> Set[str]:
    """Every page reachable from start_uri, start_uri included. BFS.

    Order is irrelevant to the ANSWER here -- the reachable set is the reachable
    set -- so the DFS/BFS argument is no longer about correctness. It is about
    how the traversal behaves against a remote API:
    """
    visited = {start_uri}
    frontier = deque([start_uri])

    while frontier:
        for link in get_linked_pages(frontier.popleft()) or ():
            if link not in visited:
                visited.add(link)
                frontier.append(link)

    return visited


def crawl_all_dfs(start_uri: str, get_linked_pages: LinkApi) -> Set[str]:
    """Same set, depth-first. Kept to make the comparison concrete.

    Why BFS is the better answer for a crawl, even though both are O(V) calls:

      * Recursion depth. The natural DFS is recursive, and link chains are long
        -- a 1,000-page chain blows Python's stack at the default limit of 1,000.
        This version is explicitly stack-based for that reason, which is already
        an admission that the elegant form does not survive contact.
      * A partial result is useless. Stop a DFS early and you hold one thin
        thread dangling into the far side of the graph. Stop a BFS early and you
        hold "everything within k clicks", complete and meaningful -- which is
        also what makes depth limits, budgets and timeouts expressible.
      * Locality and politeness. BFS finishes with one host's neighbourhood
        before wandering; DFS hops hosts every step, which is exactly what
        per-host rate limiters punish.
      * Parallelism. A BFS frontier is a batch of independent, equal-cost calls
        -- the parallel version below is the same loop with a worker pool. DFS is
        inherently sequential: the next call depends on the last one's result.
      * Distances come free. BFS already has them; DFS does not, so the moment
        the interviewer asks "and how far is each page?" the DFS is rewritten.
    """
    visited = {start_uri}
    stack = [start_uri]

    while stack:
        for link in get_linked_pages(stack.pop()) or ():
            if link not in visited:
                visited.add(link)
                stack.append(link)

    return visited


def crawl_distances(start_uri: str, get_linked_pages: LinkApi) -> Dict[str, int]:
    """Reachable pages -> click distance from start_uri. The sequential answer
    the two threaded crawls below are checked against."""
    dist = {start_uri: 0}
    frontier = deque([start_uri])

    while frontier:
        uri = frontier.popleft()
        for link in get_linked_pages(uri) or ():
            if link not in dist:
                dist[link] = dist[uri] + 1
                frontier.append(link)

    return dist


# ------------------------------------------- Task 3: the same crawl, threaded
#
# Two things are hard, and neither is the traversal:
#
#   DEDUPE. "Check visited, then add, then enqueue" must be ONE atomic step. If
#   two workers interleave between the check and the add, both enqueue the same
#   page and it is fetched twice -- rare, silent, and it recurs on every diamond
#   in the graph. In Python a set update is not the safe part; the read-then-
#   write pair is. Hold a lock across both, or use a primitive that fuses them
#   (dict.setdefault, or C#'s ConcurrentDictionary.TryAdd).
#
#   TERMINATION. "Queue is empty" does NOT mean "done": a worker may be mid-call
#   and about to discover fifty more pages. You need queued + in-flight == 0.
#   queue.Queue gives that for free as long as task_done() is called AFTER the
#   children are put() -- that is the invariant the finally-block preserves.

_STOP = object()


def crawl_all_parallel(
    start_uri: str,
    get_linked_pages: LinkApi,
    workers: int = 4,
) -> Set[str]:
    """Every reachable page, crawled by `workers` threads, each page fetched once.

    Free-running work queue: no barriers, so a fast branch keeps everyone busy.
    The price is that arrival order is no longer level order -- this returns the
    SET only. For distances use crawl_distances_parallel below; see the note
    there for why bolting depths onto this loop quietly produces wrong numbers.

    Threads (not processes) are right here despite the GIL: the work is network
    I/O, and the GIL is released for the duration of every call.
    """
    if workers < 1:
        raise ValueError("workers must be >= 1")

    visited = {start_uri}
    lock = threading.Lock()
    work: "queue.Queue" = queue.Queue()
    work.put(start_uri)
    errors: List[BaseException] = []

    def worker() -> None:
        while True:
            uri = work.get()
            if uri is _STOP:
                work.task_done()
                return
            try:
                links = get_linked_pages(uri) or ()

                # Check-and-add under one lock; enqueue outside it. Two workers
                # can never both come out of here holding the same page.
                with lock:
                    fresh = [link for link in links if link not in visited]
                    visited.update(fresh)

                for link in fresh:
                    work.put(link)          # children counted BEFORE the parent
            except BaseException as exc:    # noqa: BLE001 - a dead worker must not
                errors.append(exc)          # deadlock join(); surface it instead
            finally:
                work.task_done()            # ...so join() cannot fire early

    threads = [threading.Thread(target=worker, daemon=True) for _ in range(workers)]
    for thread in threads:
        thread.start()

    work.join()                             # queued + in-flight both hit zero
    for _ in threads:
        work.put(_STOP)
    for thread in threads:
        thread.join()

    if errors:
        raise errors[0]

    return visited


def crawl_distances_parallel(
    start_uri: str,
    get_linked_pages: LinkApi,
    workers: int = 4,
) -> Dict[str, int]:
    """Reachable pages -> click distance, level-synchronous across `workers`.

    WHY NOT just carry (uri, depth) through the free-running queue above: the
    depth written is the depth of whichever worker DISCOVERED the page first,
    and with several workers in flight that is no longer the smallest depth. A
    worker holding a depth-3 page can finish its call before another worker has
    even started expanding a depth-1 page that also links there, and the page is
    then permanently recorded as 4. Nothing crashes; the numbers are just wrong,
    on some runs and not others.

    A level barrier restores it: the whole frontier is fetched in parallel, then
    ONE thread folds the results in, so "first touch" is decided in level order
    exactly as in the sequential BFS. Deduping single-threaded between levels
    also means no lock at all. The cost is the barrier -- the level is only as
    fast as its slowest page -- which is the honest trade to state: exact
    distances OR maximum throughput, and the free-running crawl above is the
    other side of it.
    """
    if workers < 1:
        raise ValueError("workers must be >= 1")

    dist = {start_uri: 0}
    frontier = [start_uri]
    depth = 0

    with ThreadPoolExecutor(max_workers=workers) as pool:
        while frontier:
            depth += 1
            batches = list(pool.map(lambda uri: get_linked_pages(uri) or (), frontier))

            frontier = []
            for links in batches:
                for link in links:
                    if link not in dist:
                        dist[link] = depth
                        frontier.append(link)

    return dist


# ------------------------------------------------------- rate-limit follow-up

def cached(api: LinkApi) -> LinkApi:
    """Memoize the API. The cheapest call is the one you do not make.

    A single BFS never repeats a page anyway, so this pays off across RUNS
    (many shortest_clicks queries over one neighbourhood) and it makes a retry
    after a partial failure nearly free. Not locked: concurrent misses on the
    same key just cost a duplicate fetch, and dict writes are atomic under the
    GIL, so the cache cannot be corrupted -- only redundantly filled. If a
    duplicate fetch is itself expensive, key a lock per URI.

    The rest of the rate-limit answer, which the cache does not cover:
      * Batch. If the API can take many URIs per request, fetch a whole BFS
        level in one call -- that is another reason the frontier is the right
        unit of work.
      * Bound concurrency to the quota, not to the CPU: a semaphore or a token
        bucket sized to the allowed requests/second.
      * Retry 429/5xx with exponential backoff + jitter, and treat the retry as
        the same in-flight unit so termination accounting still holds.
      * Persist the cache (and the visited set) if the crawl can be resumed.
    """
    store: Dict[str, Sequence[str]] = {}

    def wrapper(uri: str) -> Sequence[str]:
        if uri not in store:
            store[uri] = api(uri)
        return store[uri]

    return wrapper


# ------------------------------------------------------------ bidirectional

def bidirectional_clicks(
    start_uri: str,
    target_uri: str,
    get_linked_pages: LinkApi,
    get_linking_pages: LinkApi,
) -> int:
    """Same answer as shortest_clicks, meeting in the middle.

    Worth MENTIONING in the screen, rarely worth writing, and it has a hard
    precondition people skip: it needs a reverse index -- "who links TO this
    page" -- which a forward-only link API cannot give you. On a real wiki that
    is a second service (or a whole precomputed backlink table), so the honest
    line is "if backlinks are available, here is the win; if not, this option
    does not exist".

    The win, when it exists: one-directional BFS touches ~b^d pages, two halves
    touch ~2 * b^(d/2). On a graph with branching factor 100 and d = 6 that is
    10^12 versus 2 * 10^6 -- the difference between impossible and instant.

    Expand the SMALLER frontier each round; that is what keeps the two halves
    balanced when in-degree and out-degree differ wildly, as they do on a wiki.
    """
    if start_uri == target_uri:
        return 0

    seen_from_start = {start_uri: 0}
    seen_from_target = {target_uri: 0}
    front, back = [start_uri], [target_uri]
    clicks = 0

    while front and back:
        # Always grow the cheaper side.
        if len(back) < len(front):
            front, back = back, front
            seen_from_start, seen_from_target = seen_from_target, seen_from_start
            api = get_linking_pages if seen_from_start is not None else None

        forward = seen_from_start.get(start_uri) is not None
        api = get_linked_pages if forward else get_linking_pages

        clicks += 1
        nxt = []
        for uri in front:
            for link in api(uri) or ():
                if link in seen_from_target:
                    return seen_from_start[uri] + 1 + seen_from_target[link]
                if link not in seen_from_start:
                    seen_from_start[link] = seen_from_start[uri] + 1
                    nxt.append(link)
        front = nxt

    return UNREACHABLE


# ----------------------------------------------------------- the simulator
#
# The follow-up half of the loop: "write get_linked_pages yourself so we can run
# it". An adjacency dict behind a function, plus a per-URI call counter -- which
# is not decoration. "Every page expanded exactly once" is the property the whole
# problem is about, and the counter is the only thing that can assert it.

class PageGraph:
    """In-memory stand-in for the real link API."""

    def __init__(self, adjacency: Dict[str, Iterable[str]], latency: float = 0.0):
        self._adjacency = {uri: list(links) for uri, links in adjacency.items()}
        self._latency = latency
        self._lock = threading.Lock()
        self.calls: Counter = Counter()

    def get_linked_pages(self, uri: str) -> List[str]:
        with self._lock:                       # Counter += is read-modify-write
            self.calls[uri] += 1
        if self._latency:
            time.sleep(self._latency)          # stands in for the round trip
        # A link to a page that does not exist is a wiki red link, not an error.
        # Returning [] here is a CONTRACT DECISION -- ask, do not assume.
        return list(self._adjacency.get(uri, ()))

    def get_linking_pages(self, uri: str) -> List[str]:
        """Backlinks. A real API usually does NOT have this -- see above."""
        return [src for src, links in self._adjacency.items() if uri in links]

    @property
    def call_count(self) -> int:
        return sum(self.calls.values())

    @property
    def refetched(self) -> List[str]:
        """Pages fetched more than once -- must always be empty."""
        return sorted(uri for uri, n in self.calls.items() if n > 1)

    def reset(self) -> None:
        self.calls.clear()


# ---- Tests ----
if __name__ == "__main__":
    # The interview's own example.
    example = PageGraph({"A": ["B", "C"], "B": ["D"], "C": ["D", "E"], "D": []})
    assert shortest_clicks("A", "D", example.get_linked_pages) == 2
    assert shortest_clicks("A", "E", example.get_linked_pages) == 2
    assert shortest_clicks("A", "B", example.get_linked_pages) == 1
    assert shortest_path("A", "D", example.get_linked_pages) in (["A", "B", "D"], ["A", "C", "D"])

    # Edge cases.
    assert shortest_clicks("A", "A", example.get_linked_pages) == 0        # no clicks
    assert shortest_path("A", "A", example.get_linked_pages) == ["A"]
    assert shortest_clicks("D", "A", example.get_linked_pages) == -1       # links are DIRECTED
    assert shortest_clicks("A", "Nope", example.get_linked_pages) == -1
    assert shortest_path("D", "A", example.get_linked_pages) is None
    assert shortest_clicks("Ghost", "A", example.get_linked_pages) == -1   # start not in the graph

    # Cycles and self-links must not hang, and must not be re-fetched.
    loops = PageGraph({"A": ["A", "B"], "B": ["A", "C"], "C": ["B", "A"]})
    assert shortest_clicks("A", "C", loops.get_linked_pages) == 2
    assert crawl_all("A", loops.get_linked_pages) == {"A", "B", "C"}
    assert loops.refetched == []

    # A depth bound stops the search early -- and reports "not within budget"
    # the same way it reports "unreachable", which is worth flagging as a
    # limitation of collapsing both into -1.
    chain = PageGraph({c: [nxt] for c, nxt in zip("ABCDE", "BCDEF")})
    assert shortest_clicks("A", "F", chain.get_linked_pages) == 5
    assert shortest_clicks("A", "F", chain.get_linked_pages, max_clicks=3) == -1

    # ONE API CALL PER VISITED PAGE. This is the assertion the problem is about:
    # the target is found without ever expanding it or anything past it.
    example.reset()
    assert shortest_clicks("A", "D", example.get_linked_pages) == 2
    assert example.calls == Counter({"A": 1, "B": 1, "C": 1})   # D never expanded
    assert example.refetched == []

    # The dequeue-time-visited bug costs E calls instead of V. Same answer, and
    # on this tiny diamond it already fetches D twice.
    def sloppy_shortest_clicks(start, target, api):
        frontier, visited = deque([(start, 0)]), set()
        while frontier:
            uri, clicks = frontier.popleft()
            if uri in visited:
                continue
            visited.add(uri)                      # <- too late
            if uri == target:
                return clicks
            for link in api(uri) or ():
                frontier.append((link, clicks + 1))
        return -1

    example.reset()
    assert sloppy_shortest_clicks("A", "E", example.get_linked_pages) == 2
    assert example.calls["D"] == 2, "the bug this file exists to avoid"

    # ---- crawl-all: a cycle AND a diamond in one graph ----
    #
    #   HOME -> A -> C -\
    #        \-> B -> C -> D -> HOME     (D closes the cycle)
    #                        \-> ORPHAN? no: ISLAND is unreachable
    site = PageGraph({
        "HOME": ["A", "B"],
        "A": ["C"],
        "B": ["C", "D"],
        "C": ["D"],
        "D": ["HOME", "E"],
        "E": [],
        "ISLAND": ["HOME"],          # links IN, so it is not reachable FROM HOME
    })
    expected = {"HOME", "A", "B", "C", "D", "E"}

    site.reset()
    assert crawl_all("HOME", site.get_linked_pages) == expected
    assert site.refetched == [] and site.call_count == len(expected)

    site.reset()
    assert crawl_all_dfs("HOME", site.get_linked_pages) == expected
    assert site.refetched == []

    assert crawl_distances("HOME", site.get_linked_pages) == {
        "HOME": 0, "A": 1, "B": 1, "C": 2, "D": 2, "E": 3,
    }
    assert shortest_clicks("HOME", "E", site.get_linked_pages) == 3
    assert shortest_clicks("HOME", "ISLAND", site.get_linked_pages) == -1

    # ---- threaded crawl: same set, each page expanded exactly once ----
    for pool_size in (1, 2, 4, 8):
        site.reset()
        assert crawl_all_parallel("HOME", site.get_linked_pages, workers=pool_size) == expected
        assert site.refetched == [], f"page fetched twice with {pool_size} workers"
        assert site.call_count == len(expected)

        site.reset()
        assert crawl_distances_parallel("HOME", site.get_linked_pages, workers=pool_size) == {
            "HOME": 0, "A": 1, "B": 1, "C": 2, "D": 2, "E": 3,
        }
        assert site.refetched == []

    # A worker that raises must not deadlock the join -- it must surface.
    def exploding_api(uri):
        if uri == "B":
            raise RuntimeError("503 from the wiki")
        return site.get_linked_pages(uri)

    try:
        crawl_all_parallel("HOME", exploding_api, workers=4)
        raise AssertionError("the worker's exception was swallowed")
    except RuntimeError as exc:
        assert "503" in str(exc)

    # Randomized: the threaded crawls must agree with the sequential ones, on
    # graphs full of cycles and shared children, every time.
    import random

    rng = random.Random(31)
    for trial in range(60):
        n = rng.randint(1, 24)
        names = [f"p{i}" for i in range(n)]
        adjacency = {
            name: rng.sample(names, rng.randint(0, min(4, n)))   # self-links included
            for name in names
        }
        graph = PageGraph(adjacency)

        want_set = crawl_all("p0", graph.get_linked_pages)
        want_dist = crawl_distances("p0", graph.get_linked_pages)

        graph.reset()
        assert crawl_all_parallel("p0", graph.get_linked_pages, workers=4) == want_set
        assert graph.refetched == [] and graph.call_count == len(want_set)

        graph.reset()
        assert crawl_distances_parallel("p0", graph.get_linked_pages, workers=4) == want_dist
        assert graph.refetched == []

        # ...and both BFS forms agree with each other on every pair.
        for src in ("p0", names[-1]):
            reference = crawl_distances(src, graph.get_linked_pages)
            for dst in names:
                want = reference.get(dst, -1)
                assert shortest_clicks(src, dst, graph.get_linked_pages) == want
                path = shortest_path(src, dst, graph.get_linked_pages)
                assert (path is None) == (want == -1)
                if path is not None:
                    assert len(path) - 1 == want
                    assert path[0] == src and path[-1] == dst
                    for a, b in zip(path, path[1:]):     # every step is a real link
                        assert b in graph.get_linked_pages(a)
                assert bidirectional_clicks(
                    src, dst, graph.get_linked_pages, graph.get_linking_pages) == want

    # ---- the cache, and what threads actually buy ----
    cached_api = cached(site.get_linked_pages)
    site.reset()
    crawl_all("HOME", cached_api)
    first = site.call_count
    crawl_all("HOME", cached_api)                 # second run: entirely from cache
    assert site.call_count == first

    # 60ms of fake latency per page, so the pool has something to hide.
    slow = PageGraph({name: list(links) for name, links in site._adjacency.items()}, latency=0.06)

    slow.reset()
    t0 = time.perf_counter()
    crawl_all("HOME", slow.get_linked_pages)
    serial = time.perf_counter() - t0

    slow.reset()
    t0 = time.perf_counter()
    crawl_all_parallel("HOME", slow.get_linked_pages, workers=4)
    threaded = time.perf_counter() - t0

    print(f"6 pages @ 60ms:  serial {serial * 1000:6.1f}ms   4 threads {threaded * 1000:6.1f}ms")
    print("  (threads win because the GIL is released across the I/O -- the speedup")
    print("   is capped by the graph's DEPTH, not by the worker count: level 3 of")
    print("   this graph holds one page, and one page cannot be split.)")

    print(f"shortest_clicks('A', 'D') = {shortest_clicks('A', 'D', example.get_linked_pages)}")
    print(f"shortest_path('HOME', 'E') = {shortest_path('HOME', 'E', site.get_linked_pages)}")
    print("All tests passed.")
</content>
