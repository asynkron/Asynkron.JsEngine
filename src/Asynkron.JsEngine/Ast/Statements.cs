using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a block statement with optional strict mode.
/// </summary>
public sealed record BlockStatement(SourceReference? Source, ImmutableArray<StatementNode> Statements, bool IsStrict)
    : StatementNode(Source);

/// <summary>
/// Represents a variable declaration (let/var/const).
/// </summary>
public sealed record VariableDeclaration(SourceReference? Source, VariableKind Kind,
    ImmutableArray<VariableDeclarator> Declarators) : StatementNode(Source);

/// <summary>
/// Supported variable declaration kinds.
/// </summary>
public enum VariableKind
{
    Var,
    Let,
    Const
}

/// <summary>
/// A single variable declarator within a declaration statement.
/// </summary>
public sealed record VariableDeclarator(SourceReference? Source, BindingTarget Target, ExpressionNode? Initializer);

/// <summary>
/// Base type for the left-hand side of a variable declaration.
/// </summary>
public abstract record BindingTarget(SourceReference? Source) : AstNode(Source);

/// <summary>
/// Simple identifier binding.
/// </summary>
public sealed record IdentifierBinding(SourceReference? Source, Symbol Name) : BindingTarget(Source);

/// <summary>
/// Placeholder for complex destructuring patterns. We keep the original cons tree
/// so we can incrementally replace it with a richer typed model without losing fidelity.
/// </summary>
public sealed record DestructuringBinding(SourceReference? Source, Cons Pattern) : BindingTarget(Source);

/// <summary>
/// Represents an expression statement.
/// </summary>
public sealed record ExpressionStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);

/// <summary>
/// Represents a return statement.
/// </summary>
public sealed record ReturnStatement(SourceReference? Source, ExpressionNode? Expression) : StatementNode(Source);

/// <summary>
/// Represents a throw statement.
/// </summary>
public sealed record ThrowStatement(SourceReference? Source, ExpressionNode Expression) : StatementNode(Source);

/// <summary>
/// Represents a break statement, optionally labeled.
/// </summary>
public sealed record BreakStatement(SourceReference? Source, Symbol? Label) : StatementNode(Source);

/// <summary>
/// Represents a continue statement, optionally labeled.
/// </summary>
public sealed record ContinueStatement(SourceReference? Source, Symbol? Label) : StatementNode(Source);

/// <summary>
/// Represents an if/else statement.
/// </summary>
public sealed record IfStatement(SourceReference? Source, ExpressionNode Condition, StatementNode Then,
    StatementNode? Else) : StatementNode(Source);

/// <summary>
/// Represents a while loop.
/// </summary>
public sealed record WhileStatement(SourceReference? Source, ExpressionNode Condition, StatementNode Body)
    : StatementNode(Source);

/// <summary>
/// Represents a do/while loop.
/// </summary>
public sealed record DoWhileStatement(SourceReference? Source, StatementNode Body, ExpressionNode Condition)
    : StatementNode(Source);

/// <summary>
/// Represents a classic C-style for loop.
/// </summary>
public sealed record ForStatement(SourceReference? Source, StatementNode? Initializer, ExpressionNode? Condition,
    ExpressionNode? Increment, StatementNode Body) : StatementNode(Source);

/// <summary>
/// Represents for...in / for...of / for await...of loops.
/// </summary>
public sealed record ForEachStatement(SourceReference? Source, BindingTarget Target, ExpressionNode Iterable,
    StatementNode Body, ForEachKind Kind, VariableKind? DeclarationKind) : StatementNode(Source);

/// <summary>
/// Distinguishes the different for-each loop flavours.
/// </summary>
public enum ForEachKind
{
    In,
    Of,
    AwaitOf
}

/// <summary>
/// Represents a labeled statement.
/// </summary>
public sealed record LabeledStatement(SourceReference? Source, Symbol Label, StatementNode Statement)
    : StatementNode(Source);

/// <summary>
/// Represents a try/catch/finally statement.
/// </summary>
public sealed record TryStatement(SourceReference? Source, BlockStatement TryBlock, CatchClause? Catch,
    BlockStatement? Finally) : StatementNode(Source);

/// <summary>
/// Represents a catch clause in a try statement.
/// </summary>
public sealed record CatchClause(SourceReference? Source, Symbol Binding, BlockStatement Body) : AstNode(Source);

/// <summary>
/// Represents a switch statement with its cases.
/// </summary>
public sealed record SwitchStatement(SourceReference? Source, ExpressionNode Discriminant,
    ImmutableArray<SwitchCase> Cases) : StatementNode(Source);

/// <summary>
/// Represents a single case clause inside a switch statement.
/// </summary>
public sealed record SwitchCase(SourceReference? Source, ExpressionNode? Test, BlockStatement Body) : AstNode(Source);

/// <summary>
/// Represents an empty statement (";").
/// </summary>
public sealed record EmptyStatement(SourceReference? Source) : StatementNode(Source);

/// <summary>
/// Represents a function declaration.
/// </summary>
public sealed record FunctionDeclaration(SourceReference? Source, Symbol Name, FunctionExpression Function)
    : StatementNode(Source);

/// <summary>
/// Represents a class declaration. We keep the shape broad for now while retaining the
/// original S-expression to avoid losing information needed by downstream passes.
/// </summary>
public sealed record ClassDeclaration(SourceReference? Source, Symbol Name, Cons? ExtendsClause, Cons Constructor,
    Cons Methods, Cons Fields) : StatementNode(Source);

/// <summary>
/// Represents an import or export statement. Modules are still WIP so we preserve the raw cons payload.
/// </summary>
public sealed record ModuleStatement(SourceReference? Source, Cons Node) : StatementNode(Source);
