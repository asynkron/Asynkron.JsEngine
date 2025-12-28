#region

using Asynkron.JsEngine.JsTypes;
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
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var mapped = callback.Invoke(callbackArgs, thisArg);
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
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;
        long toIndex = 0;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var keep = callback.Invoke(callbackArgs, thisArg);
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
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            callback.Invoke(callbackArgs, thisArg);
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("find", Length = 1d)]
    public JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.find");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var match = callback.Invoke(callbackArgs, thisArg);
            if (match.IsTruthy)
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("findIndex", Length = 1d)]
    public JsValue FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findIndex");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var match = callback.Invoke(callbackArgs, thisArg);

            if (match.IsTruthy)
            {
                return new JsValue((double)k);
            }
        }

        return new JsValue(-1d);
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
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var result = callback.Invoke(callbackArgs, thisArg);
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
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLast");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var matches = callback.Invoke(callbackArgs, thisArg);
            if (matches.IsTruthy)
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    public JsValue FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLastIndex");
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        // Pre-allocate callback args array to avoid per-iteration allocation
        var callbackArgs = new JsValue[3];
        callbackArgs[2] = accessorJsValue;

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            // candidate is already a JsValue from TryGetProperty
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : JsValue.Undefined;

            callbackArgs[0] = value;
            callbackArgs[1] = new JsValue((double)k);
            var matches = callback.Invoke(callbackArgs, thisArg);
            if (matches.IsTruthy)
            {
                return new JsValue((double)k);
            }
        }

        return new JsValue(-1d);
    }
}
