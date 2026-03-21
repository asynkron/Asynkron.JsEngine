#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the root program node.
/// </summary>
/// <param name="Source">Location of the original S-expression.</param>
/// <param name="Body">Statements that make up the program.</param>
/// <param name="IsStrict">Whether the program was prefixed with a "use strict" directive.</param>
/// <param name="HasTopLevelAwait">Whether the program contains top-level await expressions.</param>
/// <param name="ScopeId">Unique ID for this program's scope (for slot-based variable access).</param>
/// <param name="SlotCount">Number of variable slots needed for this program's scope.</param>
public sealed partial record ProgramNode(
    SourceReference? Source,
    ImmutableArray<StatementNode> Body,
    bool IsStrict,
    bool HasTopLevelAwait = false,
    int ScopeId = -1,
    int SlotCount = -1)
    : AstNode(Source),
        IAstCacheable<ScriptPlanCache>;

public sealed partial record ProgramNode
{
    private ScriptPlanCache? _cachedScriptPlan;

    ScriptPlanCache IAstCacheable<ScriptPlanCache>.GetOrCreateCache()
    {
        return AstCache.GetOrCreate(ref _cachedScriptPlan, this,
            static program => ScriptPlanCache.Build(program), out _);
    }
}
