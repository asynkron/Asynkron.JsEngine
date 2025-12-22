#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a class expression that evaluates to a constructor function.
/// </summary>
public sealed record ClassExpression(SourceReference? Source, Symbol? Name, ClassDefinition Definition)
    : ExpressionNode(Source);
