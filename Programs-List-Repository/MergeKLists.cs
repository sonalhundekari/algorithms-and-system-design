/*
Merge k sorted linked lists and return it as one sorted list.

Time Complexity: O(N log k) - where N is the total number of nodes in all lists and k is the number of linked lists.
        Each node is inserted into the priority queue once, and the priority queue has a size of at most k, so each insertion takes O(log k) time.
space Complexity: O(k) - The priority queue can hold at most k nodes at any time, where k is the number of linked lists.
*/

namespace CodingPatterns.LinkedLists;

public class ListNode<T> {
    public T val;
    public ListNode<T> next;
    public ListNode(T val = default, ListNode<T> next = null) {
        this.val = val;
        this.next = next;
    }
}

public class MergeKLists<T> where T : IComparable<T> {
    public ListNode<T> Merge(ListNode<T>[] lists) {
        if (lists == null || lists.Length == 0) 
            return null;

        // Min-heap ordered by node value; tie-break with an insertion index
        // to keep comparisons stable (PriorityQueue requires a comparable key).
        var pq = new PriorityQueue<ListNode<T>, (T val, int order)>();

        int order = 0;
        foreach (var node in lists) {
            if (node != null) {
                pq.Enqueue(node, (node.val, order++));
            }
        }

        var dummy = new ListNode<T>();
        var tail = dummy;

        while (pq.Count > 0) {
            var node = pq.Dequeue();
            tail.next = node;
            tail = tail.next;

            if (node.next != null) {
                pq.Enqueue(node.next, (node.next.val, order++));
            }
        }

        tail.next = null;
        return dummy.next;
    }

    /*
    Recursive divide-and-conquer: pair up lists and merge each pair, halving
    the number of lists every round until only one remains.

    Time Complexity: O(N log k) - there are log k rounds of pairwise merges,
            and each round merges a total of N nodes across all pairs.
    Space Complexity: O(log k) - for the recursion call stack (plus O(1)
            extra space per merge, since MergeTwoLists is iterative and
            reuses existing nodes rather than allocating new ones).
    */
    public ListNode<T> MergeKListsRecursive(ListNode<T>[] lists) {
        if (lists == null || lists.Length == 0) return null;
        return Merge(lists, 0, lists.Length - 1);
    }

    private ListNode<T> Merge(ListNode<T>[] lists, int left, int right) {
        if (left == right) return lists[left];

        int mid = left + (right - left) / 2;
        var leftMerged = Merge(lists, left, mid);
        var rightMerged = Merge(lists, mid + 1, right);
        return MergeTwoLists(leftMerged, rightMerged);
    }

    private ListNode<T> MergeTwoLists(ListNode<T> a, ListNode<T> b) {
        var dummy = new ListNode<T>();
        var tail = dummy;

        while (a != null && b != null) {
            if (a.val.CompareTo(b.val) <= 0) {
                tail.next = a;
                a = a.next;
            } else {
                tail.next = b;
                b = b.next;
            }
            tail = tail.next;
        }

        tail.next = a ?? b;
        return dummy.next;
    }
}

// Run() cannot live on MergeKLists<T> itself: an open generic type has no
// invocable static entry point, so the demo gets its own closed host class.
public static class MergeKListsDemo
{
    public static void Main() {
        var solver = new MergeKLists<int>();

        // Each solver rewires the input nodes, so every run gets fresh lists.
        ListNode<int>[] Build(params int[][] values) =>
            values.Select(FromArray).ToArray();

        var inputs = new[] {
            new[] { new[] { 1, 4, 5 }, new[] { 1, 3, 4 }, new[] { 2, 6 } },  // the example
            Array.Empty<int[]>(),                                            // k == 0
            new[] { Array.Empty<int>() },                                    // one empty list
            new[] { Array.Empty<int>(), new[] { 1 }, Array.Empty<int>() },   // empties around content
            new[] { new[] { 2, 2 }, new[] { 2 }, new[] { 2, 2, 2 } },        // all ties
            new[] { new[] { -10000, 0 }, new[] { -5, 10000 } },              // value range edges
        };

        foreach (var values in inputs) {
            var heap = ToArray(solver.Merge(Build(values)));
            var divide = ToArray(solver.MergeKListsRecursive(Build(values)));
            Console.WriteLine($"[{string.Join(" ", heap)}]  heap == divide&conquer: {heap.SequenceEqual(divide)}");
        }
    }

    private static ListNode<int> FromArray(int[] values) {
        var dummy = new ListNode<int>();
        var tail = dummy;
        foreach (var v in values)
            tail = tail.next = new ListNode<int>(v);
        return dummy.next;
    }

    private static List<int> ToArray(ListNode<int> head) {
        var values = new List<int>();
        for (var node = head; node != null; node = node.next)
            values.Add(node.val);
        return values;
    }
}
