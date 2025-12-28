#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a return statement in the generator.
/// </summary>
/// <remarks>
///     The Next parameter is important for returns inside try/finally blocks.
///     When a return occurs inside a finally block, we need to continue to
///     EndFinallyInstruction to properly process the pending completion.
/// </remarks>
internal sealed record ReturnInstruction(int Next, ExpressionNode? ReturnExpression) : ExecutionInstruction(Next);
