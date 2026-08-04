# LeetCode 127 - Word Ladder
# Difficulty: Hard
# Pattern: BFS shortest path
#
# Problem: Given beginWord, endWord, and a wordList, return the number
# of words in the shortest transformation sequence from beginWord to
# endWord, where each adjacent pair differs by one letter.
#
# Approach: BFS where each neighbor is a word differing by one char.
# Use wildcard patterns as keys to quickly find neighbors.
# e.g. "hit" -> "*it", "h*t", "hi*" — group words by pattern.
#
# Time: O(M^2 * N)  M = word length, N = wordList size
# Space: O(M^2 * N)

from typing import List
from collections import defaultdict, deque

def ladder_length(beginWord: str, endWord: str, wordList: List[str]) -> int:
    word_set = set(wordList)
    if endWord not in word_set:
        return 0

    # Build pattern -> list of words map
    pattern_map = defaultdict(list)
    for word in wordList:
        for i in range(len(word)):
            pattern = word[:i] + '*' + word[i+1:]
            pattern_map[pattern].append(word)

    queue = deque([(beginWord, 1)])
    visited = {beginWord}

    while queue:
        word, length = queue.popleft()
        for i in range(len(word)):
            pattern = word[:i] + '*' + word[i+1:]
            for neighbor in pattern_map[pattern]:
                if neighbor == endWord:
                    return length + 1
                if neighbor not in visited:
                    visited.add(neighbor)
                    queue.append((neighbor, length + 1))

    return 0


# ---- Tests ----
if __name__ == "__main__":
    assert ladder_length("hit", "cog", ["hot","dot","dog","lot","log","cog"]) == 5
    assert ladder_length("hit", "cog", ["hot","dot","dog","lot","log"]) == 0
    print("All tests passed.")
