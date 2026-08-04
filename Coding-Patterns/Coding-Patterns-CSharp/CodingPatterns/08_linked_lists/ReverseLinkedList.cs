// LeetCode 206 - Reverse Linked List
// Difficulty: Easy
// Pattern: Iterative prev/curr/next dance
//
// Time: O(n)  Space: O(1)

namespace CodingPatterns.LinkedLists;

public class ReverseLinkedList
{
    public ListNode Reverse(ListNode head)
    {
        ListNode prev = null, curr = head;
        while (curr != null)
        {
            var next = curr.Next;
            curr.Next = prev;
            prev = curr;
            curr = next;
        }
        return prev;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new ReverseLinkedList();

        var head = new ListNode(1, new ListNode(2, new ListNode(3,
                       new ListNode(4, new ListNode(5)))));
        Console.WriteLine(Format(sol.Reverse(head)));    // 5,4,3,2,1

        Console.WriteLine(Format(sol.Reverse(new ListNode(1))));  // 1
        Console.WriteLine(Format(sol.Reverse(null)));             // (empty)
    }

    private static string Format(ListNode head)
    {
        var vals = new List<int>();
        for (var node = head; node != null; node = node.Next)
            vals.Add(node.Val);
        return string.Join(",", vals);
    }
}
