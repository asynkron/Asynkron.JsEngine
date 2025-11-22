using System.Collections.Immutable;
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
public sealed record VariableDeclaration(
    SourceReference? Source,
    VariableKind Kind,
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
/// Represents an array destructuring binding with optional rest element.
/// </summary>
public sealed record ArrayBinding(
    SourceReference? Source,
    ImmutableArray<ArrayBindingElement> Elements,
    BindingTarget? RestElement) : BindingTarget(Source);

/// <summary>
/// Represents a single element within an array destructuring binding.
/// </summary>
public sealed record ArrayBindingElement(SourceReference? Source, BindingTarget? Target, ExpressionNode? DefaultValue)
    : AstNode(Source);

/// <summary>
/// Represents an object destructuring binding with optional rest binding.
/// </summary>
public sealed record ObjectBinding(
    SourceReference? Source,
    ImmutableArray<ObjectBindingProperty> Properties,
    BindingTarget? RestElement) : BindingTarget(Source);

/// <summary>
/// Represents a single property inside an object destructuring binding.
/// </summary>
public sealed record ObjectBindingProperty(
    SourceReference? Source,
    string Name,
    BindingTarget Target,
    ExpressionNode? DefaultValue) : AstNode(Source);

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
public sealed record IfStatement(
    SourceReference? Source,
    ExpressionNode Condition,
    StatementNode Then,
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
public sealed record ForStatement(
    SourceReference? Source,
    StatementNode? Initializer,
    ExpressionNode? Condition,
    ExpressionNode? Increment,
    StatementNode Body) : StatementNode(Source);

/// <summary>
/// Represents for...in / for...of / for await...of loops.
/// </summary>
public sealed record ForEachStatement(
    SourceReference? Source,
    BindingTarget Target,
    ExpressionNode Iterable,
    StatementNode Body,
    ForEachKind Kind,
    VariableKind? DeclarationKind) : StatementNode(Source);

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
public sealed record TryStatement(
    SourceReference? Source,
    BlockStatement TryBlock,
    CatchClause? Catch,
    BlockStatement? Finally) : StatementNode(Source);

/// <summary>
/// Represents a catch clause in a try statement.
/// </summary>
public sealed record CatchClause(SourceReference? Source, Symbol Binding, BlockStatement Body) : AstNode(Source);

/// <summary>
/// Represents a switch statement with its cases.
/// </summary>
public sealed record SwitchStatement(
    SourceReference? Source,
    ExpressionNode Discriminant,
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
/// Represents a class declaration with its fully typed definition.
/// </summary>
public sealed record ClassDeclaration(SourceReference? Source, Symbol Name, ClassDefinition Definition)
    : StatementNode(Source);

/// <summary>
/// Captures the structure of a class body.
/// </summary>
public sealed record ClassDefinition(
    SourceReference? Source,
    ExpressionNode? Extends,
    FunctionExpression Constructor,
    ImmutableArray<ClassMember> Members,
    ImmutableArray<ClassField> Fields) : AstNode(Source);

/// <summary>
/// Represents a single method/getter/setter within a class body.
/// </summary>
public sealed record ClassMember(
    SourceReference? Source,
    ClassMemberKind Kind,
    string Name,
    FunctionExpression Function,
    bool IsStatic) : AstNode(Source);

/// <summary>
/// Distinguishes between regular methods, getters and setters.
/// </summary>
public enum ClassMemberKind
{
    Method,
    Getter,
    Setter
}

/// <summary>
/// Represents a field declared on a class.
/// </summary>
public sealed record ClassField(
    SourceReference? Source,
    string Name,
    ExpressionNode? Initializer,
    bool IsStatic,
    bool IsPrivate) : AstNode(Source);

/// <summary>
/// Base type for module import/export statements. Concrete records capture the
/// typed shape of each construct so higher layers no longer need to reason
/// about the underlying cons cells.
/// </summary>
public abstract record ModuleStatement(SourceReference? Source) : StatementNode(Source);

/// <summary>
/// Represents an <c>import</c> declaration.
/// </summary>
public sealed record ImportStatement(
    SourceReference? Source,
    string ModulePath,
    Symbol? DefaultBinding,
    Symbol? NamespaceBinding,
    ImmutableArray<ImportBinding> NamedImports) : ModuleStatement(Source);

/// <summary>
/// Represents a single named binding within an <c>import</c> declaration.
/// </summary>
public sealed record ImportBinding(SourceReference? Source, Symbol Imported, Symbol Local) : AstNode(Source);

/// <summary>
/// Represents an <c>export default</c> declaration.
/// </summary>
public sealed record ExportDefaultStatement(SourceReference? Source, ExportDefaultValue Value)
    : ModuleStatement(Source);

/// <summary>
/// Base type for <c>export default</c> payloads.
/// </summary>
public abstract record ExportDefaultValue(SourceReference? Source) : AstNode(Source);

/// <summary>
/// Represents <c>export default</c> followed by an expression.
/// </summary>
public sealed record ExportDefaultExpression(SourceReference? Source, ExpressionNode Expression)
    : ExportDefaultValue(Source);

/// <summary>
/// Represents <c>export default</c> followed by a declaration (function/class).
/// </summary>
public sealed record ExportDefaultDeclaration(SourceReference? Source, StatementNode Declaration)
    : ExportDefaultValue(Source);

/// <summary>
/// Represents <c>export { ... }</c> declarations.
/// </summary>
public sealed record ExportNamedStatement(
    SourceReference? Source,
    ImmutableArray<ExportSpecifier> Specifiers,
    string? FromModule) : ModuleStatement(Source);

/// <summary>
/// Represents a single <c>export { local as exported }</c> specifier.
/// </summary>
public sealed record ExportSpecifier(SourceReference? Source, Symbol Local, Symbol Exported) : AstNode(Source);

/// <summary>
/// Represents <c>export</c> followed by a regular declaration (<c>let</c>,
/// <c>function</c>, etc.).
/// </summary>
public sealed record ExportDeclarationStatement(SourceReference? Source, StatementNode Declaration)
    : ModuleStatement(Source);
