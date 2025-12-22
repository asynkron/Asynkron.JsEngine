#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks the beginning of a <c>with</c> statement. Evaluates the object expression
///     and pushes a with-environment onto the scope chain.
/// </summary>
internal sealed record EnterWithInstruction(
    ExpressionNode ObjectExpression,
    Symbol WithScopeSlot,
    int Next)
    : GeneratorInstruction(Next);
