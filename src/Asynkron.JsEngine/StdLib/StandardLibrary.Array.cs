using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
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
        array.SetHostedProperty("push", ArrayPush);

        array.SetHostedProperty("pop", ArrayPop);
        array.SetHostedProperty("map", ArrayMap, realm);
        array.SetHostedProperty("filter", ArrayFilter, realm);
        array.SetHostedProperty("reduce", ArrayReduce, realm);
        array.SetHostedProperty("reduceRight", ArrayReduceRight, realm);
        array.SetHostedProperty("forEach", ArrayForEach);
        array.SetHostedProperty("find", ArrayFind);
        array.SetHostedProperty("findIndex", ArrayFindIndex);
        array.SetHostedProperty("some", ArraySome);
        array.SetHostedProperty("every", ArrayEvery);
        array.SetHostedProperty("join", ArrayJoin);
        array.SetHostedProperty("toString", (thisValue, _) => ArrayToString(thisValue, array));
        array.SetHostedProperty("includes", ArrayIncludes, realm);
        array.SetHostedProperty("indexOf", ArrayIndexOf, realm);
        var lastIndexOf = new HostFunction((thisValue, args) => ArrayLastIndexOf(thisValue, args, realm), realm)
        {
            IsConstructor = false
        };
        lastIndexOf.DefineProperty("name",
            new PropertyDescriptor { Value = "lastIndexOf", Writable = false, Enumerable = false, Configurable = true });
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
        array.SetHostedProperty("shift", ArrayShift);
        array.SetHostedProperty("unshift", ArrayUnshift);
        array.SetHostedProperty("splice", ArraySplice, realm);
        array.SetHostedProperty("concat", ArrayConcat, realm);
        array.SetHostedProperty("reverse", ArrayReverse);
        array.SetHostedProperty("sort", ArraySort);
        array.SetHostedProperty("at", ArrayAt);
        array.SetHostedProperty("flat", ArrayFlat, realm);
        array.SetHostedProperty("flatMap", ArrayFlatMap, realm);
        array.SetHostedProperty("findLast", ArrayFindLast);
        array.SetHostedProperty("findLastIndex", ArrayFindLastIndex);
        array.SetHostedProperty("fill", ArrayFill);
        array.SetHostedProperty("copyWithin", ArrayCopyWithin);
        array.SetHostedProperty("toSorted", ArrayToSorted, realm);
        array.SetHostedProperty("toReversed", ArrayToReversed, realm);
        array.SetHostedProperty("toSpliced", ArrayToSpliced, realm);
        array.SetHostedProperty("with", ArrayWith, realm);

        // entries() - returns an iterator of [index, value] pairs
        DefineArrayIteratorFunction("entries", (accessor, _) => idx =>
        {
            var pair = new JsArray(realm);
            pair.Push((double)idx);
            if (accessor.TryGetProperty(idx.ToString(CultureInfo.InvariantCulture), out var value))
            {
                pair.Push(value);
            }
            else
            {
                pair.Push(Symbols.Undefined);
            }

            AddArrayMethods(pair, realm);
            return pair;
        });

        // keys() - returns an iterator of indices
        DefineArrayIteratorFunction("keys", (_, _) => idx => (double)idx);

        // values() - returns an iterator of values
        var valuesFn = DefineArrayIteratorFunction("values", (accessor, _) => idx =>
        {
            var key = idx.ToString(CultureInfo.InvariantCulture);
            return accessor.TryGetProperty(key, out var value) ? value : Symbols.Undefined;
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
            return Math.Min(truncated, 9007199254740991d); // 2^53 - 1
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
                    doneResult.SetProperty("value", Symbols.Undefined);
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
                    result.SetProperty("value", Symbols.Undefined);
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
                if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
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
            }) { IsConstructor = false };

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

    private static object? ArrayPush(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        foreach (var arg in args)
        {
            jsArray.Push(arg);
        }

        return jsArray.Items.Count;
    }

    private static object? ArrayPop(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsArray jsArray ? jsArray.Pop() : null;
    }

    private static object? ArrayMap(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        var thisArg = args.Count > 1 ? args[1] : Symbols.Undefined;
        var result = new JsArray(realm);
        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var mapped = callback.Invoke([element, (double)i, jsArray], thisArg);
            result.Push(mapped);
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayFilter(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        var result = new JsArray(realm);
        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var keep = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(keep))
            {
                result.Push(element);
            }
        }

        AddArrayMethods(result, realm);
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

    private static object? ArrayForEach(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            callback.Invoke([element, (double)i, jsArray], null);
        }

        return null;
    }

    private static object? ArrayFind(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var match = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(match))
            {
                return element;
            }
        }

        return null;
    }

    private static object? ArrayFindIndex(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return -1d;
        }

        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var match = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(match))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    private static object? ArraySome(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return false;
        }

        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var result = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(result))
            {
                return true;
            }
        }

        return false;
    }

    private static object? ArrayEvery(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return true;
        }

        if (args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return true;
        }

        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var result = callback.Invoke([element, (double)i, jsArray], null);
            if (!IsTruthy(result))
            {
                return false;
            }
        }

        return true;
    }

    private static object? ArrayJoin(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return "";
        }

        var separator = args.Count > 0 && args[0] is string sep ? sep : ",";

        var length = jsArray.Length > int.MaxValue ? int.MaxValue : (int)jsArray.Length;
        var parts = new List<string>(length);
        for (var i = 0; i < length; i++)
        {
            var element = jsArray.GetElement(i);
            parts.Add(element.ToJsStringForArray());
        }

        return string.Join(separator, parts);
    }

    private static object? ArrayToString(object? thisValue, IJsPropertyAccessor arrayAccessor)
    {
        if (thisValue is JsArray jsArray)
        {
            return arrayAccessor.TryGetProperty("join", out var join) && join is IJsCallable joinFn
                ? joinFn.Invoke([], jsArray)
                : string.Empty;
        }

        if (thisValue is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("join", out var joinVal) &&
            joinVal is IJsCallable callableJoin)
        {
            return callableJoin.Invoke([], thisValue);
        }

        return "[object Object]";
    }

    private static object? ArrayIncludes(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.includes", realm);

        var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
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
        var lenLong = (long)Math.Min(length, 9007199254740991d);

        if (accessor is JsArray jsArr && lenLong > 100000)
        {
            var indices = jsArr.GetOwnIndices()
                .Where(idx => (long)idx >= start && (long)idx < lenLong)
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
                var key = i.ToString(CultureInfo.InvariantCulture);
                var exists = accessor.TryGetProperty(key, out var value);
                if (exists && SameValueZero(value, searchElement))
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
        var evalContext = realm is not null ? new EvaluationContext(realm) : null;
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
        var lenLong = (long)Math.Min(length, 9007199254740991d);

        for (var i = start; i < lenLong; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (accessor.TryGetProperty(key, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    private static object? ArrayLastIndexOf(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.lastIndexOf", realm);
        if (accessor is TypedArrayBase typed)
        {
            return TypedArrayBase.LastIndexOfInternal(typed, args);
        }

        var evalContext = realm is not null ? new EvaluationContext(realm) : null;
        var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;
        if (length <= 0)
        {
            return -1d;
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : length - 1;
        var lenLong = (long)Math.Min(length, 9007199254740991d);

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
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (accessor.TryGetProperty(key, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    private static object? ArrayToLocaleString(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString", realm);

        var locales = args.Count > 0 ? args[0] : Symbols.Undefined;
        var options = args.Count > 1 ? args[1] : Symbols.Undefined;
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
        var parts = new List<string>((int)length);

        for (var i = 0; i < length; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (!accessor.TryGetProperty(key, out var element) ||
                element is null ||
                ReferenceEquals(element, Symbols.Undefined))
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

    private static object? ArrayShift(object? thisValue, IReadOnlyList<object?> _)
    {
        return thisValue is JsArray jsArray ? jsArray.Shift() : null;
    }

    private static object? ArrayUnshift(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return 0;
        }

        jsArray.Unshift(args.ToArray());
        return jsArray.Items.Count;
    }

    private static object? ArraySplice(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var start = args.Count > 0 && args[0] is double startD ? (int)startD : 0;
        var deleteCount = args.Count > 1 && args[1] is double deleteD ? (int)deleteD : jsArray.Items.Count - start;

        var itemsToInsert = args.Count > 2 ? args.Skip(2).ToArray() : [];

        var deleted = jsArray.Splice(start, deleteCount, itemsToInsert);
        AddArrayMethods(deleted, realm);
        return deleted;
    }

    private static object? ArrayConcat(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var result = new JsArray(realm);
        foreach (var item in jsArray.Items)
        {
            result.Push(item);
        }

        foreach (var arg in args)
        {
            if (arg is JsArray argArray)
            {
                foreach (var item in argArray.Items)
                {
                    result.Push(item);
                }
            }
            else
            {
                result.Push(arg);
            }
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayReverse(object? thisValue, IReadOnlyList<object?> _)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        jsArray.Reverse();
        return jsArray;
    }

    private static object? ArraySort(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var items = jsArray.Items.ToList();

        if (args.Count > 0 && args[0] is IJsCallable compareFn)
        {
            items.Sort((a, b) =>
            {
                var result = compareFn.Invoke([a, b], null);
                if (result is double d)
                {
                    return d > 0 ? 1 : d < 0 ? -1 : 0;
                }

                return 0;
            });
        }
        else
        {
            items.Sort((a, b) =>
            {
                var aStr = JsValueToString(a);
                var bStr = JsValueToString(b);
                return string.CompareOrdinal(aStr, bStr);
            });
        }

        for (var i = 0; i < items.Count; i++)
        {
            jsArray.SetElement(i, items[i]);
        }

        return jsArray;
    }

    private static object? ArrayAt(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not double d)
        {
            return null;
        }

        var index = (int)d;
        if (index < 0)
        {
            index = jsArray.Items.Count + index;
        }

        if (index < 0 || index >= jsArray.Items.Count)
        {
            return null;
        }

        return jsArray.GetElement(index);
    }

    private static object? ArrayFlat(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var depth = args.Count > 0 && args[0] is double d ? (int)d : 1;

        var result = new JsArray(realm);
        FlattenArray(jsArray, result, depth);
        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayFlatMap(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        var result = new JsArray(realm);
        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            var element = jsArray.Items[i];
            var mapped = callback.Invoke([element, (double)i, jsArray], null);

            if (mapped is JsArray mappedArray)
            {
                for (var j = 0; j < mappedArray.Items.Count; j++)
                {
                    result.Push(mappedArray.GetElement(j));
                }
            }
            else
            {
                result.Push(mapped);
            }
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayFindLast(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        for (var i = jsArray.Items.Count - 1; i >= 0; i--)
        {
            var element = jsArray.Items[i];
            var matches = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(matches))
            {
                return element;
            }
        }

        return null;
    }

    private static object? ArrayFindLastIndex(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0 || args[0] is not IJsCallable callback)
        {
            return -1d;
        }

        for (var i = jsArray.Items.Count - 1; i >= 0; i--)
        {
            var element = jsArray.Items[i];
            var matches = callback.Invoke([element, (double)i, jsArray], null);
            if (IsTruthy(matches))
            {
                return (double)i;
            }
        }

        return -1d;
    }

    private static object? ArrayFill(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0)
        {
            return thisValue is JsArray ? thisValue : null;
        }

        var value = args[0];
        var start = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
        var end = args.Count > 2 && args[2] is double d2 ? (int)d2 : jsArray.Items.Count;

        if (start < 0)
        {
            start = Math.Max(0, jsArray.Items.Count + start);
        }

        if (end < 0)
        {
            end = Math.Max(0, jsArray.Items.Count + end);
        }

        start = Math.Max(0, Math.Min(start, jsArray.Items.Count));
        end = Math.Max(start, Math.Min(end, jsArray.Items.Count));

        for (var i = start; i < end; i++)
        {
            jsArray.SetElement(i, value);
        }

        return jsArray;
    }

    private static object? ArrayCopyWithin(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is not JsArray jsArray || args.Count == 0)
        {
            return thisValue is JsArray ? thisValue : null;
        }

        var target = args[0] is double dt ? (int)dt : 0;
        var start = args.Count > 1 && args[1] is double ds ? (int)ds : 0;
        var end = args.Count > 2 && args[2] is double de ? (int)de : jsArray.Items.Count;

        var len = jsArray.Items.Count;

        if (target < 0)
        {
            target = Math.Max(0, len + target);
        }
        else
        {
            target = Math.Min(target, len);
        }

        if (start < 0)
        {
            start = Math.Max(0, len + start);
        }
        else
        {
            start = Math.Min(start, len);
        }

        if (end < 0)
        {
            end = Math.Max(0, len + end);
        }
        else
        {
            end = Math.Min(end, len);
        }

        var count = Math.Min(end - start, len - target);
        if (count <= 0)
        {
            return jsArray;
        }

        var temp = new object?[count];
        for (var i = 0; i < count; i++)
        {
            temp[i] = jsArray.GetElement(start + i);
        }

        for (var i = 0; i < count; i++)
        {
            jsArray.SetElement(target + i, temp[i]);
        }

        return jsArray;
    }

    private static object? ArrayToSorted(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var result = new JsArray(realm);
        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            result.Push(jsArray.GetElement(i));
        }

        AddArrayMethods(result, realm);

        var items = result.Items.ToList();

        if (args.Count > 0 && args[0] is IJsCallable compareFn)
        {
            items.Sort((a, b) =>
            {
                var cmp = compareFn.Invoke([a, b], null);
                if (cmp is double d)
                {
                    return d > 0 ? 1 : d < 0 ? -1 : 0;
                }

                return 0;
            });
        }
        else
        {
            items.Sort((a, b) =>
            {
                var aStr = JsValueToString(a);
                var bStr = JsValueToString(b);
                return string.CompareOrdinal(aStr, bStr);
            });
        }

        for (var i = 0; i < items.Count; i++)
        {
            result.SetElement(i, items[i]);
        }

        return result;
    }

    private static object? ArrayToReversed(object? thisValue, IReadOnlyList<object?> _, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var result = new JsArray(realm);
        for (var i = jsArray.Items.Count - 1; i >= 0; i--)
        {
            result.Push(jsArray.GetElement(i));
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayToSpliced(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var result = new JsArray(realm);
        var len = jsArray.Items.Count;

        if (args.Count == 0)
        {
            for (var i = 0; i < len; i++)
            {
                result.Push(jsArray.GetElement(i));
            }
        }
        else
        {
            var start = args[0] is double ds ? (int)ds : 0;
            var deleteCount = args.Count > 1 && args[1] is double dc ? (int)dc : len - start;

            if (start < 0)
            {
                start = Math.Max(0, len + start);
            }
            else
            {
                start = Math.Min(start, len);
            }

            deleteCount = Math.Max(0, Math.Min(deleteCount, len - start));

            for (var i = 0; i < start; i++)
            {
                result.Push(jsArray.GetElement(i));
            }

            for (var i = 2; i < args.Count; i++)
            {
                result.Push(args[i]);
            }

            for (var i = start + deleteCount; i < len; i++)
            {
                result.Push(jsArray.GetElement(i));
            }
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArrayWith(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray || args.Count < 2 || args[0] is not double d)
        {
            return null;
        }

        var index = (int)d;
        var value = args[1];

        if (index < 0)
        {
            index = jsArray.Items.Count + index;
        }

        if (index < 0 || index >= jsArray.Items.Count)
        {
            return null;
        }

        var result = new JsArray(realm);
        for (var i = 0; i < jsArray.Items.Count; i++)
        {
            result.Push(i == index ? value : jsArray.GetElement(i));
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static object? ArraySlice(object? thisValue, IReadOnlyList<object?> args, RealmState? realm)
    {
        if (thisValue is not JsArray jsArray)
        {
            return null;
        }

        var start = 0;
        var end = jsArray.Items.Count;

        if (args.Count > 0 && args[0] is double startD)
        {
            start = (int)startD;
            if (start < 0)
            {
                start = Math.Max(0, jsArray.Items.Count + start);
            }
        }

        if (args.Count > 1 && args[1] is double endD)
        {
            end = (int)endD;
            if (end < 0)
            {
                end = Math.Max(0, jsArray.Items.Count + end);
            }
        }

        var result = new JsArray(realm);
        for (var i = start; i < Math.Min(end, jsArray.Items.Count); i++)
        {
            result.Push(jsArray.Items[i]);
        }

        AddArrayMethods(result, realm);
        return result;
    }

    private static void FlattenArray(JsArray source, JsArray target, int depth)
    {
        foreach (var item in source.Items)
        {
            if (depth > 0 && item is JsArray nestedArray)
            {
                FlattenArray(nestedArray, target, depth - 1);
            }
            else
            {
                target.Push(item);
            }
        }
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static bool AreStrictlyEqual(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static bool TryGetObject(object candidate, RealmState realm, out IJsObjectLike accessor)
    {
        switch (candidate)
        {
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbols.Undefined):
                accessor = null!;
                return false;
            case IJsObjectLike a:
                accessor = a;
                return true;
            case TypedAstSymbol symbol:
                accessor = CreateSymbolWrapper(symbol, realm: realm);
                return true;
            case bool b:
                accessor = CreateBooleanWrapper(b, realm: realm);
                return true;
            case string s:
                accessor = CreateStringWrapper(s, realm: realm);
                return true;
            case JsBigInt bigInt:
                accessor = CreateBigIntWrapper(bigInt, realm: realm);
                return true;
            case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                accessor = CreateNumberWrapper(JsOps.ToNumber(candidate), realm: realm);
                return true;
            default:
                accessor = null!;
                return false;
        }
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
            while (candidate is JsProxy proxy)
            {
                if (proxy.Handler is null)
                {
                    var error = realm.TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke(["Cannot perform 'isArray' with a revoked Proxy"], null)
                        : new InvalidOperationException("Cannot perform 'isArray' with a revoked Proxy.");
                    throw new ThrowSignal(error);
                }

                candidate = proxy.Target;
            }

            if (candidate is JsArray jsArray)
            {
                if (jsArray.TryGetProperty("__arguments__", out var isArgs) && isArgs is true)
                {
                    return false;
                }

                return true;
            }

            if (candidate is JsObject obj && realm.ArrayPrototype is not null &&
                ReferenceEquals(obj, realm.ArrayPrototype))
            {
                return true;
            }

            return false;
        });

        isArrayFn.DefineProperty("name",
            new PropertyDescriptor { Value = "isArray", Writable = false, Enumerable = false, Configurable = true });

        isArrayFn.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        isArrayFn.IsConstructor = false;

        arrayConstructor.DefineProperty("isArray",
            new PropertyDescriptor { Value = isArrayFn, Writable = true, Enumerable = false, Configurable = true });

        // Array.from(arrayLike)
        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor
                    ? ctor.Invoke(["Array.from requires an array-like or iterable"], null)
                    : new InvalidOperationException("Array.from requires an array-like or iterable.");
                throw new ThrowSignal(error);
            }

            var source = args[0]!;
            var mapfn = args.Count > 1 ? args[1] : null;
            var thisArg = args.Count > 2 ? args[2] : Symbols.Undefined;
            var callingEnv = arrayFrom.CallingJsEnvironment;

            if (mapfn is not null && mapfn is not IJsCallable)
            {
                var error = realm.TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Array.from: when provided, the mapping callback must be callable"], null)
                    : new InvalidOperationException(
                        "Array.from: when provided, the mapping callback must be callable.");
                throw new ThrowSignal(error);
            }

            var constructor = thisValue as IJsCallable;
            var useConstructor = constructor is not null &&
                                 (constructor is not HostFunction hostFn || hostFn.IsConstructor);
            if (!useConstructor)
            {
                constructor = realm.ArrayConstructor;
            }

            var lengthValue = source switch
            {
                string str => (double)str.Length,
                JsArray arr => arr.Length,
                JsObject obj when obj.TryGetProperty("length", out var lenVal) => lenVal,
                _ => 0d
            };
            var len = ToLength(lengthValue);
            var lengthInt = len > int.MaxValue ? int.MaxValue : (int)len;

            IJsObjectLike result;
            if (constructor is HostFunction targetCtor && ReferenceEquals(targetCtor, realm.ArrayConstructor))
            {
                var array = new JsArray(realm);
                if (arrayPrototype is not null)
                {
                    array.SetPrototype(arrayPrototype);
                }

                array.SetProperty("length", (double)lengthInt);
                AddArrayMethods(array, realm, arrayPrototype);
                result = array;
            }
            else
            {
                IJsObjectLike instance;
                var proto = constructor is not null ? ResolveConstructPrototype(constructor, constructor, realm) : null;
                if (constructor is HostFunction hostFunction && ReferenceEquals(hostFunction, realm.ArrayConstructor))
                {
                    instance = new JsArray(realm);
                }
                else
                {
                    instance = new JsObject();
                }

                if (proto is not null)
                {
                    instance.SetPrototype(proto);
                }

                var constructed = constructor?.Invoke([(double)lengthInt], instance);
                result = constructed as IJsObjectLike ?? instance;
            }

            if (TryGetIteratorMethod(source, out var iteratorMethod))
            {
                if (iteratorMethod is not IJsCallable callableIterator)
                {
                    var error = WrapTypeError("Iterator method is not callable");
                    throw new ThrowSignal(error);
                }

                var iteratorObj = callableIterator.Invoke([], source);
                if (iteratorObj is not JsObject iter)
                {
                    var error = WrapTypeError("Iterator method did not return an object");
                    throw new ThrowSignal(error);
                }

                var nextVal = iter.TryGetProperty("next", out var nextProp) ? nextProp : null;
                if (nextVal is not IJsCallable nextFn)
                {
                    var error = WrapTypeError("Iterator.next is not callable");
                    throw new ThrowSignal(error);
                }

                var k = 0;
                while (true)
                {
                    var step = nextFn.Invoke([], iter);
                    if (step is not JsObject stepObj)
                    {
                        break;
                    }

                    var done = stepObj.TryGetProperty("done", out var doneVal) && ToBoolean(doneVal);
                    if (done)
                    {
                        break;
                    }

                    var value = stepObj.TryGetProperty("value", out var val) ? val : Symbols.Undefined;
                    if (mapfn is IJsCallable mapper)
                    {
                        if (mapper is IJsEnvironmentAwareCallable envAware && callingEnv is not null)
                        {
                            envAware.CallingJsEnvironment = callingEnv;
                        }

                        value = mapper.Invoke([value, (double)k], thisArg);
                    }

                    CreateDataPropertyOrThrow(result, k.ToString(CultureInfo.InvariantCulture), value,
                        realm.TypeErrorConstructor);
                    k++;
                }

                result.SetProperty("length", (double)k);
            }
            else
            {
                for (var k = 0; k < lengthInt; k++)
                {
                    var value = GetAt(source, k);
                    if (mapfn is IJsCallable mapper)
                    {
                        if (mapper is IJsEnvironmentAwareCallable envAware && callingEnv is not null)
                        {
                            envAware.CallingJsEnvironment = callingEnv;
                        }

                        value = mapper.Invoke([value, (double)k], thisArg);
                    }

                    CreateDataPropertyOrThrow(result, k.ToString(CultureInfo.InvariantCulture), value,
                        realm.TypeErrorConstructor);
                }

                result.SetProperty("length", (double)lengthInt);
            }

            return result;

            static void CreateDataPropertyOrThrow(IJsObjectLike target, string propertyKey, object? value,
                IJsCallable? typeErrorCtor)
            {
                var existing = target.GetOwnPropertyDescriptor(propertyKey);
                if (existing is null)
                {
                    if (target.IsSealed)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Cannot define property {propertyKey} on a sealed object"], null)
                            : new InvalidOperationException(
                                $"Cannot define property {propertyKey} on a sealed object");
                        throw new ThrowSignal(error);
                    }
                }
                else if (!existing.Configurable)
                {
                    if (existing is { IsAccessorDescriptor: true, Set: null } || !existing.Writable)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Property {propertyKey} is non-writable"], null)
                            : new InvalidOperationException($"Property {propertyKey} is non-writable");
                        throw new ThrowSignal(error);
                    }
                }

                var descriptor = new PropertyDescriptor
                {
                    Value = value, Writable = true, Enumerable = true, Configurable = true
                };

                target.DefineProperty(propertyKey, descriptor);

                var defined = target.GetOwnPropertyDescriptor(propertyKey);
                if (defined?.Writable != true || !defined.Enumerable || !defined.Configurable)
                {
                    var error = typeErrorCtor is not null
                        ? typeErrorCtor.Invoke([$"Failed to create data property {propertyKey}"], null)
                        : new InvalidOperationException($"Failed to create data property {propertyKey}");
                    throw new ThrowSignal(error);
                }
            }

            IJsCallable? ResolveTypeErrorCtor()
            {
                if (callingEnv is not null &&
                    callingEnv.TryGet(Symbol.Intern("TypeError"), out var typeErrorVal) &&
                    typeErrorVal is IJsCallable typeErrorFromEnv)
                {
                    return typeErrorFromEnv;
                }

                return realm.TypeErrorConstructor;
            }

            object WrapTypeError(string message)
            {
                var typeErrorCtor = ResolveTypeErrorCtor();
                if (typeErrorCtor is null)
                {
                    return new InvalidOperationException(message);
                }

                var errorValue = typeErrorCtor.Invoke([message], null);
                if (errorValue is JsObject errorObj)
                {
                    errorObj.SetProperty("constructor", typeErrorCtor);
                }

                return errorValue ?? new InvalidOperationException(message);
            }

            static double ToLength(object? value)
            {
                while (true)
                {
                    switch (value)
                    {
                        case double d when double.IsNaN(d) || d <= 0:
                            return 0;
                        case double d when double.IsPositiveInfinity(d):
                            return double.MaxValue;
                        case double d:
                            return Math.Floor(d);
                        case int i:
                            return i < 0 ? 0 : i;
                        case string s when double.TryParse(s, out var parsed):
                            value = parsed;
                            continue;
                        default:
                            return 0;
                    }
                }
            }

            static object? GetAt(object target, int index)
            {
                var key = index.ToString(CultureInfo.InvariantCulture);
                return target switch
                {
                    JsArray jsArr => index < jsArr.Items.Count ? jsArr.GetElement(index) : Symbols.Undefined,
                    string str => index < str.Length ? str[index].ToString() : Symbols.Undefined,
                    JsObject jsObj when jsObj.TryGetProperty(key, out var value) => value,
                    _ => Symbols.Undefined
                };
            }

            static bool TryGetIteratorMethod(object sourceObj, out object? methodValue)
            {
                var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                methodValue = null;
                if (sourceObj is not IJsPropertyAccessor accessor ||
                    !accessor.TryGetProperty(iteratorKey, out var value) ||
                    ReferenceEquals(value, Symbols.Undefined) ||
                    value is null)
                {
                    return false;
                }

                methodValue = value;
                return true;
            }
        });
        arrayFrom.DefineProperty("name",
            new PropertyDescriptor { Value = "from", Writable = false, Enumerable = false, Configurable = true });
        arrayFrom.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        arrayFrom.IsConstructor = false;
        arrayConstructor.DefineProperty("from",
            new PropertyDescriptor { Value = arrayFrom, Writable = true, Enumerable = false, Configurable = true });

        // Array.of(...elements)
        arrayConstructor.SetHostedProperty("of", ArrayOf);

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
                new PropertyDescriptor { Value = arrayConstructor, Writable = true, Enumerable = false, Configurable = true });
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

        object? ArrayOf(IReadOnlyList<object?> args)
        {
            var arr = new JsArray(args, realm);
            AddArrayMethods(arr, realm);
            return arr;
        }
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
            return 9007199254740991d; // 2^53 - 1
        }

        var truncated = Math.Floor(number);
        return truncated > 9007199254740991d ? 9007199254740991d : truncated;
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
            object? accumulator = hasAccumulator ? args[1] : null;

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
                accumulator = callback.Invoke([accumulator, current, (double)index, typed], Symbols.Undefined);
                index += step;
            }

            return accumulator;
        }
        var lengthValue = accessor.TryGetProperty("length", out var len) ? len : 0d;
        var lengthGeneric = (int)ToLengthOrZero(lengthValue);
        var stepGeneric = fromRight ? -1 : 1;
        var indexGeneric = fromRight ? lengthGeneric - 1 : 0;

        var hasAccumulatorGeneric = args.Count > 1;
        object? accumulatorGeneric = hasAccumulatorGeneric ? args[1] : null;

        if (!hasAccumulatorGeneric)
        {
            var found = false;
            while (indexGeneric >= 0 && indexGeneric < lengthGeneric)
            {
                var key = indexGeneric.ToString(CultureInfo.InvariantCulture);
                if (accessor.TryGetProperty(key, out var current))
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
            var key = indexGeneric.ToString(CultureInfo.InvariantCulture);
            if (accessor.TryGetProperty(key, out var current))
            {
                accumulatorGeneric = callback.Invoke([accumulatorGeneric, current, (double)indexGeneric, accessor],
                    Symbols.Undefined);
            }

            indexGeneric += stepGeneric;
        }

        return accumulatorGeneric;
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
        if (receiver is null || ReferenceEquals(receiver, Symbols.Undefined))
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
                            Value = (double)sInner.Length, Writable = false, Enumerable = false, Configurable = false
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
}
