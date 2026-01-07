using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

internal static class PoolDebug
{
    private sealed class LeaseState
    {
        public bool IsOwned;
    }

    private static readonly ConditionalWeakTable<object, LeaseState> Leases = new();

    [Conditional("DEBUG")]
    internal static void MarkLeased(object item)
    {
        var state = Leases.GetOrCreateValue(item);
        if (state.IsOwned)
        {
            throw new InvalidOperationException("Pooled object double lease detected.");
        }

        state.IsOwned = true;
    }

    [Conditional("DEBUG")]
    internal static void MarkReturned(object item)
    {
        if (!Leases.TryGetValue(item, out var state) || !state.IsOwned)
        {
            throw new InvalidOperationException("Pooled object returned without an active lease.");
        }

        state.IsOwned = false;
    }

    [Conditional("DEBUG")]
    internal static void AssertOwned(object item, string usage)
    {
        if (!Leases.TryGetValue(item, out var state))
        {
            return;
        }

        if (!state.IsOwned)
        {
            throw new InvalidOperationException($"Pooled object used without ownership ({usage}).");
        }
    }
}
