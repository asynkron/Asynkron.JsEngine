#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    /* FLAKY */
    /* FLAKY */
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

    /* FLAKY */
    /* FLAKY */
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

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("reduce", Length = 1d)]
    public JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(ReduceLike(thisValue, args, Realm, "Array.prototype.reduce", false));
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("reduceRight", Length = 1d)]
    public JsValue ReduceRight(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(ReduceLike(thisValue, args, Realm, "Array.prototype.reduceRight", true));
    }

    /* FLAKY */
    /* FLAKY */
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

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.find");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            var match = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (match.IsTruthy)
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("findIndex", Length = 1d)]
    public JsValue FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findIndex");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            var match = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);

            if (match.IsTruthy)
            {
                return new JsValue((double)k);
            }
        }

        return new JsValue(-1d);
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("some", Length = 1d)]
    public JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(SomeLike(thisValue, args, Realm, "Array.prototype.some"));
    }

    /* FLAKY */
    /* FLAKY */
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

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("findLast", Length = 1d)]
    public JsValue FindLast(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLast");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            var matches = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (matches.IsTruthy)
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    /* FLAKY */
    /* FLAKY */
    [JsHostMethod("findLastIndex", Length = 1d)]
    public JsValue FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLastIndex");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            var matches = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (matches.IsTruthy)
            {
                return new JsValue((double)k);
            }
        }

        return new JsValue(-1d);
    }
}
