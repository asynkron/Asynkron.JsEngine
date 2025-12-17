using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a conditional branch.
/// </summary>
internal sealed record BranchInstruction(ExpressionNode Condition, int ConsequentIndex, int AlternateIndex)
    : GeneratorInstruction(-1);
