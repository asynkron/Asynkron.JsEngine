using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// Provides caching for common JavaScript values and pooling for argument arrays.
/// This reduces allocations in hot paths like function calls.
/// </summary>
public static class JsValueCache
{
    // Cache small integers (0-10239) as boxed doubles - matches Jint's approach
    // Jint uses 10240 to cover common array indices and iteration counts
    private const int IntegerCacheSize = 10240;
    private static readonly object?[] CachedIntegers = new object?[IntegerCacheSize];

    // Cache index strings for property access (0-9999) - covers common array indices
    private const int IndexStringCacheSize = 10000;
    private static readonly string[] CachedIndexStrings = new string[IndexStringCacheSize];

    // Cache common strings
    private static readonly ConcurrentDictionary<string, string> InternedStrings = new(StringComparer.Ordinal);

    // Pre-interned well-known strings
    public static readonly string EmptyString = string.Empty;
    public static readonly string UndefinedString = "undefined";
    public static readonly string NullString = "null";
    public static readonly string TrueString = "true";
    public static readonly string FalseString = "false";
    public static readonly string ObjectString = "object";
    public static readonly string FunctionString = "function";
    public static readonly string NumberString = "number";
    public static readonly string StringString = "string";
    public static readonly string BooleanString = "boolean";
    public static readonly string SymbolString = "symbol";
    public static readonly string BigIntString = "bigint";
    public static readonly string NaNString = "NaN";
    public static readonly string InfinityString = "Infinity";
    public static readonly string NegativeInfinityString = "-Infinity";

    // Cached boxed booleans
    public static readonly object BoxedTrue = true;
    public static readonly object BoxedFalse = false;

    // Cached boxed numbers
    public static readonly object BoxedZero = 0.0;
    public static readonly object BoxedOne = 1.0;
    public static readonly object BoxedNegativeOne = -1.0;
    public static readonly object BoxedNaN = double.NaN;
    public static readonly object BoxedPositiveInfinity = double.PositiveInfinity;
    public static readonly object BoxedNegativeInfinity = double.NegativeInfinity;

    // Argument array pools - 15 cached arrays per size (matches Jint's approach)
    // Most function calls have 0-3 arguments, so we optimize heavily for these cases
    private const int PoolSize = 15;
    private static readonly ObjectPool<object?[]> Pool1 = new(PoolSize, static () => new object?[1]);
    private static readonly ObjectPool<object?[]> Pool2 = new(PoolSize, static () => new object?[2]);
    private static readonly ObjectPool<object?[]> Pool3 = new(PoolSize, static () => new object?[3]);
    private static readonly ObjectPool<object?[]> Pool4 = new(PoolSize, static () => new object?[4]);

    // JsValue argument array pools - avoid boxing when passing arguments to functions
    private static readonly ObjectPool<JsValue[]> JsValuePool1 = new(PoolSize, static () => new JsValue[1]);
    private static readonly ObjectPool<JsValue[]> JsValuePool2 = new(PoolSize, static () => new JsValue[2]);
    private static readonly ObjectPool<JsValue[]> JsValuePool3 = new(PoolSize, static () => new JsValue[3]);
    private static readonly ObjectPool<JsValue[]> JsValuePool4 = new(PoolSize, static () => new JsValue[4]);

