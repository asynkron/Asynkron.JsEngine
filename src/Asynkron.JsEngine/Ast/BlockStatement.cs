#region

using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a block statement with optional strict mode.
/// </summary>
public sealed record BlockStatement(SourceReference? Source, ImmutableArray<StatementNode> Statements, bool IsStrict)
    : StatementNode(Source), IAstCacheable<HoistPlan>, IAstCacheable<HoistableDeclarationsPlan>
{
    private HoistableDeclarationsPlan? _cachedHoistableDeclarations;
    private HoistPlan? _cachedHoistPlan;
    private int _containsDynamicScopeCache = -1; // -1 unknown, 0 false, 1 true
    private int _containsInnerFunctionCache = -1; // -1 unknown, 0 false, 1 true

    internal int ScopeId { get; init; } = -1;
    internal int SlotCount { get; init; } = -1;

    internal ImmutableDictionary<Symbol, int> SlotMap { get; init; } =
        ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);

    HoistableDeclarationsPlan IAstCacheable<HoistableDeclarationsPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedHoistableDeclarations, this,
            static block => HoistableDeclarationsPlan.Build(block));
    }

    HoistPlan IAstCacheable<HoistPlan>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedHoistPlan, this, static block => HoistPlan.Build(block));
    }

    internal bool TryGetContainsInnerFunction(out bool contains)
    {
        var value = Volatile.Read(ref _containsInnerFunctionCache);
        if (value == -1)
        {
            contains = default;
            return false;
        }

        contains = value == 1;
        return true;
    }

    internal void CacheContainsInnerFunction(bool contains)
    {
        var value = contains ? 1 : 0;
        _ = Interlocked.CompareExchange(ref _containsInnerFunctionCache, value, -1);
    }

    internal bool TryGetContainsDynamicScope(out bool contains)
    {
        var value = Volatile.Read(ref _containsDynamicScopeCache);
        if (value == -1)
        {
            contains = default;
            return false;
        }

        contains = value == 1;
        return true;
    }

    internal void CacheContainsDynamicScope(bool contains)
    {
        var value = contains ? 1 : 0;
        _ = Interlocked.CompareExchange(ref _containsDynamicScopeCache, value, -1);
    }
}
