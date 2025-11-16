using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a literal (number, string, boolean, null, etc.).
/// </summary>
public sealed record LiteralExpression(SourceReference? Source, object? Value) : ExpressionNode(Source);

/// <summary>
/// Represents a reference to an identifier.
/// </summary>
public sealed record IdentifierExpression(SourceReference? Source, Symbol Name) : ExpressionNode(Source);

/// <summary>
/// Represents a binary expression such as a + b.
/// </summary>
public sealed record BinaryExpression(SourceReference? Source, string Operator, ExpressionNode Left,
    ExpressionNode Right) : ExpressionNode(Source);

/// <summary>
/// Represents a unary expression such as -a or !a.
/// </summary>
public sealed record UnaryExpression(SourceReference? Source, string Operator, ExpressionNode Operand, bool IsPrefix)
    : ExpressionNode(Source);

/// <summary>
/// Represents a conditional (ternary) expression.
/// </summary>
public sealed record ConditionalExpression(SourceReference? Source, ExpressionNode Test, ExpressionNode Consequent,
    ExpressionNode Alternate) : ExpressionNode(Source);

/// <summary>
/// Represents a function or generator expression.
/// </summary>
public sealed record FunctionExpression(SourceReference? Source, Symbol? Name,
    ImmutableArray<FunctionParameter> Parameters, BlockStatement Body, bool IsAsync, bool IsGenerator)
    : ExpressionNode(Source);

/// <summary>
/// Represents a single function parameter. Parameters may use destructuring or rest syntax,
/// so we capture the typed binding target while exposing default values.
/// </summary>
public sealed record FunctionParameter(SourceReference? Source, Symbol? Name, bool IsRest, BindingTarget? Pattern,
    ExpressionNode? DefaultValue);

/// <summary>
/// Represents a call expression.
/// </summary>
public sealed record CallExpression(SourceReference? Source, ExpressionNode Callee,
    ImmutableArray<CallArgument> Arguments, bool IsOptional) : ExpressionNode(Source);

/// <summary>
/// Represents a single call argument, optionally marked as a spread argument.
/// </summary>
public sealed record CallArgument(SourceReference? Source, ExpressionNode Expression, bool IsSpread);

/// <summary>
/// Represents a "new" expression.
/// </summary>
public sealed record NewExpression(SourceReference? Source, ExpressionNode Constructor,
    ImmutableArray<ExpressionNode> Arguments) : ExpressionNode(Source);

/// <summary>
/// Represents a property access (dot or computed) expression.
/// </summary>
public sealed record MemberExpression(SourceReference? Source, ExpressionNode Target, ExpressionNode Property,
    bool IsComputed, bool IsOptional) : ExpressionNode(Source);

/// <summary>
/// Represents an assignment to an identifier.
/// </summary>
public sealed record AssignmentExpression(SourceReference? Source, Symbol Target, ExpressionNode Value)
    : ExpressionNode(Source);

/// <summary>
/// Represents an assignment to a property access.
/// </summary>
public sealed record PropertyAssignmentExpression(SourceReference? Source, ExpressionNode Target,
    ExpressionNode Property, ExpressionNode Value, bool IsComputed) : ExpressionNode(Source);

/// <summary>
/// Represents an assignment to an indexed access.
/// </summary>
public sealed record IndexAssignmentExpression(SourceReference? Source, ExpressionNode Target,
    ExpressionNode Index, ExpressionNode Value) : ExpressionNode(Source);

/// <summary>
/// Represents a sequence expression (comma operator).
/// </summary>
public sealed record SequenceExpression(SourceReference? Source, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode(Source);

/// <summary>
/// Represents a destructuring assignment (<c>[a, b] = value</c> or <c>({ x } = value)</c>).
/// The pattern is expressed via the same typed binding nodes used by declarations so the
/// evaluator can reuse its destructuring logic.
/// </summary>
public sealed record DestructuringAssignmentExpression(SourceReference? Source, BindingTarget Target,
    ExpressionNode Value) : ExpressionNode(Source);

/// <summary>
/// Represents an array literal.
/// </summary>
public sealed record ArrayExpression(SourceReference? Source, ImmutableArray<ArrayElement> Elements)
    : ExpressionNode(Source);

/// <summary>
/// Represents a single element within an array literal.
/// </summary>
public sealed record ArrayElement(SourceReference? Source, ExpressionNode? Expression, bool IsSpread);

/// <summary>
/// Represents an object literal.
/// </summary>
public sealed record ObjectExpression(SourceReference? Source, ImmutableArray<ObjectMember> Members)
    : ExpressionNode(Source);

/// <summary>
/// Represents a member within an object literal (data property, getter, setter, method, spread, etc.).
/// </summary>
public sealed record ObjectMember(SourceReference? Source, ObjectMemberKind Kind, object Key,
    ExpressionNode? Value, FunctionExpression? Function, bool IsComputed, bool IsStatic, Symbol? Parameter);

/// <summary>
/// Enumerates the supported object literal member kinds.
/// </summary>
public enum ObjectMemberKind
{
    Property,
    Method,
    Getter,
    Setter,
    Field,
    Spread,
    Unknown
}

/// <summary>
/// Represents a class expression that evaluates to a constructor function.
/// </summary>
public sealed record ClassExpression(SourceReference? Source, Symbol? Name, ClassDefinition Definition)
    : ExpressionNode(Source);

/// <summary>
/// Represents a template literal expression.
/// </summary>
public sealed record TemplateLiteralExpression(SourceReference? Source, ImmutableArray<TemplatePart> Parts)
    : ExpressionNode(Source);

/// <summary>
/// Represents a tagged template literal expression.
/// </summary>
public sealed record TaggedTemplateExpression(SourceReference? Source, ExpressionNode Tag,
    ExpressionNode StringsArray, ExpressionNode RawStringsArray, ImmutableArray<ExpressionNode> Expressions)
    : ExpressionNode(Source);

/// <summary>
/// Represents one part of a template literal (either raw text or an interpolated expression).
/// </summary>
public sealed record TemplatePart(SourceReference? Source, string? Text, ExpressionNode? Expression);

/// <summary>
/// Represents a yield expression inside a generator.
/// </summary>
public sealed record YieldExpression(SourceReference? Source, ExpressionNode Expression, bool IsDelegated)
    : ExpressionNode(Source);

/// <summary>
/// Represents an await expression.
/// </summary>
public sealed record AwaitExpression(SourceReference? Source, ExpressionNode Expression) : ExpressionNode(Source);

/// <summary>
/// Represents the "this" keyword.
/// </summary>
public sealed record ThisExpression(SourceReference? Source) : ExpressionNode(Source);

/// <summary>
/// Represents the "super" keyword.
/// </summary>
public sealed record SuperExpression(SourceReference? Source) : ExpressionNode(Source);
