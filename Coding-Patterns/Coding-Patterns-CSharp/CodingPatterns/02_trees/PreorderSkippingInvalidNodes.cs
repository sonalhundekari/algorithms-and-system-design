// Pre-order Traversal Skipping Marked (Invalid) Nodes
// Difficulty: Easy / Medium  (N-ary pre-order, LC #589, with a predicate on emit)
// Pattern: DFS with a GATED VISIT -- recurse unconditionally, emit conditionally.
//
// A tree arrives as an ordered edge list of [parent, child] pairs. Some node ids
// are marked invalid. Return the pre-order traversal over the VALID nodes, where
// the children of an invalid node promote and attach to the nearest valid
// ancestor -- they are still visited, in the slot the invalid node itself would
// have occupied, in the original child order.
//
// The whole problem is one observation: "delete a node and contract its children
// into its parent" changes WHICH nodes are printed, not the ORDER in which the
// walk reaches them. A pre-order DFS already reaches every node in exactly the
// promoted order, so the contraction is free -- split the recursion step from
// the emit step and put the validity test on the emit alone:
//
//     void Visit(node) {
//         if (Valid(node)) output.Add(node);   // emit   <- GATED
//         foreach (child in children[node])    // recurse <- NEVER gated
//             Visit(child);
//     }
//
// Removing an element from the output does not reorder what comes after it,
// which is precisely what "promote in place" means.
//
//         0                edges = [[0,1],[0,2],[1,3],[1,4],[2,5],[2,6]]
//        / \               invalid = {1, 6}
//       1   2
//      / \  / \            walk: 0 emit | 1 skip -> 3 emit, 4 emit
//     3  4 5   6                 | 2 emit -> 5 emit, 6 skip
//                          -> [0, 3, 4, 2, 5]
//
//     the contracted tree, as a sanity check -- same sequence, never built:
//         0
//       / | \
//      3  4  2
//            |
//            5
//
// Traps:
//   - Do not build the contracted tree first. Re-parenting every child of every
//     invalid node onto its nearest valid ancestor is a correct second pass, but
//     it IS a second pass -- more code, more memory, and one more place to get
//     the child ORDER wrong. Gated emission needs neither.
//   - Do not gate the RECURSION on validity (`if (Valid(node)) foreach ...`).
//     That deletes whole subtrees instead of contracting one node -- a different
//     problem. An invalid node still has to hand its children down.
//   - Child order is the order the EDGES appear in the input, not sorted id
//     order. Appending to a List<int> as you scan the edges gives that for free;
//     a HashSet or a SortedDictionary of children throws it away.
//   - An invalid ROOT is not a special case: the output simply starts with the
//     root's children. Only the emit is skipped; the loop under it always runs.
//   - Iterative version: push children in REVERSE so they pop in input order.
//   - `invalid` must be a HashSet. A list makes the membership test O(k) and the
//     whole traversal O(n*k).
//
// Time: O(n + m) to build the child lists, O(n) to walk -- O(n) overall for a
//       tree, where m = n - 1.
// Space: O(n) for the child map and the output, plus O(h) for the stack.
//
// Depth note: a path-shaped tree makes h = n. The recursion is fine on the
// default 1 MB CLR stack for a few thousand nodes, but n in the 10^5 range
// overflows it -- and a StackOverflowException cannot be caught, it kills the
// process. The iterative version below is the one to reach for when the
// interviewer pushes on input size.

namespace CodingPatterns.Trees;

public class PreorderSkippingInvalidNodes
{
    // The one node that is never a child. Directed edge lists only.
    //
    // An undirected edge list carries no such asymmetry -- there the root has to
    // be given, and the DFS additionally needs a `parent` argument (or a visited
    // set) so it does not walk back up the edge it arrived on.
    public static int FindRoot(int n, int[][] edges)
    {
        var hasParent = new bool[n];
        foreach (var edge in edges) hasParent[edge[1]] = true;
        for (var node = 0; node < n; node++)
            if (!hasParent[node]) return node;
        return -1;   // a cycle, not a tree
    }

