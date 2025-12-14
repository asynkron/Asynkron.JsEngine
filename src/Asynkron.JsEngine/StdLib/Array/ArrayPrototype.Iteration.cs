using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("map", Length = 1d)]
    public object? Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.map");
        var result = ArraySpeciesCreate(thisValue, length, Realm);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var mapped = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for property access
            result.SetProperty(ToIndexString(k), mapped.ToObject());
#pragma warning restore CS0618
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    [JsHostMethod("filter", Length = 1d)]
    public object? Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.filter");
        var result = ArraySpeciesCreate(thisValue, 0, realm);
        long toIndex = 0;

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var keep = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (!IsTruthy(keep.ToObject()))
#pragma warning restore CS0618
            {
                continue;
            }

            result.SetProperty(ToIndexString(toIndex), value);
            toIndex++;
        }

        return result;
    }

    [JsHostMethod("reduce", Length = 1d)]
    public object? Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReduceLike(thisValue, args, Realm, "Array.prototype.reduce", false);
    }

    [JsHostMethod("reduceRight", Length = 1d)]
    public object? ReduceRight(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        return ReduceLike(thisValue, args, realm, "Array.prototype.reduceRight", true);
    }

    [JsHostMethod("forEach", Length = 1d)]
    public object? ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.forEach");

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("find", Length = 1d)]
    public object? Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.find");

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var match = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (IsTruthy(match.ToObject()))
#pragma warning restore CS0618
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("findIndex", Length = 1d)]
    public object? FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findIndex");

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var match = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (IsTruthy(match.ToObject()))
#pragma warning restore CS0618
            {
                return (double)k;
            }
        }

        return -1d;
    }

    [JsHostMethod("some", Length = 1d)]
    public object? Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        return SomeLike(thisValue, args, realm, "Array.prototype.some");
    }

    [JsHostMethod("every", Length = 1d)]
    public object? Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.every");

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var result = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (!IsTruthy(result.ToObject()))
#pragma warning restore CS0618
            {
                return false;
            }
        }

        return true;
    }

    [JsHostMethod("findLast", Length = 1d)]
    public object? FindLast(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.findLast");

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var matches = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (IsTruthy(matches.ToObject()))
#pragma warning restore CS0618
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    public object? FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLastIndex");

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var matches = callback.Invoke([JsValue.FromObject(value), new JsValue((double)k), JsValue.FromObject(accessor)], JsValue.FromObject(thisArg));
#pragma warning disable CS0618 // ToObject is obsolete but needed here for IsTruthy
            if (IsTruthy(matches.ToObject()))
#pragma warning restore CS0618
            {
                return (double)k;
            }
        }

        return -1d;
    }
}
