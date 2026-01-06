#region

using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("map", Length = 1d)]
    public JsValue Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.map");
        var result = ArraySpeciesCreate(thisValue, length, Realm);
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var mapped = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            result.SetProperty(ToIndexString(k), mapped);
        }

        SetArrayLikeLength(result, length);
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("filter", Length = 1d)]
    public JsValue Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.filter");
        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        long toIndex = 0;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var keep = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (!keep.IsTruthy)
            {
                continue;
            }

            result.SetProperty(ToIndexString(toIndex), value);
            toIndex++;
        }

        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("reduce", Length = 1d)]
    public JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(ReduceLike(thisValue, args, Realm, "Array.prototype.reduce", false));
    }

    [JsHostMethod("reduceRight", Length = 1d)]
    public JsValue ReduceRight(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(ReduceLike(thisValue, args, Realm, "Array.prototype.reduceRight", true));
    }

    [JsHostMethod("forEach", Length = 1d)]
    public JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.forEach");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "Array.prototype.find", reverse: false);
        return result?.Value ?? JsValue.Undefined;
    }

    [JsHostMethod("findIndex", Length = 1d)]
    public JsValue FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "Array.prototype.findIndex", reverse: false);
        return result is { } r ? new JsValue((double)r.Index) : new JsValue(-1d);
    }

    [JsHostMethod("some", Length = 1d)]
    public JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(SomeLike(thisValue, args, Realm, "Array.prototype.some"));
    }

    [JsHostMethod("every", Length = 1d)]
    public JsValue Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.every");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var result = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (!result.IsTruthy)
            {
                return JsValue.False;
            }
        }

        return JsValue.True;
    }

    [JsHostMethod("findLast", Length = 1d)]
    public JsValue FindLast(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "Array.prototype.findLast", reverse: true);
        return result?.Value ?? JsValue.Undefined;
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    public JsValue FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "Array.prototype.findLastIndex", reverse: true);
        return result is { } r ? new JsValue((double)r.Index) : new JsValue(-1d);
    }

    /// <summary>
    /// Core find implementation that returns both index and value.
    /// Used by find, findIndex, findLast, and findLastIndex.
    /// </summary>
    private (long Index, JsValue Value)? FindCore(
        JsValue thisValue,
        IReadOnlyList<JsValue> args,
        string methodName,
        bool reverse)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, methodName);
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        var start = reverse ? length - 1 : 0L;
        var end = reverse ? -1L : length;
        var step = reverse ? -1L : 1L;

        for (var k = start; k != end; k += step)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            var match = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (match.IsTruthy)
            {
                return (k, value);
            }
        }

        return null;
    }
}
