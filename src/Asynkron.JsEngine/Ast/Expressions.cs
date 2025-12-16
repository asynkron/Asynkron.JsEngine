using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Binary operators for BinaryExpression nodes.
/// Using an enum enables fast integer-based switch dispatch instead of string comparison.
/// </summary>
public enum BinaryOperator : byte
{
    // Arithmetic
    Add,              // +
    Subtract,         // -
    Multiply,         // *
    Divide,           // /
    Modulo,           // %
    Power,            // **

    // Comparison
    Equal,            // ==
    NotEqual,         // !=
    StrictEqual,      // ===
    StrictNotEqual,   // !==
    LessThan,         // <
    LessThanOrEqual,  // <=
    GreaterThan,      // >
    GreaterThanOrEqual, // >=

    // Logical
    LogicalAnd,       // &&
    LogicalOr,        // ||
    NullishCoalescing, // ??

    // Bitwise
    BitwiseAnd,       // &
    BitwiseOr,        // |
    BitwiseXor,       // ^
    LeftShift,        // <<
    RightShift,       // >>
    UnsignedRightShift, // >>>

    // Other
    In,               // in
    InstanceOf,       // instanceof
}

/// <summary>
/// Unary operators for UnaryExpression nodes.
/// </summary>
public enum UnaryOperator : byte
{
    // Prefix operators
    Plus,             // +
    Minus,            // -
    LogicalNot,       // !
    BitwiseNot,       // ~
    TypeOf,           // typeof
    Void,             // void
    Delete,           // delete

    // Update operators (prefix and postfix)
    Increment,        // ++
    Decrement,        // --
}

/// <summary>
///     Represents a literal (number, string, boolean, null, undefined, BigInt).
/// </summary>
public sealed record LiteralExpression(SourceReference? Source, JsValue Value) : ExpressionNode(Source);

/// <summary>
///     Represents a regex literal. Kept separate because regex objects require RealmState at runtime.
/// </summary>
public sealed record RegexLiteralExpression(SourceReference? Source, string Pattern, string Flags) : ExpressionNode(Source);

/// <summary>
///     Represents a reference to an identifier.
/// </summary>
/// <param name="Source">Source location in the original code.</param>
/// <param name="Name">The identifier name as a Symbol.</param>
/// <param name="ScopeDepth">How many scopes up to find this variable (0 = local, 1 = parent, etc.). -1 means unresolved (use dictionary lookup).</param>
/// <param name="SlotIndex">Index into the scope's slots array. -1 means unresolved.</param>
/// <param name="ScopeId">Unique ID of the scope where this variable is declared. -1 means unresolved.</param>
public sealed record IdentifierExpression(
    SourceReference? Source,
    Symbol Name,
    int ScopeDepth = -1,
    int SlotIndex = -1,
    int ScopeId = -1) : ExpressionNode(Source);

/// <summary>
///     Represents a private identifier reference used in the 'in' operator for brand checking.
///     For example: #field in obj
/// </summary>
public sealed record PrivateIdentifierExpression(SourceReference? Source, string Name) : ExpressionNode(Source);

/// <summary>
///     Represents a binary expression such as a + b.
/// </summary>
public sealed record BinaryExpression(
    SourceReference? Source,
    BinaryOperator Operator,
    ExpressionNode Left,
    ExpressionNode Right) : ExpressionNode(Source);

/// <summary>
///     Represents a unary expression such as -a or !a.
/// </summary>
public sealed record UnaryExpression(SourceReference? Source, UnaryOperator Operator, ExpressionNode Operand, bool IsPrefix)
    : ExpressionNode(Source);

/// <summary>
///     Represents a conditional (ternary) expression.
/// </summary>
public sealed record ConditionalExpression(
    SourceReference? Source,
    ExpressionNode Test,
    ExpressionNode Consequent,
    ExpressionNode Alternate) : ExpressionNode(Source);

