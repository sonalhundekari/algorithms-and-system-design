// LeetCode 21 - Merge Two Sorted Lists
// Difficulty: Easy
// Pattern: Dummy head + two pointers
//
// Time: O(m + n)  Space: O(1)

namespace CodingPatterns.LinkedLists;

public class ListNode
{
    public int Val;
    public ListNode Next;
    public ListNode(int val = 0, ListNode next = null) { Val = val; Next = next; }
}

public class MergeTwoSortedLists
{
    public ListNode MergeLists(ListNode list1, ListNode list2)
    {
        var dummy = new ListNode(0);
        var curr = dummy;

        while (list1 != null && list2 != null)
        {
            if (list1.Val <= list2.Val)
            { curr.Next = list1; list1 = list1.Next; }
            else
            { curr.Next = list2; list2 = list2.Next; }
            curr = curr.Next;
        }
        curr.Next = list1 ?? list2;
        return dummy.Next;
    }

    // ---- Tests ----
    public static void Run()
    {
        var sol = new MergeTwoSortedLists();

        var a = new ListNode(1, new ListNode(2, new ListNode(4)));
        var b = new ListNode(1, new ListNode(3, new ListNode(4)));
        Console.WriteLine(Format(sol.MergeLists(a, b)));            // 1,1,2,3,4,4

        Console.WriteLine(Format(sol.MergeLists(null, null)));      // (empty)
        Console.WriteLine(Format(sol.MergeLists(null, new ListNode(0))));  // 0
    }

    private static string Format(ListNode head)
    {
        var vals = new List<int>();
        for (var node = head; node != null; node = node.Next)
            vals.Add(node.Val);
        return string.Join(",", vals);
    }
}
