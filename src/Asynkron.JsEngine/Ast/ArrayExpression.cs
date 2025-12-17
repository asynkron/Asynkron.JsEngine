using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an array literal.
/// </summary>
public sealed record ArrayExpression(SourceReference? Source, ImmutableArray<ArrayElement> Elements)
    : ExpressionNode(Source);