/// <summary>
///     Represents a function or generator expression.
/// </summary>
/// <param name="SlotCount">Number of slots needed for local variables in this function's scope.
/// Set by ScopeAnalyzer for O(1) variable access. -1 means not analyzed.</param>
/// <param name="ScopeId">Unique ID for the scope created by this function. -1 means not analyzed.</param>
/// <param name="HasClosures">True if any inner functions capture variables from this function's scope.
/// When true, environment reuse optimization is disabled for calls within this function.</param>
public sealed record FunctionExpression(
    SourceReference? Source,
    Symbol? Name,
    ImmutableArray<FunctionParameter> Parameters,
    BlockStatement Body,
    bool IsAsync,
    bool IsGenerator,
    bool IsArrow = false,
    bool WasAsync = false,
    bool IsHoistableDefaultExport = false,
    bool IsDefaultDerivedConstructor = false,
    int SlotCount = -1,
    int ScopeId = -1,
    bool HasClosures = false)
    : ExpressionNode(Source);

/// <summary>
///     Represents a single function parameter. Parameters may use destructuring or rest syntax,
///     so we capture the typed binding target while exposing default values.
/// </summary>
public sealed record FunctionParameter(
    SourceReference? Source,
    Symbol? Name,
    bool IsRest,
    BindingTarget? Pattern,
    ExpressionNode? DefaultValue);

/// <summary>
///     Represents a call expression.
/// </summary>
/// <param name="CanReuseCallerEnvironment">True if this call can safely reuse the caller's environment.
/// Set by ScopeAnalyzer when: (1) the containing function has no closures, (2) no eval/with in scope,
/// and (3) no scope variables are referenced after this call's arguments are evaluated.</param>
public sealed record CallExpression(
    SourceReference? Source,
    ExpressionNode Callee,
    ImmutableArray<CallArgument> Arguments,
    bool IsOptional,
    bool CanReuseCallerEnvironment = false) : ExpressionNode(Source);

/// <summary>
///     Represents a single call argument, optionally marked as a spread argument.
/// </summary>
public sealed record CallArgument(SourceReference? Source, ExpressionNode Expression, bool IsSpread);

/// <summary>
///     Represents a "new" expression.
/// </summary>
public sealed record NewExpression(
    SourceReference? Source,
    ExpressionNode Constructor,
    ImmutableArray<CallArgument> Arguments) : ExpressionNode(Source);

/// <summary>
///     Represents a property access (dot or computed) expression.
/// </summary>
public sealed record MemberExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Property,
    bool IsComputed,
    bool IsOptional) : ExpressionNode(Source);

/// <summary>
///     Represents the meta-property new.target.
/// </summary>
public sealed record NewTargetExpression(SourceReference? Source) : ExpressionNode(Source);

/// <summary>
///     Represents the meta-property import.meta.
/// </summary>
public sealed record ImportMetaExpression(SourceReference? Source) : ExpressionNode(Source);

/// <summary>
///     Represents an assignment to an identifier.
/// </summary>
/// <param name="Source">Source location in the original code.</param>
/// <param name="Target">The target identifier name.</param>
/// <param name="Value">The value expression to assign.</param>
/// <param name="IsCompoundAssignment">True if this is a compound assignment (+=, -=, etc.).</param>
/// <param name="ScopeDepth">How many scopes up to find target variable. -1 means unresolved.</param>
/// <param name="SlotIndex">Index into the scope's slots array. -1 means unresolved.</param>
/// <param name="ScopeId">Unique ID of the scope where this variable is declared. -1 means unresolved.</param>
public sealed record AssignmentExpression(
    SourceReference? Source,
    Symbol Target,
    ExpressionNode Value,
    bool IsCompoundAssignment = false,
    int ScopeDepth = -1,
    int SlotIndex = -1,
    int ScopeId = -1)
    : ExpressionNode(Source);

