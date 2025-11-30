using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    private const long MaxArrayLength = 9007199254740991L; // 2^53 - 1

    public static void AddArrayMethods(IJsPropertyAccessor array, RealmState? realm = null,
        JsObject? prototypeOverride = null)
    {
        // Once the shared Array prototype has been initialized, new arrays
        // should inherit from it instead of receiving per-instance copies of
        // every method. This keeps prototype mutations (e.g., in tests) visible
        // to existing arrays.
        var resolvedPrototype = prototypeOverride ?? realm?.ArrayPrototype;
        if (resolvedPrototype is not null && array is JsArray jsArray)
        {
            jsArray.SetPrototype(resolvedPrototype);
            return;
        }

        // push - already implemented natively
        array.SetHostedProperty("push", ArrayPush, realm);

        array.SetHostedProperty("pop", ArrayPop, realm);
        DefineArrayFunction(array, "map", 1d, ArrayMap, realm);
        DefineArrayFunction(array, "filter", 1d, ArrayFilter, realm);
        array.SetHostedProperty("reduce", ArrayReduce, realm);
        array.SetHostedProperty("reduceRight", ArrayReduceRight, realm);
        DefineArrayFunction(array, "forEach", 1d, ArrayForEach, realm);
        DefineArrayFunction(array, "find", 1d, ArrayFind, realm);
        DefineArrayFunction(array, "findIndex", 1d, ArrayFindIndex, realm);
        DefineArrayFunction(array, "some", 1d, ArraySome, realm);
        DefineArrayFunction(array, "every", 1d, ArrayEvery, realm);
        array.SetHostedProperty("join", ArrayJoin, realm);
        array.SetHostedProperty("toString", (thisValue, _) => ArrayToString(thisValue, realm));
        array.SetHostedProperty("includes", ArrayIncludes, realm);
        array.SetHostedProperty("indexOf", ArrayIndexOf, realm);
        var lastIndexOf =
            new HostFunction((thisValue, args) => ArrayLastIndexOf(thisValue, args, realm), realm, isConstructor: false);
        lastIndexOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "lastIndexOf", Writable = false, Enumerable = false, Configurable = true
            });
        lastIndexOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        var lastIndexDescriptor = new PropertyDescriptor
        {
            Value = lastIndexOf, Writable = true, Enumerable = false, Configurable = true
        };
        if (array is IJsObjectLike lastIndexTarget)
        {
            lastIndexTarget.DefineProperty("lastIndexOf", lastIndexDescriptor);
        }
        else
        {
            array.SetProperty("lastIndexOf", lastIndexOf);
        }

        array.SetHostedProperty("toLocaleString", ArrayToLocaleString, realm);
        array.SetHostedProperty("slice", ArraySlice, realm);
        array.SetHostedProperty("shift", ArrayShift, realm);
        array.SetHostedProperty("unshift", ArrayUnshift, realm);
        array.SetHostedProperty("splice", ArraySplice, realm);
        DefineArrayFunction(array, "concat", 1d, ArrayConcat, realm);
        array.SetHostedProperty("reverse", ArrayReverse, realm);
        array.SetHostedProperty("sort", ArraySort, realm);
        array.SetHostedProperty("at", ArrayAt, realm);
        array.SetHostedProperty("flat", ArrayFlat, realm);
        array.SetHostedProperty("flatMap", ArrayFlatMap, realm);
        DefineArrayFunction(array, "findLast", 1d, ArrayFindLast, realm);
        DefineArrayFunction(array, "findLastIndex", 1d, ArrayFindLastIndex, realm);
        array.SetHostedProperty("fill", ArrayFill, realm);
        array.SetHostedProperty("copyWithin", ArrayCopyWithin, realm);
        array.SetHostedProperty("toSorted", ArrayToSorted, realm);
        array.SetHostedProperty("toReversed", ArrayToReversed, realm);
        array.SetHostedProperty("toSpliced", ArrayToSpliced, realm);
        array.SetHostedProperty("with", ArrayWith, realm);

        // entries() - returns an iterator of [index, value] pairs
        DefineArrayIteratorFunction("entries", (accessor, _) => idx =>
        {
            var pair = new JsArray(realm);
            pair.Push((double)idx);
            pair.Push(GetElementOrUndefined(accessor, ToIndexString(idx)));

            AddArrayMethods(pair, realm);
            return pair;
        });

        // keys() - returns an iterator of indices
        DefineArrayIteratorFunction("keys", (_, _) => idx => (double)idx);

        // values() - returns an iterator of values
        var valuesFn = DefineArrayIteratorFunction("values", (accessor, _) => idx =>
        {
            var key = idx.ToString(CultureInfo.InvariantCulture);
            return GetElementOrUndefined(accessor, key);
        });
        return;

        static double ToLengthValue(object? candidate)
        {
            var num = JsOps.ToNumber(candidate);
            if (double.IsNaN(num) || double.IsInfinity(num) || num <= 0)
            {
                return 0;
            }

            var truncated = Math.Floor(num);
            return Math.Min(truncated, (double)MaxArrayLength); // 2^53 - 1
        }

        static object CreateArrayIterator(object? thisValue, IJsPropertyAccessor accessor,
            Func<uint, object?> projector)
        {
            var iterator = new JsObject();
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

            uint index = 0;
            var exhausted = false;

            iterator.SetHostedProperty("next", Next);

            iterator.SetHostedProperty(iteratorKey, ReturnIterator);
            return iterator;

            object? Next(object? _, IReadOnlyList<object?> __)
            {
                if (exhausted)
                {
                    var doneResult = new JsObject();
                    doneResult.SetProperty("value", Symbol.Undefined);
                    doneResult.SetProperty("done", true);
                    return doneResult;
                }

                uint length = 0;
                if (accessor.TryGetProperty("length", out var lengthValue))
                {
                    length = (uint)ToLengthValue(lengthValue);
                }

                var result = new JsObject();
                if (index < length)
                {
                    result.SetProperty("value", projector(index));
                    result.SetProperty("done", false);
                    index++;
                }
                else
                {
                    result.SetProperty("value", Symbol.Undefined);
                    result.SetProperty("done", true);
                    exhausted = true;
                }

                return result;
            }

            object? ReturnIterator(object? _, IReadOnlyList<object?> __)
            {
                return iterator;
            }
        }

        HostFunction DefineArrayIteratorFunction(string name,
            Func<IJsPropertyAccessor, object?, Func<uint, object?>> projectorFactory)
        {
            var fn = new HostFunction((thisValue, _) =>
            {
                if (thisValue is null || ReferenceEquals(thisValue, Symbol.Undefined))
                {
                    var error = realm?.TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke([$"{name} called on null or undefined"], null)
                        : new InvalidOperationException($"{name} called on null or undefined");
                    throw new ThrowSignal(error);
                }

                if (thisValue is not IJsPropertyAccessor accessor)
                {
                    var error = realm?.TypeErrorConstructor is IJsCallable ctor2
                        ? ctor2.Invoke([$"{name} called on non-object"], null)
                        : new InvalidOperationException($"{name} called on non-object");
                    throw new ThrowSignal(error);
                }

                var projector = projectorFactory(accessor, thisValue);
                return CreateArrayIterator(thisValue, accessor, projector);
            }, isConstructor: false);

            fn.DefineProperty("name",
                new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });

            fn.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = false, Enumerable = false, Configurable = true });

            var descriptor = new PropertyDescriptor
            {
                Value = fn, Writable = true, Enumerable = false, Configurable = true
            };

            if (array is IJsObjectLike objectLike)
            {
                objectLike.DefineProperty(name, descriptor);
            }
            else
            {
                array.SetProperty(name, fn);
            }

            return fn;
        }
    }

    private static object? ArrayPush(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.push", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var newLength = length + args.Count;
        if (newLength > MaxArrayLength)
        {
            throw ThrowTypeError("Array.prototype.push cannot exceed 2^53 - 1 elements", realm: realm);
        }

        for (var i = 0; i < args.Count; i++)
        {
            var index = length + i;
            accessor.SetProperty(ToIndexString(index), args[i]);
        }

        accessor.SetProperty("length", (double)newLength);
        return (double)newLength;
    }

    private static object? ArrayPop(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.prototype.pop";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
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
        DeletePropertyOrThrow(objectLike, key, elementExists, MethodName, realm);
        accessor.SetProperty("length", (double)newLength);
        return elementExists ? element : Symbol.Undefined;
    }

    private static object? ArrayMap(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.map");
        var result = ArraySpeciesCreate(thisValue, length, realm);

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

    private static object? ArrayFilter(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayReduce(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        return ReduceLike(thisValue, args, realm, "Array.prototype.reduce", false);
    }

    private static object? ArrayReduceRight(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        return ReduceLike(thisValue, args, realm, "Array.prototype.reduceRight", true);
    }

    private static object? ArrayForEach(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.forEach");

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

    private static object? ArrayFind(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayFindIndex(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.findIndex");

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

    private static object? ArraySome(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        return SomeLike(thisValue, args, realm, "Array.prototype.some");
    }

    private static object? ArrayEvery(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.every");

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

    private static object? ArrayJoin(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayToString(object? thisValue, RealmState? realm)
    {
        var target = ToObjectPropertyAccessor(thisValue, "Array.prototype.toString", realm);

        if (JsOps.TryGetPropertyValue(target, "join", out var joinValue) &&
            joinValue is IJsCallable joinCallable)
        {
            return joinCallable.Invoke([], target);
        }

        return InvokeDefaultObjectToString(target, realm);
    }

    private static object? ArrayIncludes(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayIndexOf(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.indexOf", realm);

        if (args.Count == 0)
        {
            return -1d;
        }

        var searchElement = args[0];
        var evalContext = realm?.CreateContext();
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

    private static object? ArrayLastIndexOf(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayToLocaleString(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString", realm);

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

    private static object? ArrayShift(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.prototype.shift";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
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
                DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, realm);
            }
        }

        var lastKey = ToIndexString(length - 1);
        var lastExists = HasProperty(accessor, lastKey);
        DeletePropertyOrThrow(objectLike, lastKey, lastExists, MethodName, realm);
        accessor.SetProperty("length", (double)(length - 1));
        return firstElement;
    }

    private static object? ArrayUnshift(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArraySplice(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.prototype.splice";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
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
            throw ThrowRangeError("Array length exceeds 2^53 - 1", realm: realm);
        }

        var result = ArraySpeciesCreate(thisValue, actualDeleteCount, realm);
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
                    DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, realm);
                }
            }

            for (long k = length; k > length - (actualDeleteCount - insertCount); k--)
            {
                var key = ToIndexString(k - 1);
                var existed = HasProperty(accessor, key);
                DeletePropertyOrThrow(objectLike, key, existed, MethodName, realm);
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
                    DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, realm);
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

    private static object? ArrayConcat(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayReverse(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.prototype.reverse";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
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
                DeletePropertyOrThrow(objectLike, upperKey, upperExists, MethodName, realm);
                continue;
            }

            if (lowerExists && !upperExists)
            {
                DeletePropertyOrThrow(objectLike, lowerKey, lowerExists, MethodName, realm);
                accessor.SetProperty(upperKey, lowerValue);
                continue;
            }

            DeletePropertyOrThrow(objectLike, lowerKey, lowerExists, MethodName, realm);
            DeletePropertyOrThrow(objectLike, upperKey, upperExists, MethodName, realm);
        }

        return accessor;
    }

    private static object? ArraySort(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayAt(object? thisValue, IReadOnlyList<object?> args, RealmState? realm = null)
    {
        if (args.Count == 0)
        {
            return Symbol.Undefined;
        }

        var target = EnsureArrayLikeReceiver(thisValue, "Array.prototype.at", realm);
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

    private static object? ArrayFlat(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayFlatMap(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flatMap", realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError("Array.prototype.flatMap expects a callable mapper", realm: realm);
        }
        var thisArg = args.GetArgument(1);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var sourceLength = (long)ToLengthOrZero(lengthValue);
        var result = ArraySpeciesCreate(thisValue, 0, realm);
        var newLength = FlattenIntoArray(result, accessor, sourceLength, 0, 1, callback, thisArg, realm,
            "Array.prototype.flatMap");
        SetArrayLikeLength(result, newLength);
        return result;
    }

    private static object? ArrayFindLast(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayFindLastIndex(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, "Array.prototype.findLastIndex");

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

    private static object? ArrayFill(object? thisValue, IReadOnlyList<object?> args, RealmState? realm = null)
    {
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

    private static object? ArrayCopyWithin(object? thisValue, IReadOnlyList<object?> args, RealmState? realm = null)
    {
        const string MethodName = "Array.prototype.copyWithin";
        var target = EnsureArrayLikeReceiver(thisValue, MethodName, realm);
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
                DeletePropertyOrThrow(objectLike, toKey, toExisted, MethodName, realm);
            }

            from += direction;
            to += direction;
        }

        return target;
    }

    private static object? ArrayToSorted(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayToReversed(object? thisValue, IReadOnlyList<object?> _, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toReversed", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var result = ArraySpeciesCreate(thisValue, length, realm);
        for (long k = 0; k < length; k++)
        {
            var from = length - 1 - k;
            CopyArrayElement(accessor, from, result, k);
        }

        SetArrayLikeLength(result, length);
        return result;
    }

    private static object? ArrayToSpliced(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static object? ArrayWith(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.with", realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        if (args.Count == 0)
        {
            throw ThrowTypeError("Array.prototype.with requires an index argument", realm: realm);
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
            throw ThrowRangeError("Array.prototype.with index out of range", realm: realm);
        }

        var value = args.GetArgument(1);
        var result = ArraySpeciesCreate(thisValue, length, realm);

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

    private static object? ArraySlice(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
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

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static bool AreStrictlyEqual(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static IJsObjectLike ToArrayLike(object? value, RealmState? realm)
    {
        if (value is IJsObjectLike accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError("Array method called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError("Array method receiver is not object-like", realm: realm);
    }

    private static int GetArrayLikeLength(IJsObjectLike obj)
    {
        if (!obj.TryGetProperty("length", out var lengthVal))
        {
            return 0;
        }

        var asNumber = JsOps.ToNumber(lengthVal);
        if (double.IsNaN(asNumber) || !(asNumber > 0))
        {
            return 0;
        }

        if (asNumber > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)asNumber;

    }

    public static HostFunction CreateArrayConstructor(RealmState realm)
    {
        JsObject? arrayPrototype = null;

        // Array constructor
        var arrayConstructor = new HostFunction((thisValue, args) =>
        {
            // Use provided receiver when available so Reflect.construct can
            // control allocation and prototype.
            var instance = thisValue as JsArray ?? new JsArray(realm);

            // Honor an explicit prototype on the receiver; otherwise fall back
            // to the constructor's prototype if available.
            if (thisValue is JsObject { Prototype: JsObject providedProto })
            {
                instance.SetPrototype(providedProto);
            }
            else if (instance.Prototype is null && arrayPrototype is not null)
            {
                instance.SetPrototype(arrayPrototype);
            }

            // Array(length) or Array(element0, element1, ...)
            if (args is [double length])
            {
                instance.SetProperty("length", length);
                AddArrayMethods(instance, realm, instance.Prototype);
                return instance;
            }

            foreach (var value in args)
            {
                instance.Push(value);
            }

            AddArrayMethods(instance, realm, instance.Prototype);
            return instance;
        });

        arrayConstructor.RealmState = realm;
        realm.ArrayConstructor ??= arrayConstructor;

        // Ensure Array.[[Prototype]] is %FunctionPrototype% even if the shared
        // prototype was not available when the HostFunction was created.
        if (realm.FunctionPrototype is not null)
        {
            arrayConstructor.Properties.SetPrototype(realm.FunctionPrototype);
        }

        // Array.isArray(value)
        var isArrayFn = new HostFunction(args =>
        {
            if (args.Count == 0)
            {
                return false;
            }

            var candidate = args[0];
            var inspected = UnwrapProxy(candidate, realm, "isArray");

            if (inspected is JsArray jsArray)
            {
                if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
                {
                    return false;
                }

                return true;
            }

            if (inspected is JsObject obj && realm.ArrayPrototype is not null &&
                ReferenceEquals(obj, realm.ArrayPrototype))
            {
                return true;
            }

            return false;
        }, isConstructor: false);

        isArrayFn.DefineProperty("name",
            new PropertyDescriptor { Value = "isArray", Writable = false, Enumerable = false, Configurable = true });

        isArrayFn.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });

        arrayConstructor.DefineProperty("isArray",
            new PropertyDescriptor { Value = isArrayFn, Writable = true, Enumerable = false, Configurable = true });

        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) => ArrayFrom(arrayFrom, thisValue, args, realm), realm,
            isConstructor: false);
        AttachBuiltinMetadata(arrayFrom, "from", 1d);
        DefineFunctionProperty(arrayConstructor, "from", arrayFrom);

        HostFunction arrayFromAsync = null!;
        arrayFromAsync = new HostFunction(
            (thisValue, args) => ArrayFromAsync(arrayFromAsync, thisValue, args, realm), realm, isConstructor: false);
        AttachBuiltinMetadata(arrayFromAsync, "fromAsync", 1d);
        arrayFromAsync.Properties.Delete("prototype");
        DefineFunctionProperty(arrayConstructor, "fromAsync", arrayFromAsync);

        HostFunction arrayOf = null!;
        arrayOf = new HostFunction((thisValue, args) => ArrayOf(arrayOf, thisValue, args, realm), realm,
            isConstructor: false);
        AttachBuiltinMetadata(arrayOf, "of", 0d);
        DefineFunctionProperty(arrayConstructor, "of", arrayOf);

        // Expose core Array prototype methods (such as slice) on
        // Array.prototype so patterns like `Array.prototype.slice.call`
        // work against array-like values (e.g. `arguments`).
        if (arrayConstructor.TryGetProperty("prototype", out var prototypeValue) &&
            prototypeValue is JsObject prototypeObject)
        {
            prototypeObject.SetHostedProperty("slice", ArraySlice, realm);
        }

        if (arrayConstructor.TryGetProperty("prototype", out var protoValue) && protoValue is JsObject arrayProtoObj)
        {
            if (realm.ObjectPrototype is not null && arrayProtoObj.Prototype is null)
            {
                arrayProtoObj.SetPrototype(realm.ObjectPrototype);
            }

            arrayPrototype = arrayProtoObj;
            realm.ArrayPrototype ??= arrayProtoObj;
            AddArrayMethods(arrayProtoObj, realm);
            arrayProtoObj.DefineProperty("constructor",
                new PropertyDescriptor
                {
                    Value = arrayConstructor, Writable = true, Enumerable = false, Configurable = true
                });
            arrayProtoObj.DefineProperty("length",
                new PropertyDescriptor { Value = 0d, Writable = true, Enumerable = false, Configurable = false });
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
            if (arrayProtoObj.TryGetProperty("values", out var valuesFn))
            {
                arrayProtoObj.DefineProperty(iteratorKey,
                    new PropertyDescriptor
                    {
                        Value = valuesFn, Writable = true, Enumerable = false, Configurable = true
                    });
            }
        }

        arrayConstructor.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });

        arrayConstructor.DefineProperty("name",
            new PropertyDescriptor { Value = "Array", Writable = false, Enumerable = false, Configurable = true });

        return arrayConstructor;
    }

    private static object? ArrayOf(HostFunction host, object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        const string MethodName = "Array.of";
        var len = args.Count;
        IJsObjectLike result;

        if (thisValue is IJsCallable callable && JsOps.IsConstructor(callable))
        {
            var receiver = CreateArrayLikeReceiverForConstructor(callable, realm, len);
            var constructed = callable.Invoke([(double)len], receiver);
            result = constructed as IJsObjectLike ?? receiver;
        }
        else
        {
            result = CreateDefaultArrayInstance(len, realm);
        }

        for (var k = 0; k < len; k++)
        {
            var key = ToIndexString(k);
            CreateDataPropertyOrThrow(result, key, args[k], realm, MethodName);
        }

        SetArrayLikeLength(result, len);
        return result;

        static IJsObjectLike CreateDefaultArrayInstance(int length, RealmState? realm)
        {
            var arr = new JsArray(realm);
            AddArrayMethods(arr, realm, arr.Prototype);
            arr.SetProperty("length", (double)length);
            return arr;
        }
    }

    private static object? ArrayFrom(HostFunction host, object? thisValue, IReadOnlyList<object?> args,
        RealmState? realm)
    {
        const string MethodName = "Array.from";
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            throw ThrowTypeError("Array.from requires an array-like or iterable", realm: realm);
        }

        var items = args[0];
        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !ReferenceEquals(mapperCandidate, Symbol.Undefined);
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (mapperCandidate is not IJsCallable callableMapper)
            {
                throw ThrowTypeError("Array.from: when provided, the mapping callback must be callable", realm: realm);
            }

            mapper = callableMapper;
        }

        if (TryGetCallableMethod(items, SymbolIteratorKey, MethodName, realm, out var iteratorMethod))
        {
            return ArrayFromIterable(host, thisValue, items, iteratorMethod!, mapper, mapping, thisArg, realm);
        }

        var arrayLike = ToPropertyAccessor(items, MethodName, realm);
        var initialLengthValue = arrayLike.TryGetProperty("length", out var initialLenVal) ? initialLenVal : 0d;
        var initialLength = (long)ToLengthOrZero(initialLengthValue);
        var result = CreateArrayFromResult(thisValue, realm, initialLength, true);

        long k = 0;
        while (true)
        {
            var lengthValue = arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var dynamicLength = (long)ToLengthOrZero(lengthValue);
            if (k >= dynamicLength)
            {
                break;
            }

            if (k >= MaxArrayLength)
            {
                throw ThrowTypeError("Array.from result exceeds 2^53 - 1 elements", realm: realm);
            }

            var key = ToIndexString(k);
            var value = GetElementOrUndefined(arrayLike, key);
            var mapped = mapping && mapper is not null
                ? InvokeArrayFromMapper(mapper, host, thisArg, value, k)
                : value;
            CreateDataPropertyOrThrow(result, key, mapped, realm, MethodName);
            k++;
        }

        SetArrayLikeLength(result, k);
        return result;
    }

    private static object? ArrayFromAsync(HostFunction host, object? thisValue, IReadOnlyList<object?> args,
        RealmState? realm)
    {
        const string MethodName = "Array.fromAsync";
        if (realm?.Engine is null)
        {
            throw new InvalidOperationException("Array.fromAsync requires an active engine.");
        }

        var engine = realm.Engine;
        var promise = new JsPromise(engine);
        AddPromiseInstanceMethods(promise.JsObject, promise, engine);

        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            promise.Reject(CreateTypeError("Array.fromAsync requires an array-like or iterable", realm: realm));
            return promise.JsObject;
        }

        var items = args[0];

        var mapperCandidate = args.GetArgument(1);
        var thisArg = args.GetArgument(2);

        var mapping = !ReferenceEquals(mapperCandidate, Symbol.Undefined);
        IJsCallable? mapper = null;
        if (mapping)
        {
            if (mapperCandidate is not IJsCallable callableMapper)
            {
                promise.Reject(CreateTypeError("Array.fromAsync: when provided, the mapping callback must be callable",
                    realm: realm));
                return promise.JsObject;
            }

            mapper = callableMapper;
        }

        IJsObjectLike result;
        try
        {
            result = CreateArrayFromResult(thisValue, realm, 0, false);
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue ?? signal);
            return promise.JsObject;
        }

        try
        {
            var operation = new ArrayFromAsyncOperation(host, realm, promise, result, mapping, mapper, thisArg,
                MethodName);
            if (TryGetCallableMethod(items, SymbolAsyncIteratorKey, MethodName, realm, out var asyncIterator) &&
                asyncIterator is not null)
            {
                operation.StartIterator(items, asyncIterator, true);
            }
            else if (TryGetCallableMethod(items, SymbolIteratorKey, MethodName, realm, out var syncIterator) &&
                     syncIterator is not null)
            {
                operation.StartIterator(items, syncIterator, false);
            }
            else
            {
                var arrayLike = ToPropertyAccessor(items, MethodName, realm);
                operation.StartArrayLike(arrayLike);
            }
        }
        catch (ThrowSignal signal)
        {
            promise.Reject(signal.ThrownValue ?? signal);
        }

        return promise.JsObject;
    }

    private static object? ArrayFromIterable(HostFunction host, object? thisValue, object? items,
        IJsCallable iteratorMethod, IJsCallable? mapper, bool mapping, object? thisArg, RealmState? realm)
    {
        const string MethodName = "Array.from";
        var result = CreateArrayFromResult(thisValue, realm, 0, false);
        var iteratorValue = iteratorMethod.Invoke([], items);
        if (iteratorValue is not IJsPropertyAccessor iterator)
        {
            throw ThrowTypeError("Array.from iterator method did not return an object", realm: realm);
        }

        if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable nextFn)
        {
            throw ThrowTypeError("Array.from iterator does not expose a callable next()", realm: realm);
        }
        long k = 0;

        while (true)
        {
            object? step;
            try
            {
                step = nextFn.Invoke([], iterator);
            }
            catch (ThrowSignal)
            {
                IteratorClose(iterator, realm, MethodName);
                throw;
            }

            if (step is not IJsPropertyAccessor stepAccessor)
            {
                IteratorClose(iterator, realm, MethodName);
                throw ThrowTypeError("Array.from iterator result is not an object", realm: realm);
            }

            var done = stepAccessor.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue);
            if (done)
            {
                SetArrayLikeLength(result, k);
                return result;
            }

            if (k >= MaxArrayLength)
            {
                IteratorClose(iterator, realm, MethodName);
                throw ThrowTypeError("Array.from result exceeds 2^53 - 1 elements", realm: realm);
            }

            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : Symbol.Undefined;
            object? mappedValue = value;
            if (mapping && mapper is not null)
            {
                try
                {
                    mappedValue = InvokeArrayFromMapper(mapper, host, thisArg, value, k);
                }
                catch (ThrowSignal)
                {
                    IteratorClose(iterator, realm, MethodName);
                    throw;
                }
            }

            try
            {
                CreateDataPropertyOrThrow(result, ToIndexString(k), mappedValue, realm, MethodName);
            }
            catch (ThrowSignal)
            {
                IteratorClose(iterator, realm, MethodName);
                throw;
            }

            k++;
        }
    }

    private static bool TryAwaitPromiseLike(object? candidate, RealmState? realm, Action<object?> onFulfilled,
        Action<object?> onRejected)
    {
        if (candidate is JsObject jsObject &&
            jsObject.TryGetProperty("then", out var thenValue) &&
            thenValue is IJsCallable thenCallable)
        {
            var engine = realm?.Engine;
            var fulfilled = new HostFunction((_, args) =>
            {
                var value = args.GetArgument(0);
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onFulfilled(value);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onFulfilled(value);
                }

                return Symbol.Undefined;
            }, realm);
            var rejected = new HostFunction((_, args) =>
            {
                var reason = args.GetArgument(0);
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onRejected(reason);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onRejected(reason);
                }

                return Symbol.Undefined;
            }, realm);

            try
            {
                thenCallable.Invoke([fulfilled, rejected], jsObject);
            }
            catch (ThrowSignal signal)
            {
                if (engine is not null)
                {
                    engine.ScheduleTask(() =>
                    {
                        onRejected(signal.ThrownValue ?? signal);
                        return Task.CompletedTask;
                    });
                }
                else
                {
                    onRejected(signal.ThrownValue ?? signal);
                }
            }

            return true;
        }

        return false;
    }

    private static object? InvokeArrayFromMapper(IJsCallable mapper, HostFunction host, object? thisArg, object? value,
        long index)
    {
        if (mapper is IJsEnvironmentAwareCallable envAware && host.CallingJsEnvironment is not null)
        {
            envAware.CallingJsEnvironment = host.CallingJsEnvironment;
        }

        return mapper.Invoke([value, (double)index], thisArg);
    }

    private static void DefineArrayFunction(IJsPropertyAccessor target, string name, double length,
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler, RealmState? realm)
    {
        var fn = new HostFunction(handler, realm, isConstructor: false);
        fn.Properties.Delete("prototype");
        AttachBuiltinMetadata(fn, name, length);
        DefineFunctionProperty(target, name, fn);
    }

    private static void AttachBuiltinMetadata(HostFunction fn, string name, double length)
    {
        fn.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });
        fn.DefineProperty("length",
            new PropertyDescriptor { Value = length, Writable = false, Enumerable = false, Configurable = true });
    }

    private static void DefineFunctionProperty(IJsPropertyAccessor target, string name, HostFunction fn)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = fn, Writable = true, Enumerable = false, Configurable = true
        };

        if (target is IJsObjectLike objectLike)
        {
            objectLike.DefineProperty(name, descriptor);
        }
        else
        {
            target.SetProperty(name, fn);
        }
    }

    private sealed class ArrayFromAsyncOperation(
        HostFunction host,
        RealmState realm,
        JsPromise promise,
        IJsObjectLike result,
        bool mapping,
        IJsCallable? mapper,
        object? thisArg,
        string methodName)
    {
        private long _index;
        private bool _settled;
        private IJsPropertyAccessor? _iterator;
        private IJsCallable? _nextFn;
        private bool _awaitIteratorResult;
        private IJsPropertyAccessor? _arrayLike;

        public void StartIterator(object? items, IJsCallable iteratorMethod, bool awaitIteratorResult)
        {
            object? iteratorValue;
            try
            {
                iteratorValue = iteratorMethod.Invoke([], items);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            if (iteratorValue is not IJsPropertyAccessor iterator)
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator method did not return an object", null,
                    realm));
                return;
            }

            if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable nextFn)
            {
                RejectFailure(CreateTypeError("Array.fromAsync iterator does not expose a callable next()", null,
                    realm));
                return;
            }

            _iterator = iterator;
            _nextFn = nextFn;
            _awaitIteratorResult = awaitIteratorResult;
            ProcessIteratorStep();
        }

        public void StartArrayLike(IJsPropertyAccessor arrayLike)
        {
            _arrayLike = arrayLike;
            ProcessArrayLike();
        }

        private void ProcessIteratorStep()
        {
            if (_settled || _iterator is null || _nextFn is null)
            {
                return;
            }

            object? step;
            try
            {
                step = _nextFn.Invoke([], _iterator);
            }
            catch (ThrowSignal signal)
            {
                RejectSignal(signal);
                return;
            }

            HandleIteratorStep(step);
        }

        private void HandleIteratorStep(object? stepCandidate)
        {
            if (_settled)
            {
                return;
            }

            if (_awaitIteratorResult && TryAwaitPromiseLike(stepCandidate, realm, HandleIteratorStep,
                    RejectWithClose))
            {
                return;
            }

            if (stepCandidate is not IJsPropertyAccessor stepAccessor)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync iterator result is not an object", null, realm));
                return;
            }

            var done = stepAccessor.TryGetProperty("done", out var doneValue) && JsOps.ToBoolean(doneValue);
            if (done)
            {
                ResolveSuccess();
                return;
            }

            if (_index >= MaxArrayLength)
            {
                RejectWithClose(CreateTypeError("Array.fromAsync result exceeds 2^53 - 1 elements", null, realm));
                return;
            }

            var value = stepAccessor.TryGetProperty("value", out var entryValue) ? entryValue : Symbol.Undefined;
            if (TryAwaitPromiseLike(value, realm, HandleIteratorValue, RejectWithClose))
            {
                return;
            }

            HandleIteratorValue(value);
        }

        private void HandleIteratorValue(object? value)
        {
            if (_settled)
            {
                return;
            }

            if (mapping && mapper is not null)
            {
                object? mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, value, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectWithClose(signal.ThrownValue ?? signal);
                    return;
                }

                if (TryAwaitPromiseLike(mapperResult, realm, StoreIteratorValue, RejectWithClose))
                {
                    return;
                }

                StoreIteratorValue(mapperResult);
                return;
            }

            StoreIteratorValue(value);
        }

        private void StoreIteratorValue(object? value)
        {
            if (_settled)
            {
                return;
            }

            try
            {
                CreateDataPropertyOrThrow(result, ToIndexString(_index), value, realm, methodName);
            }
            catch (ThrowSignal signal)
            {
                RejectWithClose(signal.ThrownValue ?? signal);
                return;
            }

            _index++;
            ProcessIteratorStep();
        }

        private void ProcessArrayLike()
        {
            if (_settled || _arrayLike is null)
            {
                return;
            }

            var lengthValue = _arrayLike.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var dynamicLength = (long)ToLengthOrZero(lengthValue);
            if (_index >= dynamicLength)
            {
                ResolveSuccess();
                return;
            }

            if (_index >= MaxArrayLength)
            {
                RejectFailure(CreateTypeError("Array.fromAsync result exceeds 2^53 - 1 elements", null, realm));
                return;
            }

            var key = ToIndexString(_index);
            var value = GetElementOrUndefined(_arrayLike, key);
            if (TryAwaitPromiseLike(value, realm, resolved => HandleArrayLikeValue(key, resolved), RejectFailure))
            {
                return;
            }

            HandleArrayLikeValue(key, value);
        }

        private void HandleArrayLikeValue(string key, object? resolved)
        {
            if (_settled)
            {
                return;
            }

            void FinalizeValue(object? finalValue)
            {
                if (_settled)
                {
                    return;
                }

                try
                {
                    CreateDataPropertyOrThrow(result, key, finalValue, realm, methodName);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailure(signal.ThrownValue ?? signal);
                    return;
                }

                _index++;
                ProcessArrayLike();
            }

            if (mapping && mapper is not null)
            {
                object? mapperResult;
                try
                {
                    mapperResult = InvokeArrayFromMapper(mapper, host, thisArg, resolved, _index);
                }
                catch (ThrowSignal signal)
                {
                    RejectFailure(signal.ThrownValue ?? signal);
                    return;
                }

                if (TryAwaitPromiseLike(mapperResult, realm, FinalizeValue, RejectFailure))
                {
                    return;
                }

                FinalizeValue(mapperResult);
                return;
            }

            FinalizeValue(resolved);
        }

        private void ResolveSuccess()
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            SetArrayLikeLength(result, _index);
            promise.Resolve(result);
        }

        private void RejectFailure(object? reason)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            promise.Reject(reason);
        }

        private void RejectWithClose(object? reason)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;

            if (_iterator is null)
            {
                promise.Reject(reason);
                return;
            }

            if (!_iterator.TryGetProperty("return", out var returnValue) ||
                returnValue is null ||
                ReferenceEquals(returnValue, Symbol.Undefined))
            {
                promise.Reject(reason);
                return;
            }

            if (returnValue is not IJsCallable returnFn)
            {
                promise.Reject(CreateTypeError($"{methodName} iterator.return is not callable", null, realm));
                return;
            }

            object? completion;
            try
            {
                completion = returnFn.Invoke([], _iterator);
            }
            catch (ThrowSignal signal)
            {
                promise.Reject(signal.ThrownValue ?? signal);
                return;
            }

            if (TryAwaitPromiseLike(completion, realm,
                    _ => promise.Reject(reason),
                    rejection => promise.Reject(rejection)))
            {
                return;
            }

            promise.Reject(reason);
        }

        private void RejectSignal(ThrowSignal signal)
        {
            RejectFailure(signal.ThrownValue ?? signal);
        }
    }

    private static readonly string SymbolSpeciesKey = $"@@symbol:{TypedAstSymbol.For("Symbol.species").GetHashCode()}";
    private static readonly string SymbolIteratorKey = $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";
    private static readonly string SymbolAsyncIteratorKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.asyncIterator").GetHashCode()}";
    private static readonly string SymbolIsConcatSpreadableKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.isConcatSpreadable").GetHashCode()}";

    private static IJsObjectLike ArraySpeciesCreate(object? original, long length, RealmState? realm)
    {
        length = Math.Max(length, 0);

        IJsObjectLike CreateDefaultArray()
        {
            var arr = new JsArray(realm);
            AddArrayMethods(arr, realm, arr.Prototype);
            arr.SetProperty("length", (double)length);
            return arr;
        }

        if (realm is null)
        {
            return CreateDefaultArray();
        }

        if (original is not IJsPropertyAccessor accessor)
        {
            return CreateDefaultArray();
        }

        if (!IsArrayObject(original, realm, "array species creation"))
        {
            return CreateDefaultArray();
        }

        var useDefaultConstructor = false;
        if (!accessor.TryGetProperty("constructor", out var constructorValue) ||
            ReferenceEquals(constructorValue, Symbol.Undefined))
        {
            useDefaultConstructor = true;
        }

        if (!useDefaultConstructor &&
            constructorValue is HostFunction hostCtor &&
            hostCtor.RealmState is { } ctorRealm &&
            !ReferenceEquals(ctorRealm, realm) &&
            ReferenceEquals(hostCtor, ctorRealm.ArrayConstructor))
        {
            useDefaultConstructor = true;
        }

        object? constructor = constructorValue;

        if (!useDefaultConstructor && constructor is IJsPropertyAccessor ctorAccessor)
        {
            object? species = null;
            if (ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
            {
                species = speciesValue;
            }

            if (species is null || ReferenceEquals(species, Symbol.Undefined))
            {
                useDefaultConstructor = true;
            }
            else
            {
                constructor = species;
            }
        }

        if (useDefaultConstructor)
        {
            return CreateDefaultArray();
        }

        if (constructor is not IJsCallable callable || !JsOps.IsConstructor(callable))
        {
            throw ThrowTypeError("Array species constructor must be a constructor", realm: realm);
        }

        var proto = ResolveConstructPrototype(callable, callable, realm);
        IJsObjectLike receiver;

        if (callable is HostFunction hostFunction && realm?.ArrayConstructor is not null &&
            ReferenceEquals(hostFunction, realm.ArrayConstructor))
        {
            receiver = new JsArray(realm);
        }
        else
        {
            receiver = new JsObject();
        }

        if (proto is not null)
        {
            receiver.SetPrototype(proto);
        }

        var constructed = callable.Invoke([(double)length], receiver);
        if (constructed is IJsObjectLike objectLike)
        {
            return objectLike;
        }

        return receiver;
    }

    private static IJsObjectLike CreateArrayFromResult(object? constructorCandidate, RealmState? realm, long length,
        bool passLengthToConstructor)
    {
        if (constructorCandidate is IJsCallable callable && JsOps.IsConstructor(callable))
        {
            var receiver = CreateArrayLikeReceiverForConstructor(callable, realm, passLengthToConstructor ? length : 0);
            var args = passLengthToConstructor
                ? new object?[] { (double)Math.Max(length, 0) }
                : Array.Empty<object?>();
            var constructed = callable.Invoke(args, receiver);
            var result = constructed as IJsObjectLike ?? receiver;
            if (!passLengthToConstructor)
            {
                SetArrayLikeLength(result, 0);
            }

            return result;
        }

        var array = new JsArray(realm);
        AddArrayMethods(array, realm, array.Prototype);
        array.SetProperty("length", passLengthToConstructor ? (double)Math.Max(length, 0) : 0d);
        return array;
    }

    private static IJsObjectLike CreateArrayLikeReceiverForConstructor(IJsCallable constructor, RealmState? realm,
        long length)
    {
        var proto = ResolveConstructPrototype(constructor, constructor, realm);
        IJsObjectLike receiver;

        if (constructor is HostFunction hostFunction && realm?.ArrayConstructor is not null &&
            ReferenceEquals(hostFunction, realm.ArrayConstructor))
        {
            var array = new JsArray(realm);
            AddArrayMethods(array, realm, array.Prototype);
            receiver = array;
        }
        else
        {
            receiver = new JsObject();
        }

        if (proto is not null)
        {
            receiver.SetPrototype(proto);
        }

        receiver.SetProperty("length", (double)Math.Max(length, 0));
        return receiver;
    }

    private static void DeletePropertyOrThrow(IJsObjectLike? objectLike, string propertyKey, bool propertyExisted,
        string methodName, RealmState? realm)
    {
        if (objectLike is null)
        {
            if (propertyExisted)
            {
                throw ThrowTypeError($"{methodName} receiver does not support deleting property '{propertyKey}'",
                    realm: realm);
            }

            return;
        }

        if (!objectLike.Delete(propertyKey) && propertyExisted)
        {
            throw ThrowTypeError($"{methodName} could not delete property '{propertyKey}'", realm: realm);
        }
    }

    private static bool IsConcatSpreadable(object? candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate is not IJsPropertyAccessor propertyAccessor)
        {
            return false;
        }

        if (propertyAccessor.TryGetProperty(SymbolIsConcatSpreadableKey, out var spreadableValue) &&
            !ReferenceEquals(spreadableValue, Symbol.Undefined))
        {
            if (JsOps.IsTruthy(spreadableValue))
            {
                accessor = propertyAccessor;
                return true;
            }

            return false;
        }

        if (IsArrayObject(candidate, realm, operation))
        {
            accessor = propertyAccessor;
            return true;
        }

        return false;
    }

    private static bool TryGetArrayForFlatten(object? candidate, RealmState? realm, string operation,
        out IJsPropertyAccessor accessor)
    {
        accessor = null!;
        if (candidate is not IJsPropertyAccessor propertyAccessor)
        {
            return false;
        }

        var inspected = UnwrapProxy(candidate, realm, operation);

        if (inspected is JsArray jsArray)
        {
            if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
            {
                return false;
            }

            accessor = propertyAccessor;
            return true;
        }

        if (inspected is JsObject obj && realm?.ArrayPrototype is not null &&
            ReferenceEquals(obj, realm.ArrayPrototype))
        {
            accessor = propertyAccessor;
            return true;
        }

        return false;
    }

    private static object? UnwrapProxy(object? candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected is JsProxy proxy)
        {
            if (proxy.Handler is null)
            {
                throw ThrowTypeError($"Cannot perform '{operation}' with a revoked Proxy", realm: realm);
            }

            inspected = proxy.Target;
        }

        return inspected;
    }

    private static bool IsArrayObject(object? candidate, RealmState? realm, string operation)
    {
        var inspected = candidate;
        while (inspected is not null)
        {
            if (inspected is JsArray)
            {
                return true;
            }

            if (inspected is JsObject obj && realm?.ArrayPrototype is not null &&
                ReferenceEquals(obj, realm.ArrayPrototype))
            {
                return true;
            }

            if (inspected is JsProxy proxy)
            {
                if (proxy.Handler is null)
                {
                    throw ThrowTypeError($"Cannot perform '{operation}' with a revoked Proxy", realm: realm);
                }

                inspected = proxy.Target;
                continue;
            }

            break;
        }

        return false;
    }

    private static void CopyArrayElement(IJsPropertyAccessor source, long sourceIndex, IJsPropertyAccessor target,
        long targetIndex)
    {
        var sourceKey = ToIndexString(sourceIndex);
        var targetKey = ToIndexString(targetIndex);

        if (TryGetExistingElement(source, sourceKey, out var value))
        {
            target.SetProperty(targetKey, value);
            return;
        }

        if (target is IJsObjectLike targetLike)
        {
            targetLike.Delete(targetKey);
        }
        else
        {
            target.SetProperty(targetKey, Symbol.Undefined);
        }
    }

    private static long FlattenIntoArray(IJsPropertyAccessor target, IJsPropertyAccessor source, long sourceLength,
        long targetIndex, long depth, IJsCallable? mapper, object? thisArg, RealmState? realm, string operation)
    {
        for (long k = 0; k < sourceLength; k++)
        {
            var key = ToIndexString(k);
            if (!TryGetExistingElement(source, key, out var element))
            {
                continue;
            }

            object? mappedValue = element;
            if (mapper is not null)
            {
                mappedValue = mapper.Invoke([element, (double)k, source], thisArg ?? Symbol.Undefined);
            }

            if (depth > 0 && TryGetArrayForFlatten(mappedValue, realm, operation, out var flattenAccessor))
            {
                var lengthValue = flattenAccessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
                var elementLength = (long)ToLengthOrZero(lengthValue);
                targetIndex =
                    FlattenIntoArray(target, flattenAccessor, elementLength, targetIndex, depth - 1, null, null, realm,
                        operation);
                continue;
            }

            if (targetIndex >= MaxArrayLength)
            {
                throw ThrowTypeError("Flattened array exceeds maximum length", realm: realm);
            }

            target.SetProperty(ToIndexString(targetIndex), mappedValue);
            targetIndex++;
        }

        return targetIndex;
    }

    private static void SetArrayLikeLength(IJsPropertyAccessor target, long length)
    {
        target.SetProperty("length", (double)Math.Max(length, 0));
    }

    private static long LengthOfArrayLike(object? target, RealmState? realm, string operation)
    {
        if (target is null || ReferenceEquals(target, Symbol.Undefined))
        {
            throw ThrowTypeError($"{operation} called on null or undefined", realm: realm);
        }

        if (!JsOps.TryGetPropertyValue(target, "length", out var lengthValue))
        {
            lengthValue = 0d;
        }

        var numericContext = realm?.CreateContext();
        return (long)ToLengthOrZero(lengthValue, numericContext);
    }

    private static (IJsPropertyAccessor Accessor, long Length, IJsCallable Callback, object? ThisArg)
        PrepareArrayIteration(object? receiver, IReadOnlyList<object?> args, RealmState? realm, string methodName)
    {
        var accessor = EnsureArrayLikeReceiver(receiver, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);
        var thisArg = args.GetArgument(1);
        return (accessor, length, callback, thisArg);
    }

    private static string ToIndexString(long index)
    {
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private static double ToLengthOrZero(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context is not null && context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(number))
        {
            return MaxArrayLength;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLength ? MaxArrayLength : truncated;
    }

    private static double ToIntegerOrInfinity(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context is not null && context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number))
        {
            return 0;
        }

        if (double.IsInfinity(number) || number == 0)
        {
            return number;
        }

        return Math.Sign(number) * Math.Floor(Math.Abs(number));
    }

    private static long ClampRelativeIndex(double index, long length)
    {
        if (double.IsNegativeInfinity(index))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(index))
        {
            return length;
        }

        var integer = (long)Math.Truncate(index);
        if (integer < 0)
        {
            var relative = length + integer;
            return relative < 0 ? 0 : relative;
        }

        return integer > length ? length : integer;
    }

    internal static object? ReduceLike(object? thisValue, IReadOnlyList<object?> args, RealmState? realm,
        string methodName, bool fromRight)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        if (accessor is TypedArrayBase typed)
        {
            if (typed.IsDetachedOrOutOfBounds())
            {
                throw typed.CreateOutOfBoundsTypeError();
            }

            var length = typed.Length;
            var step = fromRight ? -1 : 1;
            var index = fromRight ? length - 1 : 0;

            var hasAccumulator = args.Count > 1;
            var accumulator = hasAccumulator ? args[1] : null;

            if (!hasAccumulator)
            {
                if (length == 0)
                {
                    throw ThrowTypeError("Reduce of empty array with no initial value", realm: realm);
                }

                accumulator = typed.GetValueForIndex(index);
                index += step;
            }

            while (index >= 0 && index < length)
            {
                if (typed.IsDetachedOrOutOfBounds())
                {
                    throw typed.CreateOutOfBoundsTypeError();
                }

                var current = typed.GetValueForIndex(index);
                accumulator = callback.Invoke([accumulator, current, (double)index, typed], Symbol.Undefined);
                index += step;
            }

            return accumulator;
        }

        var lengthValue = accessor.TryGetProperty("length", out var len) ? len : 0d;
        var lengthGeneric = (int)ToLengthOrZero(lengthValue);
        var stepGeneric = fromRight ? -1 : 1;
        var indexGeneric = fromRight ? lengthGeneric - 1 : 0;

        var hasAccumulatorGeneric = args.Count > 1;
        var accumulatorGeneric = hasAccumulatorGeneric ? args[1] : null;

        if (!hasAccumulatorGeneric)
        {
            var found = false;
            while (indexGeneric >= 0 && indexGeneric < lengthGeneric)
            {
                if (TryGetExistingElement(accessor, indexGeneric, out var current))
                {
                    accumulatorGeneric = current;
                    found = true;
                    indexGeneric += stepGeneric;
                    break;
                }

                indexGeneric += stepGeneric;
            }

            if (!found)
            {
                throw ThrowTypeError("Reduce of empty array with no initial value", realm: realm);
            }
        }

        while (indexGeneric >= 0 && indexGeneric < lengthGeneric)
        {
            if (TryGetExistingElement(accessor, indexGeneric, out var current))
            {
                accumulatorGeneric = callback.Invoke([accumulatorGeneric, current, (double)indexGeneric, accessor],
                    Symbol.Undefined);
            }

            indexGeneric += stepGeneric;
        }

        return accumulatorGeneric;
    }

    internal static object? SomeLike(object? thisValue, IReadOnlyList<object?> args, RealmState? realm,
        string methodName)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);

        if (accessor is TypedArrayBase typed)
        {
            if (typed.IsDetachedOrOutOfBounds())
            {
                throw typed.CreateOutOfBoundsTypeError();
            }

            var length = typed.Length;
            for (var i = 0; i < length; i++)
            {
                if (typed.IsDetachedOrOutOfBounds())
                {
                    throw typed.CreateOutOfBoundsTypeError();
                }

                var value = typed.GetValueForIndex(i);
                var testResult = callback.Invoke([value, (double)i, typed], thisArg);
                if (IsTruthy(testResult))
                {
                    return true;
                }
            }

            return false;
        }

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var objectLength = (long)ToLengthOrZero(lengthValue);
        for (long i = 0; i < objectLength; i++)
        {
            if (!TryGetExistingElement(accessor, i, out var value))
            {
                continue;
            }

            var testResult = callback.Invoke([value, (double)i, accessor], thisArg);
            if (IsTruthy(testResult))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameValueZero(object? x, object? y)
    {
        if (x is double.NaN && y is double.NaN)
        {
            return true;
        }

        return JsOps.StrictEquals(x, y);
    }

    private static IJsPropertyAccessor EnsureArrayLikeReceiver(object? receiver, string methodName, RealmState? realm)
    {
        if (receiver is null || ReferenceEquals(receiver, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        switch (receiver)
        {
            case IJsPropertyAccessor accessor when accessor is not TypedAstSymbol:
            {
                if (accessor is not JsObject jsObj || !jsObj.TryGetProperty("__value__", out var inner) ||
                    inner is not string sInner)
                {
                    return accessor;
                }

                if (!jsObj.TryGetProperty("length", out _))
                {
                    jsObj.DefineProperty("length",
                        new PropertyDescriptor
                        {
                            Value = (double)sInner.Length,
                            Writable = false,
                            Enumerable = false,
                            Configurable = false
                        });
                }

                for (var i = 0; i < sInner.Length; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    if (!jsObj.TryGetProperty(key, out _))
                    {
                        jsObj.SetProperty(key, sInner[i].ToString());
                    }
                }

                return jsObj;
            }
            // Box primitives to objects per ToObject.
            case string s:
            {
                var obj = new JsObject();
                if (realm?.StringPrototype is not null)
                {
                    obj.SetPrototype(realm.StringPrototype);
                }

                obj.SetProperty("__value__", s);
                obj.DefineProperty("length",
                    new PropertyDescriptor
                    {
                        Value = (double)s.Length, Writable = false, Enumerable = false, Configurable = false
                    });

                for (var i = 0; i < s.Length; i++)
                {
                    obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), s[i].ToString());
                }

                return obj;
            }
            case double or int or uint or long or ulong or short or ushort or byte or sbyte or decimal or float:
            {
                var obj = new JsObject();
                if (realm?.NumberPrototype is not null)
                {
                    obj.SetPrototype(realm.NumberPrototype);
                }

                obj.SetProperty("__value__", receiver);
                return obj;
            }
            case bool b:
            {
                var obj = new JsObject();
                if (realm?.BooleanPrototype is not null)
                {
                    obj.SetPrototype(realm.BooleanPrototype);
                }

                obj.SetProperty("__value__", b);
                return obj;
            }
            // Symbols and BigInts should throw TypeError for array methods
            case TypedAstSymbol:
            case JsBigInt:
                throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
            default:
                throw ThrowTypeError($"{methodName} called on non-object", realm: realm);
        }
    }

    private static bool TryGetCallableMethod(object? target, string propertyKey, string operation, RealmState? realm,
        out IJsCallable? callable)
    {
        callable = null;
        if (!JsOps.TryGetPropertyValue(target, propertyKey, out var candidate))
        {
            return false;
        }

        if (candidate is null || ReferenceEquals(candidate, Symbol.Undefined))
        {
            return false;
        }

        if (candidate is not IJsCallable method)
        {
            throw ThrowTypeError($"{operation} property '{propertyKey}' is not callable", realm: realm);
        }

        callable = method;
        return true;
    }

    private static IJsPropertyAccessor ToPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} requires an array-like or iterable", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} could not convert the source to an object", realm: realm);
    }

    private static IJsPropertyAccessor ToObjectPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm ?? new RealmState(), out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} called on non-object", realm: realm);
    }

    private static void IteratorClose(IJsPropertyAccessor iterator, RealmState? realm, string operation)
    {
        if (!iterator.TryGetProperty("return", out var returnValue) ||
            returnValue is null ||
            ReferenceEquals(returnValue, Symbol.Undefined))
        {
            return;
        }

        if (returnValue is not IJsCallable returnFn)
        {
            throw ThrowTypeError($"{operation} iterator.return is not callable", realm: realm);
        }

        _ = returnFn.Invoke([], iterator);
    }

    private static void CreateDataPropertyOrThrow(IJsObjectLike target, string propertyKey, object? value,
        RealmState? realm, string operation)
    {
        try
        {
            var descriptor = new PropertyDescriptor
            {
                Value = value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
            target.DefineProperty(propertyKey, descriptor);
        }
        catch (ThrowSignal)
        {
            throw;
        }
        catch (Exception)
        {
            throw ThrowTypeError($"{operation} could not define property '{propertyKey}'", realm: realm);
        }

        var defined = target.GetOwnPropertyDescriptor(propertyKey);
        if (defined is null || !defined.Writable || !defined.Configurable || !defined.Enumerable)
        {
            throw ThrowTypeError($"{operation} could not define property '{propertyKey}'", realm: realm);
        }
    }

    private static bool TryGetExistingElement(IJsPropertyAccessor accessor, long index, out object? value)
    {
        return TryGetExistingElement(accessor, ToIndexString(index), out value);
    }

    private static bool TryGetExistingElement(IJsPropertyAccessor accessor, string propertyKey, out object? value)
    {
        if (!HasProperty(accessor, propertyKey))
        {
            value = null;
            return false;
        }

        if (!accessor.TryGetProperty(propertyKey, out value))
        {
            value = Symbol.Undefined;
        }

        return true;
    }

    private static object? GetElementOrUndefined(IJsPropertyAccessor accessor, string propertyKey)
    {
        return accessor.TryGetProperty(propertyKey, out var value) ? value : Symbol.Undefined;
    }

    private static object InvokeDefaultObjectToString(object? target, RealmState? realm)
    {
        if (realm?.ObjectPrototype is IJsPropertyAccessor objectPrototype &&
            objectPrototype.TryGetProperty("toString", out var toStringValue) &&
            toStringValue is IJsCallable callable)
        {
            return callable.Invoke([], target);
        }

        return "[object Object]";
    }
}
