using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("map", Length = 1d)]
    public object? Map(object? thisValue, IReadOnlyList<object?> args)
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

            var mapped = callback.Invoke([value, (double)k, accessor], thisArg);
            result.SetProperty(ToIndexString(k), mapped);
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    [JsHostMethod("filter", Length = 1d)]
    public object? Filter(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
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

            var keep = callback.Invoke([value, (double)k, accessor], thisArg);
            if (!IsTruthy(keep))
            {
                continue;
            }

            result.SetProperty(ToIndexString(toIndex), value);
            toIndex++;
        }

        return result;
    }

    [JsHostMethod("reduce", Length = 1d)]
    public object? Reduce(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReduceLike(thisValue, args, Realm, "Array.prototype.reduce", false);
    }

    [JsHostMethod("reduceRight", Length = 1d)]
    public object? ReduceRight(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        return ReduceLike(thisValue, args, realm, "Array.prototype.reduceRight", true);
    }

    [JsHostMethod("forEach", Length = 1d)]
    public object? ForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.forEach");

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            callback.Invoke([value, (double)k, accessor], thisArg);
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("find", Length = 1d)]
    public object? Find(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.find");

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var match = callback.Invoke([value, (double)k, accessor], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("findIndex", Length = 1d)]
    public object? FindIndex(object? thisValue, IReadOnlyList<object?> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findIndex");

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var match = callback.Invoke([value, (double)k, accessor], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    [JsHostMethod("some", Length = 1d)]
    public object? Some(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        return SomeLike(thisValue, args, realm, "Array.prototype.some");
    }

    [JsHostMethod("every", Length = 1d)]
    public object? Every(object? thisValue, IReadOnlyList<object?> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.every");

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var result = callback.Invoke([value, (double)k, accessor], thisArg);
            if (!IsTruthy(result))
            {
                return false;
            }
        }

        return true;
    }

    [JsHostMethod("findLast", Length = 1d)]
    public object? FindLast(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.findLast");

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var matches = callback.Invoke([value, (double)k, accessor], thisArg);
            if (IsTruthy(matches))
            {
                return value;
            }
        }

        return Symbol.Undefined;
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    public object? FindLastIndex(object? thisValue, IReadOnlyList<object?> args)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, Realm, "Array.prototype.findLastIndex");

        for (var k = length - 1; k >= 0; k--)
        {
            var key = ToIndexString(k);
            var value = accessor.TryGetProperty(key, out var candidate) ? candidate : Symbol.Undefined;

            var matches = callback.Invoke([value, (double)k, accessor], thisArg);
            if (IsTruthy(matches))
            {
                return (double)k;
            }
        }

        return -1d;
    }
}
