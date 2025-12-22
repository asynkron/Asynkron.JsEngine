using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// <para>
/// A unified struct representation of JavaScript values that avoids boxing for primitives.
/// This is the core value type used throughout the engine to minimize allocations.
/// </para>
/// <para>
/// Layout (24 bytes on 64-bit):
/// - Kind: 4 bytes (int enum, better CPU performance than byte)
/// - Padding: 4 bytes (to align double to 8-byte boundary)
/// - NumberValue: 8 bytes (stores double directly, or bool as 0.0/1.0)
/// - ObjectValue: 8 bytes (reference for string, BigInt, Symbol, JsObject)
/// </para>
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

    #region Static Cache

    /// <summary>
    /// Cache of JsValue instances for integers 0-99999.
    /// Used to avoid struct copies for common numeric values.
    /// </summary>
    private static readonly JsValue[] IntegerCache = CreateIntegerCache(100000);

    private static JsValue[] CreateIntegerCache(int size)
    {
        var cache = new JsValue[size];
        for (var i = 0; i < size; i++)
        {
            cache[i] = new JsValue((double)i);
        }
        return cache;
    }

    /// <summary>
    /// Returns a JsValue for the given double, using cache for common integers.
    /// This avoids struct initialization for cached values.
    /// Note: We must not cache -0.0 since it's semantically different from +0.0 in JavaScript.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromDouble(double value)
    {
        // Fast path: check if it's a cached non-negative integer
        // Use double.IsNegative to detect negative zero (which would incorrectly match cache[0])
        // This is important because -0.0 == 0.0 in C# but they must be distinct in JavaScript
        var i = (int)value;
        if ((uint)i < (uint)IntegerCache.Length && i == value && !double.IsNegative(value))
        {
            return IntegerCache[i];
        }
        return new JsValue(value);
    }

    /// <summary>
    /// Returns a ref to a cached JsValue for the given double, or stores in the scratch slot.
    /// This avoids 24-byte struct copies for cached values.
    /// Note: We must not cache -0.0 since it's semantically different from +0.0 in JavaScript.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly JsValue FromDoubleRef(double value, ref JsValue scratch)
    {
        // Fast path: check if it's a cached non-negative integer
        // Use double.IsNegative to detect negative zero (which would incorrectly match cache[0])
        var i = (int)value;
        if ((uint)i < (uint)IntegerCache.Length && i == value && !double.IsNegative(value))
        {
            return ref IntegerCache[i];
        }
        // Fallback: store in scratch and return ref to it
        scratch = new JsValue(value);
        return ref scratch;
    }

    #endregion

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

    /// <summary>
    /// Sentinel object returned by ToObject() for Unit values.
    /// Used for backwards compatibility with code that checks ReferenceEquals against EmptyCompletion.
    /// </summary>
    internal static readonly object UnitSentinel = new();

    /// <summary>
    /// Unit/empty completion - represents "no value produced" by a statement.
    /// Used to distinguish "statement produced no completion value" from "undefined".
    /// </summary>
    public static readonly JsValue Unit = new(JsValueKind.Unit, 0.0, UnitSentinel);

    /// <summary>
    /// Uninitialized binding - represents a variable in the Temporal Dead Zone (TDZ).
    /// Accessing this value should throw a ReferenceError.
    /// </summary>
    public static readonly JsValue Uninitialized = new(JsValueKind.Uninitialized, 0.0, null);

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

    /// <summary>True if this is an uninitialized binding (TDZ).</summary>
    public bool IsUninitialized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Uninitialized;
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

    /// <summary>True if this is the Unit value (empty completion / no value).</summary>
    public bool IsUnit
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Kind == JsValueKind.Unit;
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
    public string AsString() => ObjectValue switch
    {
        string s => s,
        JsRopeString rope => rope.Flatten(),
        _ => string.Empty
    };

    /// <summary>Gets the BigInt value. Only valid when IsBigInt is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsBigInt AsBigInt() => (JsBigInt)ObjectValue!;

    /// <summary>Gets the Symbol value. Only valid when IsSymbol is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol AsSymbol() => (Symbol)ObjectValue!;

    /// <summary>Gets the object value. Only valid when IsObject is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsObject? AsObject() => (JsObject)ObjectValue!;

    /// <summary>Gets the object value as a specific type. Only valid when IsObject is true.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsObject<T>() where T : class => (T)ObjectValue!;

    #endregion

    #region TryGet Methods (for pattern matching)

    /// <summary>Tries to get the double value. Returns true if this is a number.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetDouble(out double value)
    {
        if (Kind == JsValueKind.Number)
        {
            value = NumberValue;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>Tries to get the boolean value. Returns true if this is a boolean.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetBoolean(out bool value)
    {
        if (Kind == JsValueKind.Boolean)
        {
            value = NumberValue != 0.0;
            return true;
        }
        value = false;
        return false;
    }

    /// <summary>Tries to get the string value. Returns true if this is a string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetString([NotNullWhen(true)]out string? value)
    {
        if (Kind == JsValueKind.String)
        {
            value = ObjectValue switch
            {
                string s => s,
                JsRopeString rope => rope.Flatten(),
                _ => string.Empty
            };
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Tries to get the Symbol value. Returns true if this is a Symbol.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetSymbol([NotNullWhen(true)]out Symbol? value)
    {
        if (Kind == JsValueKind.Symbol)
        {
            value = (Symbol)ObjectValue!;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Tries to get the BigInt value. Returns true if this is a BigInt.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetBigInt([NotNullWhen(true)]out JsBigInt? value)
    {
        if (Kind == JsValueKind.BigInt)
        {
            value = (JsBigInt)ObjectValue!;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Tries to get the object value. Returns true if this is an object.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetObject([NotNullWhen(true)]out JsObject? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is JsObject obj)
        {
            value = obj;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Tries to get the object value as a specific type. Returns true if this is an object of that type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetObject<T>([NotNullWhen(true)]out T? value) where T : class
    {
        if (Kind == JsValueKind.Object && ObjectValue is T obj)
        {
            value = obj;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Tries to unwrap the value to a specific type from ObjectValue. Works for any kind.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryUnwrap<T>([NotNullWhen(true)]out T? value) where T : class
    {
        if (ObjectValue is T obj)
        {
            value = obj;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get the value as a callable. Uses enum check first to avoid runtime type checks for non-objects.
    /// This is more efficient than TryGetObject&lt;IJsCallable&gt; in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCallable([NotNullWhen(true)]out IJsCallable? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is IJsCallable callable)
        {
            value = callable;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get the value as a property accessor. Uses enum check first to avoid runtime type checks for non-objects.
    /// This is more efficient than TryGetObject&lt;IJsPropertyAccessor&gt; in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPropertyAccessor([NotNullWhen(true)]out IJsPropertyAccessor? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is IJsPropertyAccessor accessor)
        {
            value = accessor;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get the value as an object-like type. Uses enum check first to avoid runtime type checks for non-objects.
    /// This is more efficient than TryGetObject&lt;IJsObjectLike&gt; in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetObjectLike([NotNullWhen(true)]out IJsObjectLike? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is IJsObjectLike objLike)
        {
            value = objLike;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get the value as a JsPromise. Uses enum check first to avoid runtime type checks for non-objects.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPromise([NotNullWhen(true)]out JsPromise? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is JsPromise promise)
        {
            value = promise;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Tries to get the value as a JsArray. Uses enum check first to avoid runtime type checks for non-objects.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetArray([NotNullWhen(true)]out JsArray? value)
    {
        if (Kind == JsValueKind.Object && ObjectValue is JsArray array)
        {
            value = array;
            return true;
        }
        value = null;
        return false;
    }

    #endregion

    #region Conversion Methods

    /// <summary>
    /// Converts this JsValue to a boxed object representation.
    /// Used for interop with existing code that uses object?.
    /// </summary>
    [Obsolete("Do not use!, make API accept JsValue")]
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
            JsValueKind.Unit => ObjectValue, // Returns UnitSentinel
            JsValueKind.Uninitialized => JsEnvironment.Uninitialized, // Preserve TDZ sentinel
            _ => Symbol.Undefined
        };
    }

    #region Typed FromObject overloads - prefer these to avoid boxing

    /// <summary>
    /// Runtime conversion from object? to JsValue. Use this ONLY when the source type
    /// is genuinely unknown at compile time (e.g., values from dictionaries, reflection, etc.).
    /// Prefer typed overloads when the type is known.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromObjectUnsafe(object? value)
    {
        // Check for JsEnvironment.Uninitialized sentinel before the switch
        if (ReferenceEquals(value, JsEnvironment.Uninitialized))
        {
            return Uninitialized;
        }

        return value switch
        {
            null => Null,
            JsValue jsv => jsv, // Already a JsValue (was boxed)
            JsObject jsObj => new JsValue(jsObj),
            string s => new JsValue(s),
            JsRopeString rope => new JsValue(JsValueKind.String, 0.0, rope), // Rope strings are strings
            double d => new JsValue(d),
            int i => new JsValue((double)i),
            long l => new JsValue((double)l),
            bool b => b ? True : False,
            Symbol sym => ReferenceEquals(sym, Symbol.Undefined) ? Undefined : new JsValue(sym),
            TypedAstSymbol astSym => new JsValue(JsValueKind.Symbol, 0.0, astSym),
            JsBigInt bi => new JsValue(bi),
            // Non-JsObject types that are wrapped as objects
            JsPromise promise => new JsValue(JsValueKind.Object, 0.0, promise),
            JsRegExp regExp => new JsValue(JsValueKind.Object, 0.0, regExp),
            JsDataView dataView => new JsValue(JsValueKind.Object, 0.0, dataView),
            JsMap map => new JsValue(JsValueKind.Object, 0.0, map),
            JsSet set => new JsValue(JsValueKind.Object, 0.0, set),
            JsWeakMap weakMap => new JsValue(JsValueKind.Object, 0.0, weakMap),
            JsWeakSet weakSet => new JsValue(JsValueKind.Object, 0.0, weakSet),
            TypedArrayBase typedArray => new JsValue(JsValueKind.Object, 0.0, typedArray),
            // Interface-based fallbacks
            IJsObjectLike objLike => new JsValue(JsValueKind.Object, 0.0, objLike),
            IJsCallable callable => new JsValue(JsValueKind.Object, 0.0, callable),
            IJsPropertyAccessor accessor => new JsValue(JsValueKind.Object, 0.0, accessor),
            // Numeric types
            float f => new JsValue(f),
            decimal dec => new JsValue((double)dec),
            uint ui => new JsValue((double)ui),
            ulong ul => new JsValue(ul),
            short sh => new JsValue((double)sh),
            ushort ush => new JsValue((double)ush),
            byte by => new JsValue((double)by),
            sbyte sby => new JsValue((double)sby),
            // Fallback: wrap unknown types as objects (for internal types like YieldResumeContext)
            _ => new JsValue(JsValueKind.Object, 0.0, value)
        };
    }

    #endregion

    #endregion

    #region Factory Methods

    /// <summary>Creates a boolean JsValue.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromBoolean(bool value) => value ? True : False;

    /// <summary>Creates a number JsValue, using cache for common values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromNumber(double value)
    {
        return value switch
        {
            // Fast path for common values
            0.0 => Zero,
            1.0 => One,
            -1.0 => NegativeOne,
            _ => new JsValue(value)
        };
    }

    /// <summary>Creates a number JsValue from an integer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromNumber(int value)
    {
        return value switch
        {
            0 => Zero,
            1 => One,
            -1 => NegativeOne,
            _ => new JsValue((double)value)
        };
    }

    /// <summary>Creates a string JsValue.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue FromString(string value)
    {
        return value.Length == 0 ? EmptyString : new JsValue(value);
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
        get
        {
            // Fast path for boolean (most common in loop conditions)
            if (Kind == JsValueKind.Boolean) return NumberValue != 0.0;
            // Fast path for objects (always truthy)
            if (Kind == JsValueKind.Object) return true;

            return Kind switch
            {
                JsValueKind.Undefined => false,
                JsValueKind.Null => false,
                JsValueKind.Unit => false,
                JsValueKind.Number => NumberValue != 0.0 && !double.IsNaN(NumberValue),
                JsValueKind.String => GetStringLength(ObjectValue) > 0,
                JsValueKind.BigInt => !((JsBigInt)ObjectValue!).Value.IsZero,
                JsValueKind.Symbol => true,
                _ => false
            };
        }
    }

    /// <summary>Returns true if this value is falsy according to JavaScript semantics.</summary>
    public bool IsFalsy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsTruthy;
    }

    /// <summary>
    /// Gets the length of a string value without flattening ropes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStringLength(object? value)
    {
        return value switch
        {
            string s => s.Length,
            JsRopeString rope => rope.Length,
            _ => 0
        };
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
            JsValueKind.Unit => true,
            JsValueKind.Boolean => NumberValue == other.NumberValue,
            JsValueKind.Number => NumberValue.Equals(other.NumberValue), // Handles NaN correctly
            JsValueKind.String => string.Equals(AsString(), other.AsString(), StringComparison.Ordinal),
            JsValueKind.BigInt => ((JsBigInt)ObjectValue!).Equals((JsBigInt)other.ObjectValue!),
            JsValueKind.Symbol or JsValueKind.Object => ReferenceEquals(ObjectValue, other.ObjectValue),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => obj is JsValue other && Equals(other);

    public override int GetHashCode()
    {
        return Kind switch
        {
            JsValueKind.Undefined => 0,
            JsValueKind.Null => 1,
            JsValueKind.Unit => -1, // Distinct from other singletons
            JsValueKind.Boolean => NumberValue != 0.0 ? 3 : 2,
            JsValueKind.Number => NumberValue.GetHashCode(),
            JsValueKind.String => AsString().GetHashCode(StringComparison.Ordinal),
            JsValueKind.BigInt or JsValueKind.Symbol or JsValueKind.Object => ObjectValue!.GetHashCode(),
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
            JsValueKind.Unit => "unit",
            JsValueKind.Boolean => NumberValue != 0.0 ? "true" : "false",
            JsValueKind.Number => NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsValueKind.String => $"\"{AsString()}\"",
            JsValueKind.BigInt => $"{ObjectValue}n",
            JsValueKind.Symbol => ObjectValue?.ToString() ?? "Symbol()",
            JsValueKind.Object => ObjectValue?.ToString() ?? "[object Object]",
            _ => "undefined"
        };
    }

    #endregion

    #region Implicit Conversions (for convenience)

    // Primitive conversions
    public static implicit operator JsValue(double value) => new(value);
    public static implicit operator JsValue(int value) => new((double)value);
    public static implicit operator JsValue(long value) => new((double)value);
    public static implicit operator JsValue(bool value) => value ? True : False;
    public static implicit operator JsValue(string value) => new(value);

    // Object type conversions - reduces JsValue.FromObjectUnsafe() boilerplate
    public static implicit operator JsValue(JsObject value) => new(value);
    public static implicit operator JsValue(JsBigInt value) => new(value);
    public static implicit operator JsValue(Symbol value) => new(value);
    public static implicit operator JsValue(TypedAstSymbol value) => new(JsValueKind.Symbol, 0.0, value);

    // Non-JsObject types that implement IJsObjectLike or are wrapped as objects
    public static implicit operator JsValue(HostFunction value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(TypedArrayBase value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsPromise value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsRegExp value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsDataView value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsMap value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsSet value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsWeakMap value) => new(JsValueKind.Object, 0.0, value);
    public static implicit operator JsValue(JsWeakSet value) => new(JsValueKind.Object, 0.0, value);

    #endregion
}