    // ---- Approach 1: recursive, gated emission ----
    public IList<int> Preorder(int n, int[][] edges, int root, IEnumerable<int> invalid)
    {
        var output = new List<int>();
        if (n <= 0) return output;

        // parent -> children, in the order the edges appear in the input.
        var children = new Dictionary<int, List<int>>();
        foreach (var edge in edges)
        {
            if (!children.TryGetValue(edge[0], out var list))
                children[edge[0]] = list = new List<int>();
            list.Add(edge[1]);
        }

        var bad = new HashSet<int>(invalid);
        if (root < 0) root = FindRoot(n, edges);
        if (root < 0) return output;

        void Visit(int node)
        {
            if (!bad.Contains(node))
                output.Add(node);                   // emit: gated on validity

            if (children.TryGetValue(node, out var kids))
                foreach (var child in kids)
                    Visit(child);                   // recurse: never gated
        }

        Visit(root);
        return output;
    }

    // ---- Approach 2: iterative, same gated emission, explicit O(h) stack ----
    // Push children in reverse so the leftmost child sits on top and pops first;
    // that single reverse loop is what preserves the input child order.
    public IList<int> PreorderIterative(int n, int[][] edges, int root,
                                        IEnumerable<int> invalid)
    {
        var output = new List<int>();
        if (n <= 0) return output;

        // parent -> children, in the order the edges appear in the input.
        var children = new Dictionary<int, List<int>>();
        foreach (var edge in edges)
        {
            if (!children.TryGetValue(edge[0], out var list))
                children[edge[0]] = list = new List<int>();
            list.Add(edge[1]);
        }

        var bad = new HashSet<int>(invalid);
        if (root < 0) root = FindRoot(n, edges);
        if (root < 0) return output;

        var stack = new Stack<int>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!bad.Contains(node))
                output.Add(node);

