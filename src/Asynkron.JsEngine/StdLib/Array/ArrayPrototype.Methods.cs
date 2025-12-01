using System.Collections.Generic;
using System.Text;
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
        if (newLength > MaxArrayLength)
        {
            throw ThrowTypeError("Array.prototype.push cannot exceed 2^53 - 1 elements", realm: Realm);
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
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

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
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

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

    [JsHostMethod("join", Length = 1d)]
    public object? Join(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.join", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        if (length == 0)
        {
            return string.Empty;
        }

        var separator = args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined)
            ? ","
            : args[0].ToJsString();

        var builder = new StringBuilder();
        for (long k = 0; k < length; k++)
        {
            if (k > 0)
            {
                builder.Append(separator);
            }

            var element = GetElementOrUndefined(accessor, ToIndexString(k));
            builder.Append(element.ToJsStringForArray());
        }

        return builder.ToString();
    }

    [JsHostMethod("toString", Length = 0d)]
    public object? ToString(object? thisValue, IReadOnlyList<object?> args)
    {
        var target = ToObjectPropertyAccessor(thisValue, "Array.prototype.toString", Realm);

        if (JsOps.TryGetPropertyValue(target, "join", out var joinValue) &&
            joinValue is IJsCallable joinCallable)
        {
            return joinCallable.Invoke([], target);
        }

        return InvokeDefaultObjectToString(target, Realm);
    }

    [JsHostMethod("includes", Length = 1d)]
    public object? Includes(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.includes", realm);

        var searchElement = args.GetArgument(0);
        var fromIndexArg = args.Count > 1 ? args[1] : 0d;
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;

        var fromIndex = ToIntegerOrInfinity(fromIndexArg);
        if (double.IsPositiveInfinity(fromIndex))
        {
            return false;
        }

        if (fromIndex < 0)
        {
            fromIndex = length + Math.Ceiling(fromIndex);
            if (fromIndex < 0)
            {
                fromIndex = 0;
            }
        }

        var start = (long)Math.Min(fromIndex, length);
        var lenLong = (long)Math.Min(length, (double)MaxArrayLength);

        if (accessor is JsArray jsArr && lenLong > 100000)
        {
            var indices = jsArr.GetOwnIndices()
                .Where(idx => idx >= start && idx < lenLong)
                .OrderBy(idx => idx);
            foreach (var idx in indices)
            {
                var val = jsArr.GetElement(idx);
                if (SameValueZero(val, searchElement))
                {
                    return true;
                }
            }
        }
        else
        {
            for (var i = start; i < lenLong; i++)
            {
                if (TryGetExistingElement(accessor, i, out var value) && SameValueZero(value, searchElement))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [JsHostMethod("indexOf", Length = 1d)]
    public object? IndexOf(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.indexOf", Realm);

        if (args.Count == 0)
        {
            return -1d;
        }

        var searchElement = args[0];
        var evalContext = Realm?.CreateContext();
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        if (double.IsPositiveInfinity(fromIndex))
        {
            return -1d;
        }

        if (fromIndex < 0)
        {
            fromIndex = Math.Max(length + Math.Ceiling(fromIndex), 0);
        }
        else
        {
            fromIndex = Math.Min(fromIndex, length);
        }

        var start = (long)Math.Min(fromIndex, length);
        var lenLong = (long)Math.Min(length, (double)MaxArrayLength);

        for (var i = start; i < lenLong; i++)
        {
            if (TryGetExistingElement(accessor, i, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    [JsHostMethod("lastIndexOf", Length = 1d)]
    public object? LastIndexOf(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.lastIndexOf", realm);
        var evalContext = realm?.CreateContext();
        var searchElement = args.GetArgument(0);
        if (accessor is TypedArrayBase typed)
        {
            // Align Array.prototype.lastIndexOf with TypedArray semantics.
            return TypedArrayBase.LastIndexOfInternal(typed, args);
        }

        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;
        if (length <= 0)
        {
            return -1d;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : length - 1;
        var lenLong = (long)Math.Min(length, (double)MaxArrayLength);

        long startIndexGeneric;
        if (double.IsNegativeInfinity(fromIndex))
        {
            return -1d;
        }

        if (double.IsPositiveInfinity(fromIndex))
        {
            startIndexGeneric = lenLong - 1;
        }
        else if (fromIndex >= 0)
        {
            startIndexGeneric = (long)Math.Min(fromIndex, lenLong - 1);
        }
        else
        {
            var candidate = lenLong + (long)Math.Ceiling(fromIndex);
            if (candidate < 0)
            {
                return -1d;
            }

            startIndexGeneric = candidate;
        }

        for (var i = startIndexGeneric; i >= 0; i--)
        {
            if (TryGetExistingElement(accessor, i, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public object? ToLocaleString(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString", Realm);

        var locales = args.GetArgument(0);
        var options = args.GetArgument(1);
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
        var parts = new List<string>((int)length);

        for (var i = 0; i < length; i++)
        {
            if (!TryGetExistingElement(accessor, i, out var element) ||
                element is null ||
                ReferenceEquals(element, Symbol.Undefined))
            {
                parts.Add(string.Empty);
                continue;
            }

            string part;
            if (element is IJsPropertyAccessor elementAccessor &&
                elementAccessor.TryGetProperty("toLocaleString", out var method) &&
                method is IJsCallable callable)
            {
                var result = callable.Invoke([locales, options], element);
                part = JsOps.ToJsString(result);
            }
            else
            {
                part = JsOps.ToJsString(element);
            }

            parts.Add(part);
        }

        return string.Join(",", parts);
    }

    [JsHostMethod("slice", Length = 2d)]
    public object? Slice(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.slice", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var endIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : length;

        var from = ClampRelativeIndex(startIndex, length);
        var to = ClampRelativeIndex(endIndex, length);
        var count = Math.Max(to - from, 0);
        var result = ArraySpeciesCreate(thisValue, count, realm);
        long targetIndex = 0;

        for (var k = from; k < to; k++)
        {
            CopyArrayElement(accessor, k, result, targetIndex++);
        }

        SetArrayLikeLength(result, targetIndex);
        return result;
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

        if (length + argCount > MaxArrayLength)
        {
            throw ThrowTypeError("Array.prototype.unshift cannot exceed 2^53 - 1 elements", realm: realm);
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
        if (newLength > MaxArrayLength)
        {
            throw ThrowRangeError("Array length exceeds 2^53 - 1", realm: Realm);
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
                if (resultIndex + spreadLength > MaxArrayLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^53 - 1", realm: realm);
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
                if (resultIndex >= MaxArrayLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^53 - 1", realm: realm);
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

    [JsHostMethod("at", Length = 1d)]
    public object? At(object? thisValue, IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return Symbol.Undefined;
        }

        var target = EnsureArrayLikeReceiver(thisValue, "Array.prototype.at", Realm);
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var indexNumber = args[0] is double d ? d : JsOps.ToNumber(args[0]);
        var index = indexNumber < 0 ? length + (long)Math.Ceiling(indexNumber) : (long)Math.Floor(indexNumber);

        if (index < 0 || index >= length)
        {
            return Symbol.Undefined;
        }

        return GetElementOrUndefined(target, ToIndexString(index));
    }

    [JsHostMethod("flat", Length = 0d)]
    public object? Flat(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flat", realm);
        var depthNum = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 1;
        long depth;
        if (double.IsNegativeInfinity(depthNum) || depthNum < 0)
        {
            depth = 0;
        }
        else if (double.IsPositiveInfinity(depthNum))
        {
            depth = long.MaxValue;
        }
        else
        {
            depth = (long)depthNum;
        }
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var sourceLength = (long)ToLengthOrZero(lengthValue);

        var result = ArraySpeciesCreate(thisValue, 0, realm);
        var newLength = FlattenIntoArray(result, accessor, sourceLength, 0, depth, null, null, realm,
            "Array.prototype.flat");
        SetArrayLikeLength(result, newLength);
        return result;
    }

    [JsHostMethod("flatMap", Length = 1d)]
    public object? FlatMap(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flatMap", Realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("Array.prototype.flatMap expects a callable mapper", realm: Realm);
        }
        var thisArg = args.GetArgument(1);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var sourceLength = (long)ToLengthOrZero(lengthValue);
        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        var newLength = FlattenIntoArray(result, accessor, sourceLength, 0, 1, callback, thisArg, Realm,
            "Array.prototype.flatMap");
        SetArrayLikeLength(result, newLength);
        return result;
    }

    [JsHostMethod("findLast", Length = 1d)]
    public object? FindLast(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.findLast");

        for (var k = length - 1; k >= 0; k--)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

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
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var matches = callback.Invoke([value, (double)k, accessor], thisArg);
            if (IsTruthy(matches))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    [JsHostMethod("fill", Length = 1d)]
    public object? Fill(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var target = EnsureArrayLikeReceiver(thisValue, "Array.prototype.fill", realm);
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var value = args.GetArgument(0);
        var startIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2]) : length;

        var start = ClampRelativeIndex(startIndex, length);
        var end = ClampRelativeIndex(endIndex, length);
        for (var k = start; k < end; k++)
        {
            target.SetProperty(ToIndexString(k), value);
        }

        return target;
    }

    [JsHostMethod("copyWithin", Length = 2d)]
    public object? CopyWithin(object? thisValue, IReadOnlyList<object?> args)
    {
        const string MethodName = "Array.prototype.copyWithin";
        var target = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var toIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2]) : length;

        var to = ClampRelativeIndex(toIndex, length);
        var from = ClampRelativeIndex(fromIndex, length);
        var final = ClampRelativeIndex(endIndex, length);

        var count = Math.Min(final - from, length - to);
        if (count <= 0)
        {
            return target;
        }

        long direction = 1;
        if (from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }

        var objectLike = target as IJsObjectLike;

        for (var i = 0; i < count; i++)
        {
            var fromKey = ToIndexString(from);
            var toKey = ToIndexString(to);

            var fromExists = TryGetExistingElement(target, fromKey, out var value);
            if (fromExists)
            {
                target.SetProperty(toKey, value);
            }
            else
            {
                var toExisted = HasProperty(target, toKey);
                DeletePropertyOrThrow(objectLike, toKey, toExisted, MethodName, Realm);
            }

            from += direction;
            to += direction;
        }

        return target;
    }

    [JsHostMethod("toSorted", Length = 1d)]
    public object? ToSorted(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toSorted", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var values = new List<object?>((int)Math.Min(length, int.MaxValue));
        for (long k = 0; k < length; k++)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                values.Add(value);
            }
        }

        if (args.Count > 0 && args[0] is IJsCallable compareFn)
        {
            values.Sort((a, b) =>
            {
                var cmp = compareFn.Invoke([a, b], null);
                if (cmp is not double d)
                {
                    return 0;
                }

                if (double.IsNaN(d))
                {
                    return 0;
                }

                return d > 0 ? 1 : d < 0 ? -1 : 0;

            });
        }
        else
        {
            values.Sort((a, b) =>
            {
                var aStr = JsValueToString(a);
                var bStr = JsValueToString(b);
                return string.CompareOrdinal(aStr, bStr);
            });
        }

        var result = ArraySpeciesCreate(thisValue, length, realm);

        long targetIndex = 0;
        foreach (var value in values)
        {
            result.SetProperty(ToIndexString(targetIndex++), value);
        }

        for (var k = targetIndex; k < length; k++)
        {
            var key = ToIndexString(k);
            result?.Delete(key);
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    [JsHostMethod("toReversed", Length = 0d)]
    public object? ToReversed(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toReversed", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var result = ArraySpeciesCreate(thisValue, length, Realm);
        for (long k = 0; k < length; k++)
        {
            var from = length - 1 - k;
            CopyArrayElement(accessor, from, result, k);
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    [JsHostMethod("toSpliced", Length = 2d)]
    public object? ToSpliced(object? thisValue, IReadOnlyList<object?> args)
    {
        RealmState? realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toSpliced", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var actualStart = ClampRelativeIndex(startIndex, length);

        var deleteCountIsUndefined = args.Count <= 1 || ReferenceEquals(args[1], Symbol.Undefined);
        long actualDeleteCount;
        if (deleteCountIsUndefined)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCountArg = ToIntegerOrInfinity(args[1]);
            if (double.IsPositiveInfinity(deleteCountArg))
            {
                actualDeleteCount = length - actualStart;
            }
            else
            {
                var bounded = Math.Max(deleteCountArg, 0);
                bounded = Math.Min(bounded, length - actualStart);
                actualDeleteCount = (long)bounded;
            }
        }

        var insertCount = Math.Max(args.Count - 2, 0);
        var newLength = length - actualDeleteCount + insertCount;
        if (newLength > MaxArrayLength)
        {
            throw ThrowTypeError("Array.prototype.toSpliced cannot exceed 2^53 - 1 elements", realm: realm);
        }

        var result = ArraySpeciesCreate(thisValue, newLength, realm);
        long targetIndex = 0;

        for (long k = 0; k < actualStart; k++)
        {
            CopyArrayElement(accessor, k, result, targetIndex++);
        }

        for (var i = 0; i < insertCount; i++)
        {
            result.SetProperty(ToIndexString(targetIndex++), args[i + 2]);
        }

        for (var k = actualStart + actualDeleteCount; k < length; k++)
        {
            CopyArrayElement(accessor, k, result, targetIndex++);
        }

        SetArrayLikeLength(result, targetIndex);
        return result;
    }

    [JsHostMethod("with", Length = 2d)]
    public object? With(object? thisValue, IReadOnlyList<object?> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.with", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        if (args.Count == 0)
        {
            throw ThrowTypeError("Array.prototype.with requires an index argument", realm: Realm);
        }

        var indexNumber = ToIntegerOrInfinity(args[0]);
        var integer = (long)Math.Truncate(indexNumber);
        if (double.IsPositiveInfinity(indexNumber))
        {
            integer = length;
        }
        else if (double.IsNegativeInfinity(indexNumber))
        {
            integer = -1;
        }

        if (integer < 0)
        {
            integer = length + integer;
        }

        if (integer < 0 || integer >= length)
        {
            throw ThrowRangeError("Array.prototype.with index out of range", realm: Realm);
        }

        var value = args.GetArgument(1);
        var result = ArraySpeciesCreate(thisValue, length, Realm);

        for (long k = 0; k < length; k++)
        {
            if (k == integer)
            {
                result.SetProperty(ToIndexString(k), value);
            }
            else
            {
                CopyArrayElement(accessor, k, result, k);
            }
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    [JsHostMethod("entries", Length = 0d)]
    public object? Entries(object? thisValue, IReadOnlyList<object?> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.entries", Realm, (accessor, _) => idx =>
        {
            var pair = new JsArray(Realm);
            pair.Push((double)idx);
            pair.Push(GetElementOrUndefined(accessor, ToIndexString(idx)));
            return pair;
        });
    }

    [JsHostMethod("keys", Length = 0d)]
    public object? Keys(object? thisValue, IReadOnlyList<object?> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.keys", Realm, (_, _) => idx => (double)idx);
    }

    [JsHostMethod("values", Length = 0d)]
    public object? Values(object? thisValue, IReadOnlyList<object?> args)
    {
        return CreateArrayIterator(thisValue, "Array.prototype.values", Realm,
            (accessor, _) => idx => GetElementOrUndefined(accessor, ToIndexString(idx)));
    }
}
