
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents a variable declaration (let/var/const).
/// </summary>
public sealed record VariableDeclaration(
    SourceReference? Source,
    VariableKind Kind,
    ImmutableArray<VariableDeclarator> Declarators) : StatementNode(Source);
