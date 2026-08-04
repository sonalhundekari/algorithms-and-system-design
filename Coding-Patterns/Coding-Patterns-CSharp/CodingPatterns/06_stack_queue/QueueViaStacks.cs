/*
    Implements a FIFO queue using two LIFO stacks.

    Why Dequeue is O(1) amortized despite an O(n) worst case: every element is pushed exactly twice 
    and popped exactly twice over its entire lifetime in the structure — once into _inbox/out of _inbox, 
    once into _outbox/out of _outbox. That's a constant amount of work per element, total. 
    So across any sequence of n operations, total work is O(n), giving O(1) per operation on average — even 
    though some individual operations (the ones that trigger a refill of _outbox) are O(n).
*/
using System;
using System.Collections.Generic;

namespace CodingPatterns.StackQueue;


// Implements a FIFO queue using two LIFO stacks.
// Core idea: pushing to a stack reverses order; reversing twice restores
// the original order. So we let items "fall through" inbox -> outbox
// exactly once each, and that single reversal gives us FIFO behavior
// out of two stacks that are individually LIFO.
public class QueueViaStacks<T>
{
    // inbox: receives all newly enqueued items. Top of inbox = most recently added item.
    private readonly Stack<T> _inbox = new();

    // outbox: holds items in dequeue-ready order. Top of outbox = next item to leave the queue.
    private readonly Stack<T> _outbox = new();

    // Total items currently stored across both stacks.
    // O(1) since Stack<T>.Count is O(1).
    public int Count => _inbox.Count + _outbox.Count;

    public bool IsEmpty => Count == 0;

    // Enqueue: always just push onto inbox.
    // We never touch outbox here, so this is O(1) worst case, no exceptions.
    public void Enqueue(T item)
    {
        _inbox.Push(item);
    }

    // Dequeue: remove and return the oldest item.
    // Complexity: O(1) amortized, O(n) worst case (see ShiftIfNeeded).
    public T Dequeue()
    {
        ShiftIfNeeded();
        return _outbox.Pop();
    }

    // Peek: look at the oldest item without removing it.
    // Same complexity profile as Dequeue.
    public T Peek()
    {
        ShiftIfNeeded();
        return _outbox.Peek();
    }

    // Ensures outbox has the next-to-dequeue item on top, refilling from inbox
    // only when outbox has been fully drained.
    //
    // Why only when outbox is empty?
    // - Items sitting in outbox are strictly older (were enqueued earlier)
    //   than anything currently in inbox.
    // - If we drained inbox into outbox while outbox still had items,
    //   we'd be putting newer items underneath older ones incorrectly
    //   interleaved — actually worse, we'd break ordering entirely since
    //   the newer batch would end up on top, jumping the queue.
    // - So we wait until outbox is fully consumed before doing another
    //   full transfer. This is what makes the amortized cost work out.
    private void ShiftIfNeeded()
    {
        if (_outbox.Count == 0)
        {
            if (_inbox.Count == 0)
                throw new InvalidOperationException("Queue is empty.");

            // Popping inbox gives newest-first; pushing each onto outbox
            // reverses that to oldest-first (oldest ends up on top).
            while (_inbox.Count > 0)
            {
                _outbox.Push(_inbox.Pop());
            }
        }
    }
}

public static class QueueViaStacksDemo
{
    public static void Run(string[] args)
    {
        var queue = new QueueViaStacks<int>();

        Console.WriteLine("Enqueue 1, 2, 3");
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        Console.WriteLine($"Peek: {queue.Peek()}");        // expect 1
        Console.WriteLine($"Dequeue: {queue.Dequeue()}");  // expect 1
        Console.WriteLine($"Dequeue: {queue.Dequeue()}");  // expect 2

        Console.WriteLine("Enqueue 4, 5 (interleaved with dequeues)");
        queue.Enqueue(4);
        queue.Enqueue(5);
        // outbox still has [3] on top at this point (from the initial drain),
        // inbox has [4, 5] pushed after — this exercises the "don't refill
        // early" branch of ShiftIfNeeded.

        Console.WriteLine($"Dequeue: {queue.Dequeue()}");  // expect 3 (last of original batch)
        Console.WriteLine($"Dequeue: {queue.Dequeue()}");  // expect 4 (triggers a refill from inbox)
        Console.WriteLine($"Dequeue: {queue.Dequeue()}");  // expect 5

        Console.WriteLine($"Queue empty? {queue.IsEmpty}"); // expect True

        try
        {
            queue.Dequeue(); // should throw
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Caught expected exception: {ex.Message}");
        }
    }
}
