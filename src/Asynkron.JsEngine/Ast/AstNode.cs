using System.Collections.Immutable;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Base type for every node in the typed abstract syntax tree.
/// Using records keeps value semantics while allowing pattern matching.
/// </summary>
public abstract record AstNode(SourceReference? Source);

/// <summary>
/// Base type for statements.
/// </summary>
public abstract record StatementNode(SourceReference? Source) : AstNode(Source);

/// <summary>
/// Base type for expressions.
/// </summary>
public abstract record ExpressionNode(SourceReference? Source) : AstNode(Source);

/// <summary>
/// Represents the root program node.
/// </summary>
/// <param name="Source">Location of the original S-expression.</param>
/// <param name="Body">Statements that make up the program.</param>
/// <param name="IsStrict">Whether the program was prefixed with a "use strict" directive.</param>
public sealed record ProgramNode(SourceReference? Source, ImmutableArray<StatementNode> Body, bool IsStrict)
    : AstNode(Source);

/// <summary>
/// Wrapper for constructs we do not translate yet. This keeps the AST walkable while we
/// gradually replace raw S-expressions with typed counterparts.
/// </summary>
public sealed record UnknownStatement(SourceReference? Source, Cons Node) : StatementNode(Source);

/// <summary>
/// Wrapper for expressions that are not translated yet.
/// </summary>
public sealed record UnknownExpression(SourceReference? Source, Cons Node) : ExpressionNode(Source);
