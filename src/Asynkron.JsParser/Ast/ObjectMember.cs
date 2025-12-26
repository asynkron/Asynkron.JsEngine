



namespace Asynkron.JsParser;

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
