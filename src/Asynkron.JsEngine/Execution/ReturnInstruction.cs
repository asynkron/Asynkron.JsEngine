#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a return statement in the generator.
/// </summary>
internal sealed record ReturnInstruction(ExpressionNode? ReturnExpression) : GeneratorInstruction(-1);
