using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// Provides caching for common JavaScript values and pooling for argument arrays.
/// This reduces allocations in hot paths like function calls.
/// </summary>
public static class JsValueCache
{
    // Cache small integers (0-1023) as boxed doubles - matches Jint's approach
    private const int IntegerCacheSize = 1024;
    private static readonly object?[] CachedIntegers = new object?[IntegerCacheSize];

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
    public static readonly object BoxedZero;
    public static readonly object BoxedOne;
    public static readonly object BoxedNegativeOne;
    public static readonly object BoxedNaN;
    public static readonly object BoxedPositiveInfinity;
    public static readonly object BoxedNegativeInfinity;

    // Argument array pools (separate pools for different sizes like Jint)
    private const int PoolSize = 16;
    private static readonly ConcurrentBag<object?[]> Pool1 = new();
    private static readonly ConcurrentBag<object?[]> Pool2 = new();
    private static readonly ConcurrentBag<object?[]> Pool3 = new();
    private static readonly ConcurrentBag<object?[]> Pool4 = new();

    static JsValueCache()
    {
        // Pre-cache integers 0-1023
        for (var i = 0; i < IntegerCacheSize; i++)
        {
            CachedIntegers[i] = (double)i;
        }

        // Cache special numbers
        BoxedZero = 0.0;
        BoxedOne = 1.0;
        BoxedNegativeOne = -1.0;
        BoxedNaN = double.NaN;
        BoxedPositiveInfinity = double.PositiveInfinity;
        BoxedNegativeInfinity = double.NegativeInfinity;

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
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object?[] RentArgumentArray(int size)
    {
        var pool = size switch
        {
            1 => Pool1,
            2 => Pool2,
            3 => Pool3,
            4 => Pool4,
            _ => null
        };

        if (pool is not null && pool.TryTake(out var array))
        {
            return array;
        }

        return new object?[size];
    }

    /// <summary>
    /// Returns an argument array to the pool. Arrays larger than 4 are not pooled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnArgumentArray(object?[] array)
    {
        var pool = array.Length switch
        {
            1 => Pool1,
            2 => Pool2,
            3 => Pool3,
            4 => Pool4,
            _ => null
        };

        if (pool is null) return;

        // Clear references to allow GC of contained objects
        Array.Clear(array);

        // Don't let pool grow unbounded
        if (pool.Count < PoolSize)
        {
            pool.Add(array);
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
}

/// <summary>
/// A disposable wrapper for pooled argument arrays that returns them on dispose.
/// Use with 'using' statement to ensure arrays are returned to the pool.
/// </summary>
public readonly struct PooledArgumentArray : IDisposable, IReadOnlyList<object?>
{
    private readonly object?[] _array;
    private readonly int _length;

    public PooledArgumentArray(object?[] array, int length)
    {
        _array = array;
        _length = length;
    }

    public object? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index < _length ? _array[index] : throw new IndexOutOfRangeException();
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public object?[] Array
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _array;
    }

    public void Dispose()
    {
        if (_array is not null && _array.Length <= 4)
        {
            JsValueCache.ReturnArgumentArray(_array);
        }
    }

    public IEnumerator<object?> GetEnumerator()
    {
        for (var i = 0; i < _length; i++)
        {
            yield return _array[i];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
