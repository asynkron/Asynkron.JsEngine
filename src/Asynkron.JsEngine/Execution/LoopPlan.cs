using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal enum LoopKind
{
    While,
    DoWhile,
    For
}

/// <summary>
/// Normalized description of a loop that flattens initializer/test/body/increment
/// into explicit statement lists the IR builder can consume without re-parsing
/// individual loop syntaxes.
/// </summary>
internal sealed record LoopPlan(
    LoopKind Kind,
    ImmutableArray<StatementNode> LeadingStatements,
    ImmutableArray<StatementNode> ConditionPrologue,
    ExpressionNode Condition,
    BlockStatement Body,
    ImmutableArray<StatementNode> PostIteration,
    bool ConditionAfterBody);
