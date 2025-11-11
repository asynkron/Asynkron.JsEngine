namespace Asynkron.JsEngine;

/// <summary>
/// Extension methods for Cons that provide checks for constant expressions.
/// These are used by the constant expression transformer to identify values that can be folded.
/// </summary>
public static class ConsExtensions
{
    /// <summary>
    /// Checks if the given object is a constant number (double).
    /// </summary>
    public static bool IsConstantNumber(this object? obj)
        => obj is double;

    /// <summary>
    /// Checks if the given object is a constant string.
    /// </summary>
    public static bool IsConstantString(this object? obj)
        => obj is string;

    /// <summary>
    /// Checks if the given object is a constant boolean.
    /// </summary>
    public static bool IsConstantBoolean(this object? obj)
        => obj is bool;

    /// <summary>
    /// Checks if the given object is a constant null value.
    /// </summary>
    public static bool IsConstantNull(this object? obj)
        => obj == null;

    /// <summary>
    /// Checks if the given object is any constant literal value
    /// (number, string, boolean, or null).
    /// </summary>
    public static bool IsConstant(this object? obj)
        => obj.IsConstantNumber() || 
           obj.IsConstantString() || 
           obj.IsConstantBoolean() || 
           obj.IsConstantNull();
}
