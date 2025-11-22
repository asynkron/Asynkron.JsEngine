using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

public static partial class StandardLibrary
{
    public static void AddArrayMethods(IJsPropertyAccessor array, JsObject? prototypeOverride = null)
    {
        // Once the shared Array prototype has been initialised, new arrays
        // should inherit from it instead of receiving per-instance copies of
        // every method. This keeps prototype mutations (e.g. in tests) visible
        // to existing arrays.
        var resolvedPrototype = prototypeOverride ?? ArrayPrototype;
        if (resolvedPrototype is not null && array is JsArray jsArray)
        {
            jsArray.SetPrototype(resolvedPrototype);
            return;
        }

        // push - already implemented natively
        array.SetProperty("push", new HostFunction((thisValue, args) =>
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
        }));

        // pop
        array.SetProperty("pop", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            return jsArray.Pop();
        }));

        // map
        array.SetProperty("map", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke([element, (double)i, jsArray], null);
                result.Push(mapped);
            }

            AddArrayMethods(result);
            return result;
        }));

        // filter
        array.SetProperty("filter", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var keep = callback.Invoke([element, (double)i, jsArray], null);
                if (IsTruthy(keep))
                {
                    result.Push(element);
                }
            }

            AddArrayMethods(result);
            return result;
        }));

        // reduce
        array.SetProperty("reduce", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            if (jsArray.Items.Count == 0)
            {
                return args.Count > 1 ? args[1] : null;
            }

            var startIndex = 0;
            object? accumulator;

            if (args.Count > 1)
            {
                accumulator = args[1];
            }
            else
            {
                accumulator = jsArray.Items[0];
                startIndex = 1;
            }

            for (var i = startIndex; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                accumulator = callback.Invoke([accumulator, element, (double)i, jsArray], null);
            }

            return accumulator;
        }));

        // forEach
        array.SetProperty("forEach", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                callback.Invoke([element, (double)i, jsArray], null);
            }

            return null;
        }));

        // find
        array.SetProperty("find", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
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
        }));

        // findIndex
        array.SetProperty("findIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
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
        }));

        // some
        array.SetProperty("some", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return false;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
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
        }));

        // every
        array.SetProperty("every", new HostFunction((thisValue, args) =>
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
        }));

        // join
        array.SetProperty("join", new HostFunction((thisValue, args) =>
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
        }));

        // toString - delegates to join with the default separator
        array.SetProperty("toString", new HostFunction((thisValue, args) =>
        {
            if (thisValue is JsArray jsArray)
            {
                return array.TryGetProperty("join", out var join) && join is IJsCallable joinFn
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
        }));

        // includes
        array.SetProperty("includes", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.includes");

            var searchElement = args.Count > 0 ? args[0] : Symbols.Undefined;
            var fromIndexArg = args.Count > 1 ? args[1] : 0d;
            var length = 0d;
            if (accessor.TryGetProperty("length", out var lenVal)) length = ToLengthOrZero(lenVal);

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
                    .Where(idx => idx >= start && idx < lenLong)
                    .OrderBy(idx => idx);
                foreach (var idx in indices)
                {
                    var val = jsArr.GetElement((int)idx);
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
        }));

        // indexOf
        array.SetProperty("indexOf", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.indexOf");

            if (args.Count == 0)
            {
                return -1d;
            }

            var searchElement = args[0];
            var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
            var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : 0d;

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

            if (accessor is JsArray jsArr && lenLong > 100000)
            {
                var indices = jsArr.GetOwnIndices()
                    .Where(idx => idx >= start && idx < lenLong)
                    .OrderBy(idx => idx);
                foreach (var idx in indices)
                {
                    if (AreStrictlyEqual(jsArr.GetElement((int)idx), searchElement))
                    {
                        return (double)idx;
                    }
                }
            }
            else
            {
                for (var i = start; i < lenLong; i++)
                {
                    var key = i.ToString(CultureInfo.InvariantCulture);
                    if (accessor.TryGetProperty(key, out var value) && AreStrictlyEqual(value, searchElement))
                    {
                        return (double)i;
                    }
                }
            }

            return -1d;
        }));

        // toLocaleString
        array.SetProperty("toLocaleString", new HostFunction((thisValue, args) =>
        {
            var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString");

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
        }));

        // slice
        array.SetProperty("slice", new HostFunction((thisValue, args) => ArraySlice(thisValue, args)));

        // shift
        array.SetProperty("shift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            return jsArray.Shift();
        }));

        // unshift
        array.SetProperty("unshift", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return 0;
            }

            jsArray.Unshift(args.ToArray());
            return jsArray.Items.Count;
        }));

        // splice
        array.SetProperty("splice", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var start = args.Count > 0 && args[0] is double startD ? (int)startD : 0;
            var deleteCount = args.Count > 1 && args[1] is double deleteD ? (int)deleteD : jsArray.Items.Count - start;

            var itemsToInsert = args.Count > 2 ? args.Skip(2).ToArray() : [];

            var deleted = jsArray.Splice(start, deleteCount, itemsToInsert);
            AddArrayMethods(deleted);
            return deleted;
        }));

        // concat
        array.SetProperty("concat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            // Add current array items
            foreach (var item in jsArray.Items)
            {
                result.Push(item);
            }

            // Add items from arguments
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

            AddArrayMethods(result);
            return result;
        }));

        // reverse
        array.SetProperty("reverse", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            jsArray.Reverse();
            return jsArray;
        }));

        // sort
        array.SetProperty("sort", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var items = jsArray.Items.ToList();

            if (args.Count > 0 && args[0] is IJsCallable compareFn)
                // Sort with custom compare function
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
                // Default sort: convert to strings and sort lexicographically
            {
                items.Sort((a, b) =>
                {
                    var aStr = JsValueToString(a);
                    var bStr = JsValueToString(b);
                    return string.CompareOrdinal(aStr, bStr);
                });
            }

            // Replace array items with sorted items
            for (var i = 0; i < items.Count; i++) jsArray.SetElement(i, items[i]);

            return jsArray;
        }));

        // at(index)
        array.SetProperty("at", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            // Handle negative indices
            if (index < 0)
            {
                index = jsArray.Items.Count + index;
            }

            if (index < 0 || index >= jsArray.Items.Count)
            {
                return null;
            }

            return jsArray.GetElement(index);
        }));

        // flat(depth = 1)
        array.SetProperty("flat", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var depth = args.Count > 0 && args[0] is double d ? (int)d : 1;

            var result = new JsArray();
            FlattenArray(jsArray, result, depth);
            AddArrayMethods(result);
            return result;
        }));

        // flatMap(callback)
        array.SetProperty("flatMap", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++)
            {
                var element = jsArray.Items[i];
                var mapped = callback.Invoke([element, (double)i, jsArray], null);

                // Flatten one level
                if (mapped is JsArray mappedArray)
                {
                    for (var j = 0; j < mappedArray.Items.Count; j++)
                        result.Push(mappedArray.GetElement(j));
                }
                else
                {
                    result.Push(mapped);
                }
            }

            AddArrayMethods(result);
            return result;
        }));

        // findLast(callback)
        array.SetProperty("findLast", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
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
        }));

        // findLastIndex(callback)
        array.SetProperty("findLastIndex", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return -1d;
            }

            if (args.Count == 0 || args[0] is not IJsCallable callback)
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
        }));

        // fill(value, start = 0, end = length)
        array.SetProperty("fill", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0)
            {
                return jsArray;
            }

            var value = args[0];
            var start = args.Count > 1 && args[1] is double d1 ? (int)d1 : 0;
            var end = args.Count > 2 && args[2] is double d2 ? (int)d2 : jsArray.Items.Count;

            // Handle negative indices
            if (start < 0)
            {
                start = Math.Max(0, jsArray.Items.Count + start);
            }

            if (end < 0)
            {
                end = Math.Max(0, jsArray.Items.Count + end);
            }

            // Clamp to array bounds
            start = Math.Max(0, Math.Min(start, jsArray.Items.Count));
            end = Math.Max(start, Math.Min(end, jsArray.Items.Count));

            for (var i = start; i < end; i++) jsArray.SetElement(i, value);

            return jsArray;
        }));

        // copyWithin(target, start = 0, end = length)
        array.SetProperty("copyWithin", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count == 0)
            {
                return jsArray;
            }

            var target = args[0] is double dt ? (int)dt : 0;
            var start = args.Count > 1 && args[1] is double ds ? (int)ds : 0;
            var end = args.Count > 2 && args[2] is double de ? (int)de : jsArray.Items.Count;

            var len = jsArray.Items.Count;

            // Handle negative indices
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

            // Copy to temporary array to handle overlapping ranges
            var temp = new object?[count];
            for (var i = 0; i < count; i++) temp[i] = jsArray.GetElement(start + i);

            for (var i = 0; i < count; i++) jsArray.SetElement(target + i, temp[i]);

            return jsArray;
        }));

        // toSorted(compareFn) - non-mutating sort
        array.SetProperty("toSorted", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++) result.Push(jsArray.GetElement(i));
            AddArrayMethods(result);

            var items = result.Items.ToList();

            if (args.Count > 0 && args[0] is IJsCallable compareFn)
                // Sort with custom compare function
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
                // Default sort: convert to strings and sort lexicographically
            {
                items.Sort((a, b) =>
                {
                    var aStr = JsValueToString(a);
                    var bStr = JsValueToString(b);
                    return string.CompareOrdinal(aStr, bStr);
                });
            }

            for (var i = 0; i < items.Count; i++) result.SetElement(i, items[i]);

            return result;
        }));

        // toReversed() - non-mutating reverse
        array.SetProperty("toReversed", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = jsArray.Items.Count - 1; i >= 0; i--) result.Push(jsArray.GetElement(i));
            AddArrayMethods(result);
            return result;
        }));

        // toSpliced(start, deleteCount, ...items) - non-mutating splice
        array.SetProperty("toSpliced", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            var result = new JsArray();
            var len = jsArray.Items.Count;

            if (args.Count == 0)
            {
                // No arguments, return copy
                for (var i = 0; i < len; i++) result.Push(jsArray.GetElement(i));
            }
            else
            {
                var start = args[0] is double ds ? (int)ds : 0;
                var deleteCount = args.Count > 1 && args[1] is double dc ? (int)dc : len - start;

                // Handle negative start
                if (start < 0)
                {
                    start = Math.Max(0, len + start);
                }
                else
                {
                    start = Math.Min(start, len);
                }

                // Clamp deleteCount
                deleteCount = Math.Max(0, Math.Min(deleteCount, len - start));

                // Copy elements before start
                for (var i = 0; i < start; i++) result.Push(jsArray.GetElement(i));

                // Insert new items
                for (var i = 2; i < args.Count; i++) result.Push(args[i]);

                // Copy elements after deleted section
                for (var i = start + deleteCount; i < len; i++) result.Push(jsArray.GetElement(i));
            }

            AddArrayMethods(result);
            return result;
        }));

        // with(index, value) - non-mutating element replacement
        array.SetProperty("with", new HostFunction((thisValue, args) =>
        {
            if (thisValue is not JsArray jsArray)
            {
                return null;
            }

            if (args.Count < 2)
            {
                return null;
            }

            if (args[0] is not double d)
            {
                return null;
            }

            var index = (int)d;
            var value = args[1];

            // Handle negative indices
            if (index < 0)
            {
                index = jsArray.Items.Count + index;
            }

            // Index out of bounds throws RangeError in JavaScript
            if (index < 0 || index >= jsArray.Items.Count)
            {
                return null;
            }

            var result = new JsArray();
            for (var i = 0; i < jsArray.Items.Count; i++) result.Push(i == index ? value : jsArray.GetElement(i));
            AddArrayMethods(result);
            return result;
        }));

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

        static object CreateArrayIterator(object? thisValue, IJsPropertyAccessor accessor, Func<uint, object?> projector)
        {
            var iterator = new JsObject();
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";

            uint index = 0;
            var exhausted = false;

            iterator.SetProperty("next", new HostFunction((_, __) =>
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
            }));

            iterator.SetProperty(iteratorKey, new HostFunction((_, __) => iterator));
            return iterator;
        }

        HostFunction DefineArrayIteratorFunction(string name, Func<IJsPropertyAccessor, object?, Func<uint, object?>> projectorFactory)
        {
            var fn = new HostFunction((thisValue, args) =>
            {
                if (thisValue is null || ReferenceEquals(thisValue, Symbols.Undefined))
                {
                    var error = TypeErrorConstructor is IJsCallable ctor
                        ? ctor.Invoke([$"{name} called on null or undefined"], null)
                        : new InvalidOperationException($"{name} called on null or undefined");
                    throw new ThrowSignal(error);
                }

                if (thisValue is not IJsPropertyAccessor accessor)
                {
                    var error = TypeErrorConstructor is IJsCallable ctor2
                        ? ctor2.Invoke([$"{name} called on non-object"], null)
                        : new InvalidOperationException($"{name} called on non-object");
                    throw new ThrowSignal(error);
                }

                var projector = projectorFactory(accessor, thisValue);
                return CreateArrayIterator(thisValue, accessor, projector);
            })
            {
                IsConstructor = false
            };

            fn.DefineProperty("name", new PropertyDescriptor
            {
                Value = name,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

            fn.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

            var descriptor = new PropertyDescriptor
            {
                Value = fn,
                Writable = true,
                Enumerable = false,
                Configurable = true
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

        // entries() - returns an iterator of [index, value] pairs
        DefineArrayIteratorFunction("entries", (accessor, _) => idx =>
        {
            var pair = new JsArray();
            pair.Push((double)idx);
            if (accessor.TryGetProperty(idx.ToString(CultureInfo.InvariantCulture), out var value))
            {
                pair.Push(value);
            }
            else
            {
                pair.Push(Symbols.Undefined);
            }

            AddArrayMethods(pair);
            return pair;
        });

        // keys() - returns an iterator of indices
        DefineArrayIteratorFunction("keys", (_, __) => idx => (double)idx);

        // values() - returns an iterator of values
        var valuesFn = DefineArrayIteratorFunction("values", (accessor, thisValue) => idx =>
        {
            var key = idx.ToString(CultureInfo.InvariantCulture);
            return accessor.TryGetProperty(key, out var value) ? value : Symbols.Undefined;
        });
    }

    private static object? ArraySlice(object? thisValue, IReadOnlyList<object?> args)
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

        var result = new JsArray();
        for (var i = start; i < Math.Min(end, jsArray.Items.Count); i++)
        {
            result.Push(jsArray.Items[i]);
        }

        AddArrayMethods(result);
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

    private static bool TryGetObject(object candidate, out IJsObjectLike accessor)
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
            case bool b:
                accessor = CreateBooleanWrapper(b);
                return true;
            case string s:
                accessor = CreateStringWrapper(s);
                return true;
            case JsBigInt bigInt:
                accessor = CreateBigIntWrapper(bigInt);
                return true;
            case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                accessor = CreateNumberWrapper(JsOps.ToNumber(candidate));
                return true;
            default:
                accessor = null!;
                return false;
        }
    }

    public static HostFunction CreateArrayConstructor(Runtime.RealmState realm)
    {
        JsObject? arrayPrototype = null;

        // Array constructor
        var arrayConstructor = new HostFunction((thisValue, args) =>
        {
            // Use provided receiver when available so Reflect.construct can
            // control allocation and prototype.
            var instance = thisValue as JsArray ?? new JsArray();

            // Honor an explicit prototype on the receiver; otherwise fall back
            // to the constructor's prototype if available.
            if (thisValue is JsObject thisObj && thisObj.Prototype is JsObject providedProto)
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
                AddArrayMethods(instance, instance.Prototype);
                return instance;
            }

            foreach (var value in args)
            {
                instance.Push(value);
            }

            AddArrayMethods(instance, instance.Prototype);
            return instance;
        });

        arrayConstructor.RealmState = realm;
        realm.ArrayConstructor ??= arrayConstructor;
        ArrayConstructor ??= arrayConstructor;

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
                    var error = TypeErrorConstructor is IJsCallable ctor
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

            if (candidate is JsObject obj && ArrayPrototype is not null &&
                ReferenceEquals(obj, ArrayPrototype))
            {
                return true;
            }

            return false;
        });

        isArrayFn.DefineProperty("name", new PropertyDescriptor
        {
            Value = "isArray",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        isArrayFn.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        isArrayFn.IsConstructor = false;

        arrayConstructor.DefineProperty("isArray", new PropertyDescriptor
        {
            Value = isArrayFn,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        // Array.from(arrayLike)
        HostFunction arrayFrom = null!;
        arrayFrom = new HostFunction((thisValue, args) =>
        {
            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbols.Undefined))
            {
                var error = TypeErrorConstructor is IJsCallable ctor
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
                var error = TypeErrorConstructor is IJsCallable ctor2
                    ? ctor2.Invoke(["Array.from: when provided, the mapping callback must be callable"], null)
                    : new InvalidOperationException("Array.from: when provided, the mapping callback must be callable.");
                throw new ThrowSignal(error);
            }

            static double ToLength(object? value)
            {
                while (true)
                {
                    if (value is double d)
                    {
                        if (double.IsNaN(d) || d <= 0)
                        {
                            return 0;
                        }

                        if (double.IsPositiveInfinity(d))
                        {
                            return double.MaxValue;
                        }

                        return Math.Floor(d);
                    }

                    if (value is int i)
                    {
                        return i < 0 ? 0 : i;
                    }

                    if (value is string s && double.TryParse(s, out var parsed))
                    {
                        value = parsed;
                        continue;
                    }

                    return 0;
                }
            }

            static object? GetAt(object target, int index)
            {
                var key = index.ToString(CultureInfo.InvariantCulture);
                if (target is JsArray jsArr)
                {
                    return index < jsArr.Items.Count ? jsArr.GetElement(index) : Symbols.Undefined;
                }

                if (target is string str)
                {
                    return index < str.Length ? str[index].ToString() : Symbols.Undefined;
                }

                if (target is JsObject jsObj && jsObj.TryGetProperty(key, out var value))
                {
                    return value;
                }

                return Symbols.Undefined;
            }

            static bool TryGetIteratorMethod(object sourceObj, out object? methodValue)
            {
                var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
                var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
                methodValue = null;
                if (sourceObj is IJsPropertyAccessor accessor &&
                    accessor.TryGetProperty(iteratorKey, out var value) &&
                    !ReferenceEquals(value, Symbols.Undefined) &&
                    value is not null)
                {
                    methodValue = value;
                    return true;
                }

                return false;
            }

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
                    if (existing.IsAccessorDescriptor && existing.Set is null)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Property {propertyKey} is non-writable"], null)
                            : new InvalidOperationException($"Property {propertyKey} is non-writable");
                        throw new ThrowSignal(error);
                    }

                    if (!existing.Writable)
                    {
                        var error = typeErrorCtor is not null
                            ? typeErrorCtor.Invoke([$"Property {propertyKey} is non-writable"], null)
                            : new InvalidOperationException($"Property {propertyKey} is non-writable");
                        throw new ThrowSignal(error);
                    }
                }

                var descriptor = new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
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

            var constructor = thisValue as IJsCallable;
            var useConstructor = constructor is not null &&
                                 (constructor is not HostFunction hostFn || hostFn.IsConstructor);
            if (!useConstructor)
            {
                constructor = ArrayConstructor;
            }

            var lengthValue = source switch
            {
                string str => (double)str.Length,
                JsArray arr => (double)arr.Length,
                JsObject obj when obj.TryGetProperty("length", out var lenVal) => lenVal,
                _ => 0d
            };
            var len = ToLength(lengthValue);
            var lengthInt = len > int.MaxValue ? int.MaxValue : (int)len;

            IJsObjectLike result;
            if (constructor is HostFunction targetCtor && ReferenceEquals(targetCtor, ArrayConstructor))
            {
                var array = new JsArray();
                if (arrayPrototype is not null)
                {
                    array.SetPrototype(arrayPrototype);
                }

                array.SetProperty("length", (double)lengthInt);
                AddArrayMethods(array, arrayPrototype);
                result = array;
            }
            else
            {
                IJsObjectLike instance;
                var proto = constructor is not null ? ResolveConstructPrototype(constructor, constructor) : null;
                if (constructor is HostFunction hostFunction && ReferenceEquals(hostFunction, ArrayConstructor))
                {
                    instance = new JsArray();
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
                result = constructed is IJsObjectLike objectLike ? objectLike : instance;
            }

            IJsCallable? ResolveTypeErrorCtor()
            {
                if (callingEnv is not null &&
                    callingEnv.TryGet(Symbol.Intern("TypeError"), out var typeErrorVal) &&
                    typeErrorVal is IJsCallable typeErrorFromEnv)
                {
                    return typeErrorFromEnv;
                }

                return TypeErrorConstructor;
            }

            object WrapTypeError(string message)
            {
                var typeErrorCtor = ResolveTypeErrorCtor();
                if (typeErrorCtor is not null)
                {
                    var errorValue = typeErrorCtor.Invoke([message], null);
                    if (errorValue is JsObject errorObj)
                    {
                        errorObj.SetProperty("constructor", typeErrorCtor);
                    }

                    return errorValue ?? new InvalidOperationException(message);
                }

                return new InvalidOperationException(message);
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
                        TypeErrorConstructor);
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
                        TypeErrorConstructor);
                }

                result.SetProperty("length", (double)lengthInt);
            }
            return result;
        });
        arrayFrom.DefineProperty("name", new PropertyDescriptor
        {
            Value = "from",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        arrayFrom.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });
        arrayFrom.IsConstructor = false;
        arrayConstructor.DefineProperty("from", new PropertyDescriptor
        {
            Value = arrayFrom,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });

        // Array.of(...elements)
        arrayConstructor.SetProperty("of", new HostFunction(args =>
        {
            var arr = new JsArray(args);
            AddArrayMethods(arr);
            return arr;
        }));

        // Expose core Array prototype methods (such as slice) on
        // Array.prototype so patterns like `Array.prototype.slice.call`
        // work against array-like values (e.g. `arguments`).
        if (arrayConstructor.TryGetProperty("prototype", out var prototypeValue) &&
            prototypeValue is JsObject prototypeObject)
        {
            prototypeObject.SetProperty("slice", new HostFunction((thisValue, args) => ArraySlice(thisValue, args)));
        }

        if (arrayConstructor.TryGetProperty("prototype", out var protoValue) && protoValue is JsObject arrayProtoObj)
        {
            if (realm.ObjectPrototype is not null && arrayProtoObj.Prototype is null)
            {
                arrayProtoObj.SetPrototype(realm.ObjectPrototype);
            }
            arrayPrototype = arrayProtoObj;
            realm.ArrayPrototype ??= arrayProtoObj;
            ArrayPrototype ??= arrayProtoObj;
            AddArrayMethods(arrayProtoObj);
            arrayProtoObj.DefineProperty("length", new PropertyDescriptor
            {
                Value = 0d,
                Writable = true,
                Enumerable = false,
                Configurable = false
            });
            var iteratorSymbol = TypedAstSymbol.For("Symbol.iterator");
            var iteratorKey = $"@@symbol:{iteratorSymbol.GetHashCode()}";
            if (arrayProtoObj.TryGetProperty("values", out var valuesFn))
            {
                arrayProtoObj.DefineProperty(iteratorKey, new PropertyDescriptor
                {
                    Value = valuesFn,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
            }
        }

        arrayConstructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 1d,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        arrayConstructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "Array",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        return arrayConstructor;
    }

    private static double ToLengthOrZero(object? value)
    {
        var number = JsOps.ToNumber(value);
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

    private static double ToIntegerOrInfinity(object? value)
    {
        var number = JsOps.ToNumberWithContext(value);
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

    private static bool SameValueZero(object? x, object? y)
    {
        if (x is double.NaN && y is double.NaN)
        {
            return true;
        }

        return JsOps.StrictEquals(x, y);
    }

    private static IJsPropertyAccessor EnsureArrayLikeReceiver(object? receiver, string methodName)
    {
        if (receiver is null || ReferenceEquals(receiver, Symbols.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined");
        }

        if (receiver is IJsPropertyAccessor accessor)
        {
            if (accessor is JsObject jsObj && jsObj.TryGetProperty("__value__", out var inner) && inner is string sInner)
            {
                if (!jsObj.TryGetProperty("length", out _))
                {
                    jsObj.DefineProperty("length", new PropertyDescriptor
                    {
                        Value = (double)sInner.Length,
                        Writable = false,
                        Enumerable = false,
                        Configurable = false
                    });

                    for (var i = 0; i < sInner.Length; i++)
                    {
                        jsObj.SetProperty(i.ToString(CultureInfo.InvariantCulture), sInner[i].ToString());
                    }
                }

                return jsObj;
            }

            return accessor;
        }

        // Box primitives to objects per ToObject.
        if (receiver is string s)
        {
            var obj = new JsObject();
            obj.SetPrototype(StringPrototype);
            obj.SetProperty("__value__", s);
            obj.DefineProperty("length", new PropertyDescriptor
            {
                Value = (double)s.Length,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

            for (var i = 0; i < s.Length; i++)
            {
                obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), s[i].ToString());
            }

            return obj;
        }

        if (receiver is double or int or uint or long or ulong or short or ushort or byte or sbyte or decimal or float)
        {
            var obj = new JsObject();
            obj.SetPrototype(NumberPrototype);
            obj.SetProperty("__value__", receiver);
            return obj;
        }

        if (receiver is bool b)
        {
            var obj = new JsObject();
            obj.SetPrototype(BooleanPrototype);
            obj.SetProperty("__value__", b);
            return obj;
        }

        // Symbols and BigInts should throw TypeError for array methods
        if (receiver is TypedAstSymbol || receiver is JsBigInt)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver");
        }

        throw ThrowTypeError($"{methodName} called on non-object");
    }

}
