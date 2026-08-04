using System;
using System.Collections.Generic;

namespace CodingPatterns.LinkedLists
{
    /// <summary>
    /// Tracks user logins and answers "earliest-logged-in user with exactly one login"
    /// in O(1) worst-case, while Record() is also O(1) worst-case.
    ///
    /// Design:
    ///   - Dictionary&lt;userId, Node&gt;  -> O(1) direct lookup of a user's list node.
    ///   - Doubly linked list (with sentinel head/tail) -> threads users in login order.
    ///   - HashSet&lt;userId&gt;            -> permanently marks users with 2+ logins.
    /// </summary>
    public class FirstUniqueTracker
    {
        private class Node
        {
            public int UserId;
            public Node Prev;
            public Node Next;
        }

        // Maps userId -> its node in the list (only while it's still "unique so far")
        private readonly Dictionary<int, Node> _nodeMap = new();

        // Users who have logged in 2+ times — permanently disqualified
        private readonly HashSet<int> _duplicates = new();

        // Sentinel head/tail keep insert/remove branch-free and O(1)
        private readonly Node _head = new();
        private readonly Node _tail = new();

        public FirstUniqueTracker()
        {
            _head.Next = _tail;
            _tail.Prev = _head;
        }

        public void Record(int userId)
        {
            if (_duplicates.Contains(userId))
                return; // already known-duplicate, no-op

            if (_nodeMap.TryGetValue(userId, out Node existing))
            {
                // 2nd login: promote to duplicate, evict from the list
                RemoveNode(existing);
                _nodeMap.Remove(userId);
                _duplicates.Add(userId);
            }
            else
            {
                // 1st login: append to tail (preserves arrival order)
                var node = new Node { UserId = userId };
                AppendNode(node);
                _nodeMap[userId] = node;
            }
        }

        public int? FirstUnique()
        {
            Node first = _head.Next;
            return first == _tail ? (int?)null : first.UserId;
        }

        private void AppendNode(Node node)
        {
            Node last = _tail.Prev;
            last.Next = node;
            node.Prev = last;
            node.Next = _tail;
            _tail.Prev = node;
        }

        private void RemoveNode(Node node)
        {
            node.Prev.Next = node.Next;
            node.Next.Prev = node.Prev;
        }
    }

    public static class FirstUniqueLoginTrackerDemo
    {
        public static void Run(string[] args)
        {
            var tracker = new FirstUniqueTracker();

            void Log(int userId)
            {
                tracker.Record(userId);
                Console.WriteLine($"Record({userId})");
            }

            void Query(string label)
            {
                int? result = tracker.FirstUnique();
                Console.WriteLine($"  -> FirstUnique() [{label}] = {(result.HasValue ? result.Value.ToString() : "null")}");
            }

            Console.WriteLine("=== First-Login Only-Once User Tracker Demo ===\n");

            Log(1);
            Log(2);
            Log(3);
            Query("after 1, 2, 3 each logged in once");   // expect 1

            Log(1); // user 1 logs in again -> becomes duplicate, evicted
            Query("after user 1 logs in again");          // expect 2

            Log(2); // user 2 logs in again -> becomes duplicate, evicted
            Query("after user 2 logs in again");           // expect 3

            Log(4);
            Log(5);
            Query("after users 4 and 5 log in");            // expect 3 (still earliest unique)

            Log(3); // user 3 logs in again -> becomes duplicate, evicted
            Query("after user 3 logs in again");            // expect 4

            Log(4); // user 4 logs in again -> becomes duplicate, evicted
            Log(5); // user 5 logs in again -> becomes duplicate, evicted
            Query("after users 4 and 5 both log in again"); // expect null (no unique users left)

            Log(6);
            Query("after a fresh user 6 logs in");          // expect 6

            Console.WriteLine("\n=== Demo complete ===");
        }
    }
}
