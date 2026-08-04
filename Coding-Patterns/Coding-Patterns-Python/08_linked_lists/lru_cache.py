# LeetCode 146 - LRU Cache
# Difficulty: Medium
# Pattern: HashMap + Doubly Linked List
#
# Problem: Design a data structure that follows LRU (Least Recently Used)
# cache eviction. Support get(key) and put(key, value) in O(1).
#
# Approach:
#   - HashMap: key -> node for O(1) lookup
#   - Doubly linked list: maintains usage order
#     - Most recently used near tail (dummy)
#     - Least recently used near head (dummy)
#   - On get/put: move accessed node to tail (most recent)
#   - On capacity exceeded: remove node at head (least recent)
#
# Time: O(1) for get and put  Space: O(capacity)

class DLinkedNode:
    def __init__(self, key=0, val=0):
        self.key = key
        self.val = val
        self.prev = None
        self.next = None

class LRUCache:
    def __init__(self, capacity: int):
        self.capacity = capacity
        self.cache = {}  # key -> node

        # Dummy head (LRU end) and tail (MRU end)
        self.head = DLinkedNode()
        self.tail = DLinkedNode()
        self.head.next = self.tail
        self.tail.prev = self.head

    def _remove(self, node: DLinkedNode):
        node.prev.next = node.next
        node.next.prev = node.prev

    def _insert_tail(self, node: DLinkedNode):
        node.prev = self.tail.prev
        node.next = self.tail
        self.tail.prev.next = node
        self.tail.prev = node

    def get(self, key: int) -> int:
        if key not in self.cache:
            return -1
        node = self.cache[key]
        self._remove(node)
        self._insert_tail(node)
        return node.val

    def put(self, key: int, value: int) -> None:
        if key in self.cache:
            self._remove(self.cache[key])
        node = DLinkedNode(key, value)
        self.cache[key] = node
        self._insert_tail(node)
        if len(self.cache) > self.capacity:
            lru = self.head.next
            self._remove(lru)
            del self.cache[lru.key]


# ---- Tests ----
if __name__ == "__main__":
    lru = LRUCache(2)
    lru.put(1, 1)
    lru.put(2, 2)
    assert lru.get(1) == 1       # returns 1, key 1 now MRU
    lru.put(3, 3)                # evicts key 2 (LRU)
    assert lru.get(2) == -1      # key 2 was evicted
    lru.put(4, 4)                # evicts key 1 (LRU)
    assert lru.get(1) == -1      # key 1 was evicted
    assert lru.get(3) == 3
    assert lru.get(4) == 4
    print("All tests passed.")
