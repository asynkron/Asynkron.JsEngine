using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents the root program node.
/// </summary>
/// <param name="Source">Location of the original S-expression.</param>
/// <param name="Body">Statements that make up the program.</param>
/// <param name="IsStrict">Whether the program was prefixed with a "use strict" directive.</param>
/// <param name="HasTopLevelAwait">Whether the program contains top-level await expressions.</param>
public sealed record ProgramNode(
    SourceReference? Source,
    ImmutableArray<StatementNode> Body,
    bool IsStrict,
    bool HasTopLevelAwait = false)
    : AstNode(Source);
