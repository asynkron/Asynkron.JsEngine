using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.StdLib;

internal static class AtomicsWaiterManager
{
    private static readonly ConditionalWeakTable<JsArrayBuffer, WaiterTable> Tables = new();

    internal sealed class Waiter : IDisposable
    {
        private int _state;

        internal Waiter()
        {
            Semaphore = new SemaphoreSlim(0, 1);
        }

        /// <summary>
        /// SemaphoreSlim instead of ManualResetEventSlim for better thread pool behavior.
        /// WaitAsync() frees the thread during the wait; Wait() still blocks but participates
        /// in .NET's thread pool work-stealing.
        /// </summary>
        internal SemaphoreSlim Semaphore { get; }

        internal LinkedListNode<Waiter>? Node { get; set; }

        internal TaskCompletionSource<JsValue>? PromiseCompletionSource { get; set; }

        internal CancellationTokenSource? TimeoutTokenSource { get; set; }

        internal bool IsNotified => Volatile.Read(ref _state) == 1;

        internal void Notify()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                return;
            }

            try
            {
                TimeoutTokenSource?.Cancel();
            }
            catch
            {
                // Ignore cancellation races.
            }

            try { Semaphore.Release(); } catch (SemaphoreFullException) { }
            PromiseCompletionSource?.TrySetResult((JsValue)"ok");
        }

        internal bool TryTimeout()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
            {
                return false;
            }

            PromiseCompletionSource?.TrySetResult((JsValue)"timed-out");
            return true;
        }

        public void Dispose()
        {
            try
            {
                TimeoutTokenSource?.Dispose();
            }
            catch
            {
                // Ignore disposal races.
            }

            TimeoutTokenSource = null;
            Semaphore.Dispose();
        }
    }

    internal static Waiter EnqueueWaiter(JsArrayBuffer buffer, int byteOffset)
    {
        lock (buffer)
        {
            var table = Tables.GetValue(buffer, static _ => new WaiterTable());
            var list = GetOrCreateWaiterList(table, byteOffset);

            var waiter = new Waiter();
            waiter.Node = list.AddLast(waiter);
            return waiter;
        }
    }

    internal static void DequeueWaiter(JsArrayBuffer buffer, int byteOffset, Waiter waiter)
    {
        lock (buffer)
        {
            var node = waiter.Node;
            if (node is null)
            {
                return;
            }

            var list = node.List;
            if (list is null)
            {
                waiter.Node = null;
                return;
            }

            list.Remove(node);
            waiter.Node = null;

            if (list.Count != 0)
            {
                return;
            }

            if (Tables.TryGetValue(buffer, out var table) &&
                table.Waiters.TryGetValue(byteOffset, out var existing) &&
                ReferenceEquals(existing, list))
            {
                table.Waiters.Remove(byteOffset);
            }
        }
    }

    internal static int Notify(JsArrayBuffer buffer, int byteOffset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        lock (buffer)
        {
            if (!Tables.TryGetValue(buffer, out var table))
            {
                return 0;
            }

            if (!table.Waiters.TryGetValue(byteOffset, out var list) || list.Count == 0)
            {
                return 0;
            }

            var notified = 0;
            while (notified < count && list.First is { } node)
            {
                list.RemoveFirst();
                node.Value.Node = null;
                node.Value.Notify();
                notified++;
            }

            if (list.Count == 0)
            {
                table.Waiters.Remove(byteOffset);
            }

            return notified;
        }
    }

    private static LinkedList<Waiter> GetOrCreateWaiterList(WaiterTable table, int byteOffset)
    {
        if (table.Waiters.TryGetValue(byteOffset, out var existing))
        {
            return existing;
        }

        var list = new LinkedList<Waiter>();
        table.Waiters.Add(byteOffset, list);
        return list;
    }

    private sealed class WaiterTable
    {
        internal readonly Dictionary<int, LinkedList<Waiter>> Waiters = new();
    }
}
