using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an object literal.
/// </summary>
public sealed record ObjectExpression(
    SourceReference? Source,
    ImmutableArray<ObjectMember> Members,
    bool HasCoverInitializedName = false)
    : ExpressionNode(Source);
