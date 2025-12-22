namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Unary operators for UnaryExpression nodes.
/// </summary>
public enum UnaryOperator : byte
{
    // Prefix operators
    Plus, // +
    Minus, // -
    LogicalNot, // !
    BitwiseNot, // ~
    TypeOf, // typeof
    Void, // void
    Delete, // delete

    // Update operators (prefix and postfix)
    Increment, // ++
    Decrement // --
}
