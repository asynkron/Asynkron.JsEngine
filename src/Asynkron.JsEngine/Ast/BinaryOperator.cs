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
    InstanceOf // instanceof
}
