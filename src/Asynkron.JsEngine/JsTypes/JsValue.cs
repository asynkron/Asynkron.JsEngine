using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents the kind of a JavaScript value.
/// </summary>
public enum JsValueKind : byte
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
    Object = 7
}

/// <summary>
/// A unified struct representation of JavaScript values that avoids boxing for primitives.
/// This is the core value type used throughout the engine to minimize allocations.
///
/// Layout (24 bytes on 64-bit):
/// - Kind: 1 byte (+ 7 bytes padding)
/// - NumberValue: 8 bytes (stores double directly, or bool as 0.0/1.0)
/// - ObjectValue: 8 bytes (reference for string, BigInt, Symbol, JsObject)
/// </summary>
public readonly struct JsValue : IEquatable<JsValue>
{
    /// <summary>The type of this value.</summary>
    public readonly JsValueKind Kind;

    /// <summary>
    /// For Number: the actual double value.
    /// For Boolean: 0.0 = false, 1.0 = true.
    /// For other types: unused (0.0).
    /// </summary>
    public readonly double NumberValue;

    /// <summary>
    /// For String: the string value.
    /// For BigInt: the JsBigInt instance.
    /// For Symbol: the Symbol instance.
    /// For Object: the JsObject (or derived type like JsArray, JsFunction).
    /// For primitives: null.
    /// </summary>
    public readonly object? ObjectValue;

    #region Static Singletons

    /// <summary>The undefined value.</summary>
    public static readonly JsValue Undefined = new(JsValueKind.Undefined, 0.0, null);

    /// <summary>The null value.</summary>
    public static readonly JsValue Null = new(JsValueKind.Null, 0.0, null);

    /// <summary>The boolean true value.</summary>
    public static readonly JsValue True = new(JsValueKind.Boolean, 1.0, null);

    /// <summary>The boolean false value.</summary>
    public static readonly JsValue False = new(JsValueKind.Boolean, 0.0, null);

    /// <summary>The number zero.</summary>
    public static readonly JsValue Zero = new(0.0);

    /// <summary>The number one.</summary>
    public static readonly JsValue One = new(1.0);

    /// <summary>The number negative one.</summary>
    public static readonly JsValue NegativeOne = new(-1.0);

    /// <summary>The NaN value.</summary>
    public static readonly JsValue NaN = new(double.NaN);

    /// <summary>Positive infinity.</summary>
    public static readonly JsValue PositiveInfinity = new(double.PositiveInfinity);

    /// <summary>Negative infinity.</summary>
    public static readonly JsValue NegativeInfinity = new(double.NegativeInfinity);

    /// <summary>The empty string.</summary>
    public static readonly JsValue EmptyString = new(string.Empty);

    #endregion

    #region Constructors

    /// <summary>Internal constructor for full control (used by Binding for special bindings).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue(JsValueKind kind, double numberValue, object? objectValue)
    {
        Kind = kind;
        NumberValue = numberValue;
        ObjectValue = objectValue;
    }

    /// <summary>Creates a number value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(double value)
    {
        Kind = JsValueKind.Number;
        NumberValue = value;
        ObjectValue = null;
    }

    /// <summary>Creates an integer number value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(int value)
    {
        Kind = JsValueKind.Number;
        NumberValue = value;
        ObjectValue = null;
    }

    /// <summary>Creates a long number value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(long value)
    {
        Kind = JsValueKind.Number;
        NumberValue = value;
        ObjectValue = null;
    }

    /// <summary>Creates a boolean value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(bool value)
    {
        Kind = JsValueKind.Boolean;
        NumberValue = value ? 1.0 : 0.0;
        ObjectValue = null;
    }

    /// <summary>Creates a string value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(string value)
    {
        Kind = JsValueKind.String;
        NumberValue = 0.0;
        ObjectValue = value;
    }

    /// <summary>Creates a BigInt value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(JsBigInt value)
    {
        Kind = JsValueKind.BigInt;
        NumberValue = 0.0;
        ObjectValue = value;
    }

    /// <summary>Creates a Symbol value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(Symbol value)
    {
        // Special case: Symbol.Undefined represents the undefined value
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            Kind = JsValueKind.Undefined;
            NumberValue = 0.0;
            ObjectValue = null;
        }
        else
        {
            Kind = JsValueKind.Symbol;
            NumberValue = 0.0;
            ObjectValue = value;
        }
    }

    /// <summary>Creates an object value (JsObject, JsArray, JsFunction, etc.).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue(JsObject value)
    {
        Kind = JsValueKind.Object;
        NumberValue = 0.0;
        ObjectValue = value;
    }

    #endregion

    #region Type Checks

    /// <summary>True if this is the undefined value.</summary>
    public bool IsUndefined
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Undefined;
    }

    /// <summary>True if this is the null value.</summary>
    public bool IsNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Null;
    }

    /// <summary>True if this is undefined or null.</summary>
    public bool IsNullish
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind <= JsValueKind.Null; // Undefined = 0, Null = 1
    }

    /// <summary>True if this is undefined or null (alias for IsNullish).</summary>
    public bool IsNullOrUndefined
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind <= JsValueKind.Null;
    }

    /// <summary>True if this is a boolean value.</summary>
    public bool IsBoolean
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Boolean;
    }

    /// <summary>True if this is a number value.</summary>
    public bool IsNumber
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Number;
    }

    /// <summary>True if this is a BigInt value.</summary>
    public bool IsBigInt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.BigInt;
    }

    /// <summary>True if this is a string value.</summary>
    public bool IsString
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.String;
    }

    /// <summary>True if this is a Symbol value.</summary>
    public bool IsSymbol
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Symbol;
    }

    /// <summary>True if this is an object value.</summary>
    public bool IsObject
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Object;
    }

    /// <summary>True if this is a primitive value (not an object).</summary>
    public bool IsPrimitive
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind != JsValueKind.Object;
    }

    #endregion

    #region Value Accessors

    /// <summary>Gets the double value. Only valid when IsNumber is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AsDouble() => NumberValue;

    /// <summary>Gets the boolean value. Only valid when IsBoolean is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBoolean() => NumberValue != 0.0;

    /// <summary>Gets the string value. Only valid when IsString is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string AsString() => (string)ObjectValue!;

    /// <summary>Gets the BigInt value. Only valid when IsBigInt is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsBigInt AsBigInt() => (JsBigInt)ObjectValue!;

    /// <summary>Gets the Symbol value. Only valid when IsSymbol is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol AsSymbol() => (Symbol)ObjectValue!;

    /// <summary>Gets the object value. Only valid when IsObject is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsObject AsObject() => (JsObject)ObjectValue!;

    /// <summary>Gets the object value as a specific type. Only valid when IsObject is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsObject<T>() where T : class => (T)ObjectValue!;

    #endregion

    #region Conversion Methods

    /// <summary>
    /// Converts this JsValue to a boxed object representation.
    /// Used for interop with existing code that uses object?.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ToObject()
    {
        return Kind switch
        {
            JsValueKind.Undefined => Symbol.Undefined,
            JsValueKind.Null => null,
            JsValueKind.Boolean => JsValueCache.GetBoolean(NumberValue != 0.0),
            JsValueKind.Number => JsValueCache.GetNumber(NumberValue),
            JsValueKind.BigInt => ObjectValue,
            JsValueKind.String => ObjectValue,
            JsValueKind.Symbol => ObjectValue,
            JsValueKind.Object => ObjectValue,
            _ => Symbol.Undefined
        };
    }

    /// <summary>
    /// Creates a JsValue from a boxed object representation.
    /// Used for gradual migration from object? to JsValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromObject(object? value)
    {
        return value switch
        {
            null => Null,
            double d => new JsValue(d),
            int i => new JsValue((double)i),
            long l => new JsValue((double)l),
            bool b => b ? True : False,
            string s => new JsValue(s),
            JsBigInt bi => new JsValue(bi),
            Symbol sym => sym == Symbol.Undefined ? Undefined : new JsValue(sym),
            JsObject obj => new JsValue(obj),
            _ => FromObjectSlow(value)
        };
    }

    /// <summary>Slow path for less common types.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue FromObjectSlow(object value)
    {
        // Handle other numeric types
        return value switch
        {
            // Handle boxed JsValue - unwrap it instead of wrapping again
            JsValue jsValue => jsValue,
            float f => new JsValue((double)f),
            decimal dec => new JsValue((double)dec),
            uint ui => new JsValue((double)ui),
            ulong ul => new JsValue((double)ul),
            short s => new JsValue((double)s),
            ushort us => new JsValue((double)us),
            byte b => new JsValue((double)b),
            sbyte sb => new JsValue((double)sb),
            // If it's a JsObject subclass, wrap it
            _ when value is JsObject obj => new JsValue(obj),
            // Unknown type - wrap as object (should rarely happen)
            _ => new JsValue(JsValueKind.Object, 0.0, value)
        };
    }

    #endregion

    #region Factory Methods

    /// <summary>Creates a boolean JsValue.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromBoolean(bool value) => value ? True : False;

    /// <summary>Creates a number JsValue, using cache for common values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromNumber(double value)
    {
        // Fast path for common values
        if (value == 0.0) return Zero;
        if (value == 1.0) return One;
        if (value == -1.0) return NegativeOne;
        return new JsValue(value);
    }

    /// <summary>Creates a number JsValue from an integer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromNumber(int value)
    {
        if (value == 0) return Zero;
        if (value == 1) return One;
        if (value == -1) return NegativeOne;
        return new JsValue((double)value);
    }

    /// <summary>Creates a string JsValue.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromString(string value)
    {
        if (value.Length == 0) return EmptyString;
        return new JsValue(value);
    }

    #endregion

    #region JavaScript Truthiness

    /// <summary>
    /// Returns true if this value is truthy according to JavaScript semantics.
    /// Falsy values: undefined, null, false, 0, -0, NaN, ""
    /// </summary>
    public bool IsTruthy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind switch
        {
            JsValueKind.Undefined => false,
            JsValueKind.Null => false,
            JsValueKind.Boolean => NumberValue != 0.0,
            JsValueKind.Number => NumberValue != 0.0 && !double.IsNaN(NumberValue),
            JsValueKind.String => ((string)ObjectValue!).Length > 0,
            JsValueKind.BigInt => !((JsBigInt)ObjectValue!).Value.IsZero,
            JsValueKind.Symbol => true,
            JsValueKind.Object => true,
            _ => false
        };
    }

    /// <summary>Returns true if this value is falsy according to JavaScript semantics.</summary>
    public bool IsFalsy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsTruthy;
    }

    #endregion

    #region Equality

    public bool Equals(JsValue other)
    {
        if (Kind != other.Kind) return false;

        return Kind switch
        {
            JsValueKind.Undefined => true,
            JsValueKind.Null => true,
            JsValueKind.Boolean => NumberValue == other.NumberValue,
            JsValueKind.Number => NumberValue.Equals(other.NumberValue), // Handles NaN correctly
            JsValueKind.String => string.Equals((string)ObjectValue!, (string)other.ObjectValue!, StringComparison.Ordinal),
            JsValueKind.BigInt => ((JsBigInt)ObjectValue!).Equals((JsBigInt)other.ObjectValue!),
            JsValueKind.Symbol => ReferenceEquals(ObjectValue, other.ObjectValue),
            JsValueKind.Object => ReferenceEquals(ObjectValue, other.ObjectValue),
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is JsValue other && Equals(other);

    public override int GetHashCode()
    {
        return Kind switch
        {
            JsValueKind.Undefined => 0,
            JsValueKind.Null => 1,
            JsValueKind.Boolean => NumberValue != 0.0 ? 3 : 2,
            JsValueKind.Number => NumberValue.GetHashCode(),
            JsValueKind.String => ((string)ObjectValue!).GetHashCode(StringComparison.Ordinal),
            JsValueKind.BigInt => ObjectValue!.GetHashCode(),
            JsValueKind.Symbol => ObjectValue!.GetHashCode(),
            JsValueKind.Object => ObjectValue!.GetHashCode(),
            _ => 0
        };
    }

    public static bool operator ==(JsValue left, JsValue right) => left.Equals(right);
    public static bool operator !=(JsValue left, JsValue right) => !left.Equals(right);

    #endregion

    #region ToString

    public override string ToString()
    {
        return Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => NumberValue != 0.0 ? "true" : "false",
            JsValueKind.Number => NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsValueKind.String => $"\"{ObjectValue}\"",
            JsValueKind.BigInt => $"{ObjectValue}n",
            JsValueKind.Symbol => ObjectValue?.ToString() ?? "Symbol()",
            JsValueKind.Object => ObjectValue?.ToString() ?? "[object Object]",
            _ => "undefined"
        };
    }

    #endregion

    #region Implicit Conversions (for convenience)

    public static implicit operator JsValue(double value) => new(value);
    public static implicit operator JsValue(int value) => new((double)value);
    public static implicit operator JsValue(bool value) => value ? True : False;
    public static implicit operator JsValue(string value) => new(value);

    #endregion
}
