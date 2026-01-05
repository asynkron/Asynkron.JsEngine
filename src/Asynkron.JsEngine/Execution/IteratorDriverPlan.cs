#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    // Cached slot map and required slot count - computed once, reused every iteration
    private ImmutableDictionary<Symbol, int>? _cachedSlotMap;
    private int _cachedRequiredSlots = -2; // -2 = not computed, -1 = no slots needed

    /// <summary>
    /// Gets or creates the cached slot map for iteration environments.
    /// The slot map is identical for every iteration, so we compute it once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImmutableDictionary<Symbol, int>? GetOrCreateSlotMap()
    {
        if (_cachedSlotMap is not null || _cachedRequiredSlots != -2)
            return _cachedSlotMap;

        BuildSlotMapCache();
        return _cachedSlotMap;
    }

    /// <summary>
    /// Gets the required slot count for iteration environments.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetRequiredSlots()
    {
        if (_cachedRequiredSlots != -2)
            return _cachedRequiredSlots;

        BuildSlotMapCache();
        return _cachedRequiredSlots;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildSlotMapCache()
    {
        if (IterationScopeId < 0 || PerIterationBindings.IsDefaultOrEmpty)
        {
            _cachedRequiredSlots = -1;
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
                maxSlotIndex = slotIndex;
        }

        var requiredSlots = Math.Max(IterationSlotCount, maxSlotIndex + 1);
        if (requiredSlots < 0)
            requiredSlots = maxSlotIndex + 1;

        _cachedRequiredSlots = requiredSlots;
        _cachedSlotMap = slotMapBuilder.ToImmutable();
    }
}
