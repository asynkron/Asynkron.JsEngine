using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a yield expression. When executed, the generator returns control to the caller.
/// </summary>
internal sealed record YieldInstruction(int Next, ExpressionNode? YieldExpression) : GeneratorInstruction(Next);