            if (children.TryGetValue(node, out var kids))
                for (var i = kids.Count - 1; i >= 0; i--)
                    stack.Push(kids[i]);
        }

        return output;
    }

    // ---- Approach 3: the explicit contraction, written out for comparison ----
    // What candidates usually reach for: rebuild the tree with the invalid nodes
    // spliced out, then run a plain pre-order. Correct, and strictly more work --
    // two passes, a second adjacency structure, and the child order has to be
    // reassembled by hand at every splice point. Kept only to show that it
    // produces the identical sequence.
    public IList<int> PreorderByContraction(int n, int[][] edges, int root,
                                            IEnumerable<int> invalid)
    {
        var output = new List<int>();
        if (n <= 0) return output;

        // parent -> children, in the order the edges appear in the input.
        var children = new Dictionary<int, List<int>>();
        foreach (var edge in edges)
        {
            if (!children.TryGetValue(edge[0], out var list))
                children[edge[0]] = list = new List<int>();
            list.Add(edge[1]);
        }

        var bad = new HashSet<int>(invalid);
        if (root < 0) root = FindRoot(n, edges);
        if (root < 0) return output;

        // This node's children after the invalid ones are spliced out, in order.
        List<int> ValidChildren(int node)
        {
            var result = new List<int>();
            if (children.TryGetValue(node, out var kids))
                foreach (var child in kids)
                {
                    if (bad.Contains(child))
                        result.AddRange(ValidChildren(child));   // promote, in place
                    else
                        result.Add(child);
                }
            return result;
        }

        var contracted = new Dictionary<int, List<int>>();
        for (var node = 0; node < n; node++) contracted[node] = ValidChildren(node);

        void Walk(int node)
        {
            output.Add(node);
            foreach (var child in contracted[node]) Walk(child);
        }

        foreach (var start in bad.Contains(root) ? ValidChildren(root) : new List<int> { root })
            Walk(start);

        return output;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new PreorderSkippingInvalidNodes();

        //         0
        //        / \
        //       1   2
        //      / \  / \
        //     3  4 5   6
        int[][] full = [[0, 1], [0, 2], [1, 3], [1, 4], [2, 5], [2, 6]];

        //          0
        //        / | \
        //       1  2  3        n-ary, uneven, 7 nodes
        //      /   |
        //     4    5
        //          |
        //          6
        int[][] nary = [[0, 1], [0, 2], [0, 3], [1, 4], [2, 5], [5, 6]];

        void Check(string label, int n, int[][] edges, int root, int[] invalid, int[] expected)
        {
            var rec = sol.Preorder(n, edges, root, invalid);
            var itr = sol.PreorderIterative(n, edges, root, invalid);
            var con = sol.PreorderByContraction(n, edges, root, invalid);
            var ok = rec.SequenceEqual(expected)
                     && itr.SequenceEqual(expected)
                     && con.SequenceEqual(expected);
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label,-26} [{string.Join(",", rec)}]" +
                              (ok ? "" : $"  expected [{string.Join(",", expected)}]" +
                                         $"  iterative [{string.Join(",", itr)}]" +
                                         $"  contracted [{string.Join(",", con)}]"));
        }

        Check("stated example", 7, full, 0, [1, 6], [0, 3, 4, 2, 5]);
        Check("nothing invalid", 7, full, 0, [], [0, 1, 3, 4, 2, 5, 6]);
        Check("invalid root", 7, full, 0, [0], [1, 3, 4, 2, 5, 6]);
        // Root plus two interior nodes gone: 1 and 2 promote their children to
        // the top level, and with 0 gone the output is the four leaves in order.
        Check("invalid root + 2 interior", 7, full, 0, [0, 1, 2], [3, 4, 5, 6]);
        Check("both interior, root kept", 7, full, 0, [1, 2], [0, 3, 4, 5, 6]);
        Check("all invalid", 7, full, 0, [0, 1, 2, 3, 4, 5, 6], []);
        Check("only leaves invalid", 7, full, 0, [3, 4, 5, 6], [0, 1, 2]);
        Check("single valid leaf", 7, full, 0, [0, 1, 2, 3, 5, 6], [4]);
        // 2 and 5 both go: 6 promotes all the way to the root, and it lands in
        // 2's old slot -- BEFORE sibling 3, not after it.
        Check("n-ary, chain contracted", 7, nary, 0, [2, 5], [0, 1, 4, 6, 3]);
        Check("n-ary, mid of chain gone", 7, nary, 0, [5], [0, 1, 4, 2, 6, 3]);
        Check("single node", 1, [], 0, [], [0]);
        Check("single node, invalid", 1, [], 0, [0], []);
        Check("empty tree", 0, [], 0, [], []);
        // Invalid ids that are not in the tree are simply never matched.
        Check("invalid ids off-tree", 7, full, 0, [42, 1], [0, 3, 4, 2, 5, 6]);

        // Root derived from the edge list (root = -1) rather than given. A
        // shuffled edge list still names 0 as root, and each parent's children
        // keep the order they appear in -- here 2 before 1 under the root.
        int[][] shuffled = [[2, 5], [0, 2], [1, 4], [0, 1], [2, 6], [1, 3]];
        var derived = FindRoot(7, full) == 0
                      && sol.Preorder(7, full, -1, [1, 6]).SequenceEqual([0, 3, 4, 2, 5])
                      && sol.Preorder(7, shuffled, -1, []).SequenceEqual([0, 2, 5, 6, 1, 4, 3]);
        Console.WriteLine($"{(derived ? "PASS" : "FAIL")}  {"root derived from edges",-26} 0");

        // Depth: a 100,000-node path. Every other node is invalid, so the answer
        // is the 50,000 even ids. The recursive version overflows the CLR stack
        // here; the iterative one does not care.
        const int depth = 100_000;
        var chain = new int[depth - 1][];
        for (var i = 0; i < depth - 1; i++) chain[i] = [i, i + 1];
        var odds = new List<int>();
        for (var i = 1; i < depth; i += 2) odds.Add(i);

        var deep = sol.PreorderIterative(depth, chain, 0, odds);
        var deepOk = deep.Count == depth / 2 && deep[0] == 0 && deep[^1] == depth - 2;
        Console.WriteLine($"{(deepOk ? "PASS" : "FAIL")}  {"deep chain (100k nodes)",-26} {deep.Count} kept");
    }
}
