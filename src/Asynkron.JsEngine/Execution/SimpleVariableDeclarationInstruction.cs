#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Represents a simple variable declaration with an identifier binding (no destructuring).
///     Handles declarations like: let x = expr; const y = 5; var z = value;
/// </summary>
internal sealed record SimpleVariableDeclarationInstruction(
    int Next,
    VariableKind Kind,
    Symbol TargetSymbol,
    ExpressionNode? Initializer) : GeneratorInstruction(Next);
