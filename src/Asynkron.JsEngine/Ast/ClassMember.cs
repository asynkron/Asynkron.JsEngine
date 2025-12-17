using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a single method/getter/setter within a class body.
/// </summary>
public sealed record ClassMember(
    SourceReference? Source,
    ClassMemberKind Kind,
    string Name,
    FunctionExpression Function,
    bool IsStatic,
    bool IsComputed = false,
    ExpressionNode? ComputedName = null,
    bool IsPrivate = false) : AstNode(Source);
