using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a tagged template literal expression.
/// </summary>
public sealed record TaggedTemplateExpression(
    SourceReference? Source,
    ExpressionNode Tag,
    ExpressionNode StringsArray,
    ExpressionNode RawStringsArray,
    ImmutableArray<ExpressionNode> Expressions)
    : ExpressionNode(Source);
