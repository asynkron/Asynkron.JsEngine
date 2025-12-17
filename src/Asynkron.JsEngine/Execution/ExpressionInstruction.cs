using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Evaluates an expression and exposes the result.
/// </summary>
internal sealed record ExpressionInstruction(int Next, ExpressionNode Expression) : GeneratorInstruction(Next);
