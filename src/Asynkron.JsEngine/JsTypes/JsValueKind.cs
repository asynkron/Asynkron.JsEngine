namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents the kind of JavaScript value.
/// </summary>
public enum JsValueKind
{
    /// <summary>The undefined value.</summary>
    Undefined = 0,

    /// <summary>The null value.</summary>
    Null = 1,

    /// <summary>A boolean value (true/false).</summary>
    Boolean = 2,

    /// <summary>A number value (IEEE 754 double).</summary>
    Number = 3,

    /// <summary>A BigInt value (arbitrary precision integer).</summary>
    BigInt = 4,

    /// <summary>A string value.</summary>
    String = 5,

    /// <summary>A Symbol value.</summary>
    Symbol = 6,

    /// <summary>An object value (includes arrays, functions, etc.).</summary>
    Object = 7,

    /// <summary>
    /// Unit/empty completion - represents "no value produced".
    /// Used internally to distinguish "statement produced no completion value" from "statement produced undefined".
    /// </summary>
    Unit = 8,

    /// <summary>
    /// Uninitialized binding - represents a variable in the Temporal Dead Zone (TDZ).
    /// Accessing this value throws a ReferenceError.
    /// </summary>
    Uninitialized = 9,
}