/// <summary>
///     Represents an assignment to a property access.
/// </summary>
public sealed record PropertyAssignmentExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Property,
    ExpressionNode Value,
    bool IsComputed,
    bool IsCompoundAssignment = false) : ExpressionNode(Source);

/// <summary>
///     Represents an assignment to an indexed access.
/// </summary>
public sealed record IndexAssignmentExpression(
    SourceReference? Source,
    ExpressionNode Target,
    ExpressionNode Index,
    ExpressionNode Value,
    bool IsCompoundAssignment = false) : ExpressionNode(Source);

/// <summary>
///     Represents a sequence expression (comma operator).
/// </summary>
public sealed record SequenceExpression(SourceReference? Source, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode(Source);

/// <summary>
///     Represents a destructuring assignment (<c>[a, b] = value</c> or <c>({ x } = value)</c>).
///     The pattern is expressed via the same typed binding nodes used by declarations so the
///     evaluator can reuse its destructuring logic.
/// </summary>
public sealed record DestructuringAssignmentExpression(
    SourceReference? Source,
    BindingTarget Target,
    ExpressionNode Value) : ExpressionNode(Source);

/// <summary>
///     Represents an array literal.
/// </summary>
public sealed record ArrayExpression(SourceReference? Source, ImmutableArray<ArrayElement> Elements)
    : ExpressionNode(Source);

/// <summary>
///     Represents a single element within an array literal.
/// </summary>
public sealed record ArrayElement(SourceReference? Source, ExpressionNode? Expression, bool IsSpread);

/// <summary>
///     Represents an object literal.
/// </summary>
public sealed record ObjectExpression(
    SourceReference? Source,
    ImmutableArray<ObjectMember> Members,
    bool HasCoverInitializedName = false)
    : ExpressionNode(Source);

/// <summary>
///     Represents a member within an object literal (data property, getter, setter, method, spread, etc.).
/// </summary>
public sealed record ObjectMember(
    SourceReference? Source,
    ObjectMemberKind Kind,
    object Key,
    ExpressionNode? Value,
    FunctionExpression? Function,
    bool IsComputed,
    bool IsStatic,
    Symbol? Parameter);

/// <summary>
///     Enumerates the supported object literal member kinds.
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
///     Represents a class expression that evaluates to a constructor function.
/// </summary>
public sealed record ClassExpression(SourceReference? Source, Symbol? Name, ClassDefinition Definition)
    : ExpressionNode(Source);

/// <summary>
///     Represents a template literal expression.
/// </summary>
public sealed record TemplateLiteralExpression(SourceReference? Source, ImmutableArray<TemplatePart> Parts)
    : ExpressionNode(Source);

/// <summary>
///     Represents a tagged template literal expression.
/// </summary>
public sealed record TaggedTemplateExpression(
    SourceReference? Source,
    ExpressionNode Tag,
    ExpressionNode StringsArray,
    ExpressionNode RawStringsArray,
    ImmutableArray<ExpressionNode> Expressions)
    : ExpressionNode(Source);

/// <summary>
///     Represents one part of a template literal (either raw text or an interpolated expression).
/// </summary>
public sealed record TemplatePart(SourceReference? Source, string? Text, ExpressionNode? Expression);

/// <summary>
///     Represents a yield expression inside a generator.
/// </summary>
public sealed record YieldExpression(SourceReference? Source, ExpressionNode? Expression, bool IsDelegated)
    : ExpressionNode(Source);

/// <summary>
///     Represents an await expression.
/// </summary>
public sealed record AwaitExpression(SourceReference? Source, ExpressionNode Expression) : ExpressionNode(Source);

/// <summary>
///     Represents the "this" keyword.
/// </summary>
public sealed record ThisExpression(SourceReference? Source) : ExpressionNode(Source);

/// <summary>
///     Represents the "super" keyword.
/// </summary>
public sealed record SuperExpression(SourceReference? Source) : ExpressionNode(Source);
