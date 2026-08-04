using System;
using System.Collections.Generic;

namespace CodingPatterns.Trees
{
    /// <summary>
    /// N-ary tree node. Each node has a value and an ordered list of children.
    /// </summary>
    public class Node
    {
        public int Val;
        public IList<Node> Children;

        public Node(int val)
        {
            Val = val;
            Children = new List<Node>();
        }
    }

    /// <summary>
    /// Compares two N-ary trees for structural + value equality.
    /// Child order is treated as significant: [2,3] != [3,2].
    /// </summary>
    public class NAryTreeComparer
    {
        /// <summary>
        /// Recursive comparison.
        ///
        /// Time complexity:  O(n), where n = total number of nodes in the
        ///                   smaller tree (each node visited at most once;
        ///                   short-circuits on first mismatch).
        /// Space complexity: O(h), where h = height of the tree, due to the
        ///                   recursive call stack. Worst case O(n) for a
        ///                   completely skewed (linked-list-like) tree.
        /// </summary>
        public bool IsEqualRecursive(Node p, Node q)
        {
            // Both null -> equal
            if (p == null && q == null) 
                return true;

            // One null, other not -> not equal
            if (p == null || q == null) 
                return false;

            // Values differ -> not equal
            if (p.Val != q.Val) 
                return false;

            // Different number of children -> not equal
            if (p.Children.Count != q.Children.Count) 
                return false;

            // Recursively compare each child pair, in order
            for (int i = 0; i < p.Children.Count; i++)
            {
                if (!IsEqualRecursive(p.Children[i], q.Children[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Iterative comparison using an explicit stack in place of the call stack.
        ///
        /// Time complexity:  O(n), where n = total number of nodes in the
        ///                   smaller tree (each node pushed/popped once;
        ///                   short-circuits on first mismatch).
        /// Space complexity: O(w), where w = maximum number of node-pairs
        ///                   sitting on the stack at once. Bounded by O(n)
        ///                   in the worst case (e.g. a very bushy/wide tree),
        ///                   versus O(h) for the recursive version. This
        ///                   trades call-stack depth risk (stack overflow on
        ///                   very deep trees) for explicit heap-allocated
        ///                   storage.
        /// </summary>
        public bool IsEqualIterative(Node p, Node q)
        {
            var stack = new Stack<(Node, Node)>();
            stack.Push((p, q));

            while (stack.Count > 0)
            {
                var (nodeP, nodeQ) = stack.Pop();

                // Both null -> continue checking rest of stack
                if (nodeP == null && nodeQ == null) 
                    continue;

                // One null, other not -> not equal
                if (nodeP == null || nodeQ == null) 
                    return false;

                // Values differ -> not equal
                if (nodeP.Val != nodeQ.Val) 
                    return false;

                // Different number of children -> not equal
                if (nodeP.Children.Count != nodeQ.Children.Count) 
                    return false;

                // Push each child pair onto the stack
                for (int i = 0; i < nodeP.Children.Count; i++)
                {
                    stack.Push((nodeP.Children[i], nodeQ.Children[i]));
                }
            }

            return true;
        }
    }

    public class NAryTreeComparerDemo
    {
        public static void Run()
        {
            // Tree 1: 1 -> [2, 3]
            var t1 = new Node(1);
            t1.Children.Add(new Node(2));
            t1.Children.Add(new Node(3));

            // Tree 2: identical structure to Tree 1
            var t2 = new Node(1);
            t2.Children.Add(new Node(2));
            t2.Children.Add(new Node(3));

            // Tree 3: different value in a child
            var t3 = new Node(1);
            t3.Children.Add(new Node(2));
            t3.Children.Add(new Node(99));

            var comparer = new NAryTreeComparer();

            Console.WriteLine("Recursive:");
            Console.WriteLine($"  t1 == t2 : {comparer.IsEqualRecursive(t1, t2)}"); // True
            Console.WriteLine($"  t1 == t3 : {comparer.IsEqualRecursive(t1, t3)}"); // False

            Console.WriteLine("Iterative:");
            Console.WriteLine($"  t1 == t2 : {comparer.IsEqualIterative(t1, t2)}"); // True
            Console.WriteLine($"  t1 == t3 : {comparer.IsEqualIterative(t1, t3)}"); // False

            // Complexity summary
            Console.WriteLine();
            Console.WriteLine("Complexity summary:");
            Console.WriteLine("  Recursive : Time O(n), Space O(h) [h = tree height, call stack]");
            Console.WriteLine("  Iterative : Time O(n), Space O(w) [w = max stack width, up to O(n)]");
        }
    }
}
