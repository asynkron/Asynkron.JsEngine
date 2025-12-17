using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a variable declaration (let/var/const).
/// </summary>
public sealed record VariableDeclaration(
    SourceReference? Source,
    VariableKind Kind,
    ImmutableArray<VariableDeclarator> Declarators) : StatementNode(Source);
