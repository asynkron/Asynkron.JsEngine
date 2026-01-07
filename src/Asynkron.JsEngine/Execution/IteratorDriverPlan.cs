#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

internal sealed record IteratorDriverPlan(
    IteratorDriverKind Kind,
    ExpressionNode Iterable,
    BindingTarget Target,
    VariableKind? DeclarationKind,
    BlockStatement Body,
    int IterationScopeId = -1,
    int IterationParentScopeId = -1,
    int IterationSlotCount = -1,
    ImmutableArray<int> PerIterationSlotIndices = default,
    ImmutableArray<Symbol> PerIterationBindings = default,
    bool CanReuseIterationEnvironment = false)
{
    private sealed record SlotMapCache(int RequiredSlots, ImmutableDictionary<Symbol, int>? SlotMap);

    // Cached slot map and required slot count - computed once, reused every iteration
    private SlotMapCache? _slotMapCache;

    /// <summary>
    /// Gets or creates the cached slot map for iteration environments.
    /// The slot map is identical for every iteration, so we compute it once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImmutableDictionary<Symbol, int>? GetOrCreateSlotMap()
    {
        var cached = Volatile.Read(ref _slotMapCache);
        if (cached is not null)
        {
            return cached.SlotMap;
        }

        BuildSlotMapCache();
        return _slotMapCache?.SlotMap;
    }

    /// <summary>
    /// Gets the required slot count for iteration environments.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetRequiredSlots()
    {
        var cached = Volatile.Read(ref _slotMapCache);
        if (cached is not null)
        {
            return cached.RequiredSlots;
        }

        BuildSlotMapCache();
        return _slotMapCache?.RequiredSlots ?? -2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildSlotMapCache()
    {
        if (Volatile.Read(ref _slotMapCache) is not null)
        {
            return;
        }

        if (IterationScopeId < 0 || PerIterationBindings.IsDefaultOrEmpty)
        {
            Interlocked.CompareExchange(ref _slotMapCache, new SlotMapCache(-1, null), null);
            return;
        }

        var slotIndices = PerIterationSlotIndices;
        var bindings = PerIterationBindings;
        var slotMapBuilder = ImmutableDictionary.CreateBuilder<Symbol, int>();
        var maxSlotIndex = -1;

        for (var i = 0; i < bindings.Length; i++)
        {
            var slotIndex = !slotIndices.IsDefaultOrEmpty && slotIndices.Length > i && slotIndices[i] >= 0
                ? slotIndices[i]
                : i;
            slotMapBuilder[bindings[i]] = slotIndex;
            if (slotIndex > maxSlotIndex)
            {
                maxSlotIndex = slotIndex;
            }
        }

        var requiredSlots = Math.Max(IterationSlotCount, maxSlotIndex + 1);
        if (requiredSlots < 0)
        {
            requiredSlots = maxSlotIndex + 1;
        }

        var cache = new SlotMapCache(requiredSlots, slotMapBuilder.ToImmutable());
        Interlocked.CompareExchange(ref _slotMapCache, cache, null);
    }
}
