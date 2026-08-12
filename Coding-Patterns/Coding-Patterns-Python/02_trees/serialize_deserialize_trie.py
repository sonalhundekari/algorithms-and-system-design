# Serialize and Deserialize Dictionary Trie
# Difficulty: Medium
# Pattern: Preorder DFS with an explicit "close" marker; stack-based decode
#
# serialize(words) -> one string.  deserialize(data) -> the same words, sorted.
#
# THE INSIGHT. A trie is just an n-ary tree, and the classic n-ary trick applies:
# a preorder walk alone is ambiguous ("ab" could be a->b or two siblings), so emit
# ONE extra token per node saying "this subtree is finished". With that token the
# preorder string is uniquely decodable by a stack — no lookahead, no recursion on
# the way back in, no separators between letters.
#
# THE GRAMMAR (three token kinds, one character each):
#
#     a..z   descend: create this child of the current node and move into it
#     '$'    the current node ends a word
#     ')'    close the current node and return to its parent
#
# ["app", "apple", "bat"]  ->  "app$le$)))))bat$)))"
#
#     root
#      +- a          "a"
#      |   +- p      "p"
#      |       +- p* "p$"        <- app
#      |           +- l      "l"
#      |               +- e* "e$"   <- apple
#      +- b ...
#
# Reading it back, ')' pops and everything else pushes or flags — so the decoder is
# five lines and has no parsing state beyond the stack.
#
# WHY CHILDREN ARE EMITTED IN SORTED ORDER. Two payoffs for one `sorted()` over at
# most 26 keys: the encoding becomes CANONICAL (input order cannot change the
# string, so two dictionaries with the same words serialize identically and can be
# compared or deduped by bytes), and the decoded trie is already in lexicographic
# order, so collecting the words is a plain preorder walk with no final sort.
#
# A prefix word lands in the right place for free: a node is emitted before its
# children, so "app" precedes "apple" without a special case.
#
#   Time:  O(N) both ways, N = number of trie nodes <= sum of word lengths
#   Space: O(N) for the trie; the string is 2N + (#words) characters
#
# WHAT TO ASK BEFORE WRITING ANYTHING:
#
#   1. Is the alphabet really lowercase a-z? That is what lets '$' and ')' be
#      unescaped literals. Any other alphabet needs escaping — see the follow-ups.
#   2. Is "" a possible word? The format handles it (a leading '$' flags the root),
#      but the stated limits say length >= 1.
#   3. Must the format be the trie, or is any round-trip allowed? "\n".join(words)
#      also passes the test; the trie encoding is what pays off on shared prefixes.

from typing import Dict, Iterable, List

WORD_END = "$"   # the node above me terminates a word
NODE_END = ")"   # pop back to the parent


class TrieNode:
    __slots__ = ("children", "is_word")

    def __init__(self) -> None:
        self.children: Dict[str, "TrieNode"] = {}
        self.is_word = False


class Codec:
    def serialize(self, words: Iterable[str]) -> str:
        """Build a trie from `words` and encode it as a single string."""
        out: List[str] = []
        self._encode(self.build(words), out)
        return "".join(out)

    def deserialize(self, data: str) -> List[str]:
        """Rebuild the trie from `data` and return its words, sorted."""
        words: List[str] = []
        self._collect(self.decode(data), [], words)
        return words

    # ---- trie construction ----

    @staticmethod
    def build(words: Iterable[str]) -> TrieNode:
        root = TrieNode()
        for word in words:
            node = root
            for ch in word:
                node = node.children.setdefault(ch, TrieNode())
            node.is_word = True
        return root

    @staticmethod
    def decode(data: str) -> TrieNode:
        """The inverse of _encode: ')' pops, a letter pushes, '$' flags."""
        root = TrieNode()
        stack = [root]
        for ch in data:
            if ch == NODE_END:
                if len(stack) == 1:
                    raise ValueError("malformed encoding: unmatched ')'")
                stack.pop()
            elif ch == WORD_END:
                stack[-1].is_word = True
            else:
                child = TrieNode()
                stack[-1].children[ch] = child   # keys arrive already sorted
                stack.append(child)
        if len(stack) != 1:
            raise ValueError("malformed encoding: unclosed node")
        return root

    # ---- the two walks ----

    # Depth is bounded by the longest word (<= 50 here), so recursion is safe;
    # an arbitrary-depth trie wants the same explicit stack `decode` already uses.
    def _encode(self, node: TrieNode, out: List[str]) -> None:
        if node.is_word:
            out.append(WORD_END)                 # at the root this encodes ""
        for ch in sorted(node.children):         # <= 26 keys: sorting is O(1)
            out.append(ch)
            self._encode(node.children[ch], out)
            out.append(NODE_END)                 # the root is never closed

    def _collect(self, node: TrieNode, prefix: List[str], words: List[str]) -> None:
        if node.is_word:
            words.append("".join(prefix))        # emit before descending: "app" < "apple"
        for ch in sorted(node.children):
            prefix.append(ch)
            self._collect(node.children[ch], prefix, words)
            prefix.pop()


