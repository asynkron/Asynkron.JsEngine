#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a template literal expression.
/// </summary>
public sealed record TemplateLiteralExpression(SourceReference? Source, ImmutableArray<TemplatePart> Parts)
    : ExpressionNode(Source);
