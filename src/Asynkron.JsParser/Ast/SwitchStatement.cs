using System.Collections.Immutable;

namespace Asynkron.JsParser;

/// <summary>
///     Represents a switch statement with its cases.
/// </summary>
public sealed record SwitchStatement(
    SourceReference? Source,
    ExpressionNode Discriminant,
    ImmutableArray<SwitchCase> Cases) : StatementNode(Source);