# ---- Tests ----
if __name__ == "__main__":
    import random

    codec = Codec()

    assert codec.deserialize(codec.serialize(["app", "apple", "bat"])) == [
        "app", "apple", "bat"]
    assert codec.deserialize(codec.serialize(["dog", "deer", "deal"])) == [
        "deal", "deer", "dog"]

    # The encoding itself, spelled out — the shape is the point of the problem.
    assert codec.serialize(["app", "apple", "bat"]) == "app$le$)))))bat$)))"

    for words in [
        [],                                  # empty dictionary -> empty string
        ["a"],                               # single letter
        ["a", "ab", "abc", "abcd"],          # every node on the chain is a word
        ["abcd"],                            # no node on the chain is a word
        ["cat", "car", "card", "care", "dog"],
        [chr(ord("a") + i) for i in range(26)],   # full 26-way fan-out at the root
        ["z" * 50, "z" * 49],                # max depth, one a prefix of the other
    ]:
        assert codec.deserialize(codec.serialize(words)) == sorted(words), words

    assert codec.serialize([]) == ""
    assert codec.deserialize("") == []

    # The format can represent the empty word even though the limits exclude it.
    assert codec.deserialize(codec.serialize(["", "a"])) == ["", "a"]

    # Malformed input is rejected rather than silently decoded.
    for bad in ["ab))))", "abc"]:
        try:
            codec.decode(bad)
            raise AssertionError(f"expected {bad!r} to be rejected")
        except ValueError:
            pass

    # Randomized round-trip, plus the canonical-encoding property: the string must
    # depend on the SET of words, never on the order they were inserted.
    random.seed(208)
    for _ in range(500):
        pool = {
            "".join(random.choice("abcde") for _ in range(random.randint(1, 6)))
            for _ in range(random.randint(0, 40))
        }
        words = list(pool)
        encoded = codec.serialize(words)
        assert codec.deserialize(encoded) == sorted(words)
        random.shuffle(words)
        assert codec.serialize(words) == encoded

    # Shared prefixes are where the trie encoding beats "\n".join(words).
    dense = [f"internationalization{i:04d}" for i in range(500)]
    assert len(codec.serialize(dense)) < len("\n".join(dense))

    print(f'Serialized ["app","apple","bat"]: {codec.serialize(["app", "apple", "bat"])}')
    print("All tests passed.")

# ---- Follow-ups ----
#
# "Why not just join the words with newlines?"  For a pure round-trip, nothing —
# it is simpler and, for a dictionary with little prefix overlap, SHORTER. The
# trie encoding costs 2 chars per node and the word list costs 1 char per
# character, so the trie wins exactly when nodes < total characters, i.e. when
# prefixes are genuinely shared. Say this out loud rather than pretending the
# trie is unconditionally better.
#
# "The alphabet is arbitrary (unicode, digits, punctuation)."  '$' and ')' stop
# being safe sentinels. Either escape them (prefix a reserved char, and escape the
# escape) or move to a length-prefixed/binary framing: per node write the child
# count, then each child's code point and subtree. Escaping keeps the string
# human-readable; framing avoids the O(n) rescan for escapes.
#
# "Ten million words, and the file has to be small."  Two structural wins beyond
# this format. A DAWG (minimized DFA) also shares SUFFIXES — "-ing", "-tion" —
# collapsing the trie into a graph and often shrinking it by an order of
# magnitude; build it by hashing subtrees bottom-up and interning duplicates.
# A succinct/LOUDS encoding stores the same trie in ~2 bits + 1 label per node
# and is searchable without decompressing.
#
# "Deserialize lazily / query without rebuilding."  Since children are sorted and
# each subtree is a contiguous span of the string, you can walk the encoding
# directly: to follow a letter, scan the current node's children and skip past
# unwanted subtrees by counting ')' against letters. That gives prefix search over
# the serialized bytes with O(1) memory — the reason the closing marker is worth
# its byte.
#
# "The trie is deeper than the recursion limit."  `decode` is already iterative.
# Make `_encode` and `_collect` match by pushing (node, iterator) pairs onto an
# explicit stack; the emit-on-the-way-down / close-on-the-way-up structure is
# unchanged.