    static JsValueCache()
    {
        // Pre-cache integers 0-10239
        for (var i = 0; i < IntegerCacheSize; i++)
        {
            CachedIntegers[i] = (double)i;
        }

        // Pre-cache index strings 0-9999 for array property access
        for (var i = 0; i < IndexStringCacheSize; i++)
        {
            CachedIndexStrings[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Pre-intern well-known strings
        InternedStrings[EmptyString] = EmptyString;
        InternedStrings[UndefinedString] = UndefinedString;
        InternedStrings[NullString] = NullString;
        InternedStrings[TrueString] = TrueString;
        InternedStrings[FalseString] = FalseString;
        InternedStrings[ObjectString] = ObjectString;
        InternedStrings[FunctionString] = FunctionString;
        InternedStrings[NumberString] = NumberString;
        InternedStrings[StringString] = StringString;
        InternedStrings[BooleanString] = BooleanString;
        InternedStrings[SymbolString] = SymbolString;
        InternedStrings[BigIntString] = BigIntString;
        InternedStrings[NaNString] = NaNString;
        InternedStrings[InfinityString] = InfinityString;
        InternedStrings[NegativeInfinityString] = NegativeInfinityString;
    }

    /// <summary>
    /// Gets a cached boxed integer if within cache range, otherwise boxes fresh.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object GetInteger(int value)
    {
        if ((uint)value < IntegerCacheSize)
        {
            return CachedIntegers[value]!;
        }
        return (double)value;
    }

    /// <summary>
    /// Gets a cached boxed double for common values, otherwise boxes fresh.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object GetNumber(double value)
    {
        // Check for common integer values first
        // Note: Must check for negative zero first since -0.0 >= 0 is true
        if (value >= 0 && value < IntegerCacheSize && value == Math.Truncate(value))
        {
            // Preserve negative zero (don't use cache for -0)
            if (value == 0 && double.IsNegative(value))
            {
                return value;
            }
            return CachedIntegers[(int)value]!;
        }

        // Check special values
        if (double.IsNaN(value)) return BoxedNaN;
        if (double.IsPositiveInfinity(value)) return BoxedPositiveInfinity;
        if (double.IsNegativeInfinity(value)) return BoxedNegativeInfinity;
        if (value == -1.0) return BoxedNegativeOne;

        return value;
    }

    /// <summary>
    /// Gets a cached boxed boolean.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object GetBoolean(bool value) => value ? BoxedTrue : BoxedFalse;

    /// <summary>
    /// Gets a cached string representation of an integer index if within cache range.
    /// This avoids allocations for common array index operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetIndexString(int index)
    {
        if ((uint)index < IndexStringCacheSize)
        {
            return CachedIndexStrings[index];
        }
        return index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a cached string representation of a long integer index if within cache range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetIndexString(long index)
    {
        if ((ulong)index < IndexStringCacheSize)
        {
            return CachedIndexStrings[(int)index];
        }
        return index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a cached string representation of a double if it's a valid array index.
    /// Returns null if not a valid index or out of cache range - caller should format manually.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? TryGetIndexString(double value)
    {
        // Check if it's a valid non-negative integer within cache range
        if (value >= 0 && value < IndexStringCacheSize && value == Math.Truncate(value))
        {
            return CachedIndexStrings[(int)value];
        }
        return null;
    }

    /// <summary>
    /// Interns a string if it's a well-known value, otherwise returns as-is.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string InternString(string value)
    {
        if (value.Length == 0) return EmptyString;
        if (value.Length > 20) return value; // Don't intern long strings

        return InternedStrings.GetOrAdd(value, static v => v);
    }

    /// <summary>
    /// Rents an argument array of the specified size from the pool.
    /// Uses lock-free pooling for sizes 1-4 with 15 cached arrays per size.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object?[] RentArgumentArray(int size)
    {
        return size switch
        {
            0 => [],
            1 => Pool1.Rent(),
            2 => Pool2.Rent(),
            3 => Pool3.Rent(),
            4 => Pool4.Rent(),
            _ => new object?[size]
        };
    }

    /// <summary>
    /// Returns an argument array to the pool. Arrays larger than 4 are not pooled.
    /// Clears array contents before returning to allow GC of referenced objects.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnArgumentArray(object?[] array)
    {
        var length = array.Length;
        if (length == 0 || length > 4) return;

        // Clear references to allow GC of contained objects
        // Use explicit assignments for small arrays to avoid Array.Clear overhead
        switch (length)
        {
            case 1:
                array[0] = null;
                Pool1.Return(array);
                break;
            case 2:
                array[0] = null;
                array[1] = null;
                Pool2.Return(array);
                break;
            case 3:
                array[0] = null;
                array[1] = null;
                array[2] = null;
                Pool3.Return(array);
                break;
            case 4:
                array[0] = null;
                array[1] = null;
                array[2] = null;
                array[3] = null;
                Pool4.Return(array);
                break;
        }
    }

    /// <summary>
    /// Creates a pooled single-element argument array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object?[] CreateArgs(object? arg0)
    {
        var array = RentArgumentArray(1);
        array[0] = arg0;
        return array;
    }

    /// <summary>
    /// Creates a pooled two-element argument array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object?[] CreateArgs(object? arg0, object? arg1)
    {
        var array = RentArgumentArray(2);
        array[0] = arg0;
        array[1] = arg1;
        return array;
    }

    /// <summary>
    /// Creates a pooled three-element argument array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object?[] CreateArgs(object? arg0, object? arg1, object? arg2)
    {
        var array = RentArgumentArray(3);
        array[0] = arg0;
        array[1] = arg1;
        array[2] = arg2;
        return array;
    }

    /// <summary>
    /// Rents a JsValue argument array of the specified size from the pool.
    /// Avoids boxing when passing arguments to functions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue[] RentJsValueArray(int size)
    {
        return size switch
        {
            1 => JsValuePool1.Rent(),
            2 => JsValuePool2.Rent(),
            3 => JsValuePool3.Rent(),
            4 => JsValuePool4.Rent(),
            _ => new JsValue[size]
        };
    }

    /// <summary>
    /// Returns a JsValue argument array to the pool. Arrays larger than 4 are not pooled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnJsValueArray(JsValue[] array)
    {
        var length = array.Length;
        if (length == 0 || length > 4) return;

        // Reset to default (Undefined) to allow GC of any object references
        switch (length)
        {
            case 1:
                array[0] = default;
                JsValuePool1.Return(array);
                break;
            case 2:
                array[0] = default;
                array[1] = default;
                JsValuePool2.Return(array);
                break;
            case 3:
                array[0] = default;
                array[1] = default;
                array[2] = default;
                JsValuePool3.Return(array);
                break;
            case 4:
                array[0] = default;
                array[1] = default;
                array[2] = default;
                array[3] = default;
                JsValuePool4.Return(array);
                break;
        }
    }
}
