using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("push", Length = 1d)]
    public object? Push(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.push", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var newLength = length + args.Count;
        if (newLength > MaxConcreteArrayLength)
        {
            throw ThrowTypeError("Array.prototype.push cannot exceed 2^32 - 1 elements", realm: Realm);
        }

        for (var i = 0; i < args.Count; i++)
        {
            var index = length + i;
            accessor.SetProperty(ToIndexString(index), args[i]);
        }

        accessor.SetProperty("length", (double)newLength);
        return (double)newLength;
    }

    [JsHostMethod("pop", Length = 0d)]
    public object? Pop(object? thisValue, IReadOnlyList<object?> args)
    {
        const string MethodName = "Array.prototype.pop";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        if (length == 0)
        {
            accessor.SetProperty("length", 0d);
            return Symbol.Undefined;
        }

        var newLength = length - 1;
        var key = ToIndexString(newLength);
        var elementExists = TryGetExistingElement(accessor, key, out var element);
        DeletePropertyOrThrow(objectLike, key, elementExists, MethodName, Realm);
        accessor.SetProperty("length", (double)newLength);
        return elementExists ? element : Symbol.Undefined;
    }

    [JsHostMethod("shift", Length = 0d)]
    public object? Shift(object? thisValue, IReadOnlyList<object?> args)
    {
        const string MethodName = "Array.prototype.shift";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        if (length == 0)
        {
            accessor.SetProperty("length", 0d);
            return Symbol.Undefined;
        }

        object? firstElement = Symbol.Undefined;
        var firstExists = TryGetExistingElement(accessor, "0", out var firstValue);
        if (firstExists)
        {
            firstElement = firstValue;
        }

        for (long k = 1; k < length; k++)
        {
            var fromKey = ToIndexString(k);
            var toKey = ToIndexString(k - 1);
            var fromExists = TryGetExistingElement(accessor, fromKey, out var fromValue);
            if (fromExists)
            {
                accessor.SetProperty(toKey, fromValue);
            }
            else
            {
                var toExists = HasProperty(accessor, toKey);
                DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, Realm);
            }
        }

        var lastKey = ToIndexString(length - 1);
        var lastExists = HasProperty(accessor, lastKey);
        DeletePropertyOrThrow(objectLike, lastKey, lastExists, MethodName, Realm);
        accessor.SetProperty("length", (double)(length - 1));
        return firstElement;
    }

    [JsHostMethod("unshift", Length = 1d)]
    public object? Unshift(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        const string MethodName = "Array.prototype.unshift";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var argCount = args.Count;

        if (length + argCount > MaxConcreteArrayLength)
        {
            throw ThrowTypeError("Array.prototype.unshift cannot exceed 2^32 - 1 elements", realm: realm);
        }

        for (long k = length - 1; k >= 0; k--)
        {
            var fromKey = ToIndexString(k);
            var toKey = ToIndexString(k + argCount);
            var fromExists = TryGetExistingElement(accessor, fromKey, out var fromValue);
            if (fromExists)
            {
                accessor.SetProperty(toKey, fromValue);
            }
            else
            {
                var toExists = HasProperty(accessor, toKey);
                DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, realm);
            }
        }

        for (var j = 0; j < argCount; j++)
        {
            accessor.SetProperty(ToIndexString(j), args[j]);
        }

        var newLength = length + argCount;
        accessor.SetProperty("length", (double)newLength);
        return (double)newLength;
    }

    [JsHostMethod("splice", Length = 2d)]
    public object? Splice(object? thisValue, IReadOnlyList<object?> args)
    {
        const string MethodName = "Array.prototype.splice";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var actualStart = ClampRelativeIndex(startIndex, length);

        var insertCount = args.Count > 2 ? args.Count - 2 : 0;

        double deleteCountArg;
        if (args.Count == 0)
        {
            deleteCountArg = length - actualStart;
        }
        else if (args.Count == 1)
        {
            deleteCountArg = length - actualStart;
        }
        else
        {
            deleteCountArg = ToIntegerOrInfinity(args[1]);
        }

        long actualDeleteCount;
        if (double.IsPositiveInfinity(deleteCountArg))
        {
            actualDeleteCount = length - actualStart;
        }
        else if (double.IsNegativeInfinity(deleteCountArg))
        {
            actualDeleteCount = 0;
        }
        else
        {
            var bounded = Math.Max(deleteCountArg, 0);
            bounded = Math.Min(bounded, length - actualStart);
            actualDeleteCount = (long)bounded;
        }

        var newLength = length - actualDeleteCount + insertCount;
        if (newLength > MaxConcreteArrayLength)
        {
            throw ThrowRangeError("Array length exceeds 2^32 - 1", realm: Realm);
        }

        var result = ArraySpeciesCreate(thisValue, actualDeleteCount, Realm);
        for (long k = 0; k < actualDeleteCount; k++)
        {
            CopyArrayElement(accessor, actualStart + k, result, k);
        }

        SetArrayLikeLength(result, actualDeleteCount);

        if (insertCount < actualDeleteCount)
        {
            for (long k = actualStart; k < length - actualDeleteCount; k++)
            {
                var from = k + actualDeleteCount;
                var to = k + insertCount;
                var fromKey = ToIndexString(from);
                var toKey = ToIndexString(to);

                if (TryGetExistingElement(accessor, fromKey, out var fromValue))
                {
                    accessor.SetProperty(toKey, fromValue);
                }
                else
                {
                    var toExists = HasProperty(accessor, toKey);
                    DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, Realm);
                }
            }

            for (long k = length; k > length - (actualDeleteCount - insertCount); k--)
            {
                var key = ToIndexString(k - 1);
                var existed = HasProperty(accessor, key);
                DeletePropertyOrThrow(objectLike, key, existed, MethodName, Realm);
            }
        }
        else if (insertCount > actualDeleteCount)
        {
            for (long k = length - actualDeleteCount; k > actualStart; k--)
            {
                var from = k + actualDeleteCount - 1;
                var to = k + insertCount - 1;
                var fromKey = ToIndexString(from);
                var toKey = ToIndexString(to);

                if (TryGetExistingElement(accessor, fromKey, out var fromValue))
                {
                    accessor.SetProperty(toKey, fromValue);
                }
                else
                {
                    var toExists = HasProperty(accessor, toKey);
                    DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, Realm);
                }
            }
        }

        for (var j = 0; j < insertCount; j++)
        {
            accessor.SetProperty(ToIndexString(actualStart + j), args[j + 2]);
        }

        accessor.SetProperty("length", (double)newLength);
        return result;
    }

    [JsHostMethod("concat", Length = 1d)]
    public object? Concat(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        const string MethodName = "Array.prototype.concat";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
        var result = ArraySpeciesCreate(thisValue, 0, realm);
        long resultIndex = 0;

        var sources = new object?[args.Count + 1];
        sources[0] = accessor;
        for (var i = 0; i < args.Count; i++)
        {
            sources[i + 1] = args[i];
        }

        foreach (var sourceValue in sources)
        {
            if (IsConcatSpreadable(sourceValue, realm, MethodName, out var spreadAccessor))
            {
                var spreadLength = LengthOfArrayLike(spreadAccessor, realm, MethodName);
                if (resultIndex + spreadLength > MaxConcreteArrayLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^32 - 1", realm: realm);
                }

                for (long k = 0; k < spreadLength; k++)
                {
                    var fromKey = ToIndexString(k);
                    var toKey = ToIndexString(resultIndex);
                    if (TryGetExistingElement(spreadAccessor, fromKey, out var value))
                    {
                        CreateDataPropertyOrThrow(result, toKey, value, realm, MethodName);
                    }

                    resultIndex++;
                }
            }
            else
            {
                if (resultIndex >= MaxConcreteArrayLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^32 - 1", realm: realm);
                }

                CreateDataPropertyOrThrow(result, ToIndexString(resultIndex++), sourceValue, realm, MethodName);
            }
        }

        SetArrayLikeLength(result, resultIndex);
        return result;
    }

    [JsHostMethod("reverse", Length = 0d)]
    public object? Reverse(object? thisValue, IReadOnlyList<object?> args)
    {
        const string MethodName = "Array.prototype.reverse";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var middle = length / 2;

        for (long lower = 0; lower < middle; lower++)
        {
            var upper = length - lower - 1;
            var lowerKey = ToIndexString(lower);
            var upperKey = ToIndexString(upper);

            var lowerExists = TryGetExistingElement(accessor, lowerKey, out var lowerValue);
            if (!lowerExists)
            {
                lowerValue = Symbol.Undefined;
            }

            var upperExists = TryGetExistingElement(accessor, upperKey, out var upperValue);
            if (!upperExists)
            {
                upperValue = Symbol.Undefined;
            }

            if (lowerExists && upperExists)
            {
                accessor.SetProperty(lowerKey, upperValue);
                accessor.SetProperty(upperKey, lowerValue);
                continue;
            }

            if (!lowerExists && upperExists)
            {
                accessor.SetProperty(lowerKey, upperValue);
                DeletePropertyOrThrow(objectLike, upperKey, upperExists, MethodName, Realm);
                continue;
            }

            if (lowerExists && !upperExists)
            {
                DeletePropertyOrThrow(objectLike, lowerKey, lowerExists, MethodName, Realm);
                accessor.SetProperty(upperKey, lowerValue);
                continue;
            }

            DeletePropertyOrThrow(objectLike, lowerKey, lowerExists, MethodName, Realm);
            DeletePropertyOrThrow(objectLike, upperKey, upperExists, MethodName, Realm);
        }

        return accessor;
    }

    [JsHostMethod("sort", Length = 1d)]
    public object? Sort(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.sort", realm);
        var objectLike = accessor as IJsObjectLike;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var elements = new List<object?>((int)Math.Min(length, int.MaxValue));
        for (long k = 0; k < length; k++)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                elements.Add(value);
            }
        }

        var compareFn = args.Count > 0 && args[0] is IJsCallable callable ? callable : null;

        int Comparer(object? a, object? b)
        {
            if (compareFn is not null)
            {
                var result = compareFn.Invoke([a, b], null);
                if (result is not double d)
                {
                    return 0;
                }

                if (double.IsNaN(d))
                {
                    return 0;
                }

                return d > 0 ? 1 : d < 0 ? -1 : 0;
            }

            var aStr = JsValueToString(a);
            var bStr = JsValueToString(b);
            return string.CompareOrdinal(aStr, bStr);
        }

        elements.Sort((Comparison<object?>)Comparer);

        long index = 0;
        foreach (var value in elements)
        {
            accessor.SetProperty(ToIndexString(index++), value);
        }

        if (objectLike is not null)
        {
            for (var k = index; k < length; k++)
            {
                objectLike.Delete(ToIndexString(k));
            }
        }
        else
        {
            for (var k = index; k < length; k++)
            {
                accessor.SetProperty(ToIndexString(k), Symbol.Undefined);
            }
        }

        return accessor;
    }
}
