
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents an object literal.
/// </summary>
public sealed record ObjectExpression(
    SourceReference? Source,
    ImmutableArray<ObjectMember> Members,
    bool HasCoverInitializedName = false)
    : ExpressionNode(Source);
