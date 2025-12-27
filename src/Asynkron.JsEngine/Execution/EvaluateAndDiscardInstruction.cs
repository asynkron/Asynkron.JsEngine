#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents an expression statement in the generator.
///     Evaluates the expression and discards the result.
/// </summary>
internal sealed record EvaluateAndDiscardInstruction(int Next, ExpressionNode Expression) : ExecutionInstruction(Next);
