#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a throw statement in the generator.
///     Evaluates the expression and throws it as an exception.
/// </summary>
internal sealed record ThrowInstruction(ExpressionNode Expression) : GeneratorInstruction(-1);
