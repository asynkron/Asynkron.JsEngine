#region

using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("join", Length = 1d)]
    public JsValue Join(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.join", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        if (length == 0)
        {
            return new JsValue(string.Empty);
        }

        var separator = args.Count == 0 || args[0].IsUndefined
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

        return new JsValue(builder.ToString());
    }

    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue)
    {
        var target = ToObjectPropertyAccessor(thisValue, "Array.prototype.toString", Realm);

        if (JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(target), "join", out var joinValueJs) &&
            joinValueJs.TryGetObject<IJsCallable>(out var joinCallable))
        {
            return joinCallable.Invoke([], JsValue.FromObjectUnsafe(target));
        }

        return JsValue.FromObjectUnsafe(InvokeDefaultObjectToString(target, Realm));
    }

    [JsHostMethod("includes", Length = 1d)]
    public JsValue Includes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.includes", Realm);

        var searchElement = args.Count > 0 ? args[0] : JsValue.Undefined;
        var fromIndexArg = args.Count > 1 ? args[1] : 0d;
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;

        var fromIndex = ToIntegerOrInfinity(fromIndexArg);
        if (double.IsPositiveInfinity(fromIndex))
        {
            return new JsValue(false);
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
        var lenLong = (long)Math.Min(length, MaxArrayLength);

        if (accessor is JsArray jsArr && lenLong > 100000)
        {
            // Collect and filter indices without closure allocation
            var filteredIndices = new List<uint>();
            foreach (var idx in jsArr.GetOwnIndices())
            {
                if (idx >= start && idx < lenLong)
                {
                    filteredIndices.Add(idx);
                }
            }

            filteredIndices.Sort();
            foreach (var idx in filteredIndices)
            {
                var val = jsArr.GetElement(idx);
                if (SameValueZero(val, searchElement))
                {
                    return new JsValue(true);
                }
            }
        }
        else
        {
            for (var i = start; i < lenLong; i++)
            {
                if (TryGetExistingElement(accessor, i, out var value) && SameValueZero(value, searchElement))
                {
                    return new JsValue(true);
                }
            }
        }

        return new JsValue(false);
    }

    [JsHostMethod("indexOf", Length = 1d)]
    public JsValue IndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.indexOf", Realm);

        if (args.Count == 0)
        {
            return new JsValue(-1d);
        }

        var searchElement = args[0];
        var evalContext = Realm?.CreateContext();
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0d;

        if (double.IsPositiveInfinity(fromIndex))
        {
            return new JsValue(-1d);
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
        var lenLong = (long)Math.Min(length, MaxArrayLength);

        for (var i = start; i < lenLong; i++)
        {
            if (TryGetExistingElement(accessor, i, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return new JsValue((double)i);
            }
        }

        return new JsValue(-1d);
    }

    [JsHostMethod("lastIndexOf", Length = 1d)]
    public JsValue LastIndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.lastIndexOf", Realm);
        var evalContext = Realm?.CreateContext();
        var searchElement = args.Count > 0 ? args[0] : JsValue.Undefined;
        if (accessor is TypedArrayBase typed)
        {
            // Align Array.prototype.lastIndexOf with TypedArray semantics.
            return TypedArrayBase.LastIndexOfInternal(typed, args);
        }

        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;
        if (length <= 0)
        {
            return new JsValue(-1d);
        }

        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : length - 1;
        var lenLong = (long)Math.Min(length, MaxArrayLength);

        long startIndexGeneric;
        if (double.IsNegativeInfinity(fromIndex))
        {
            return new JsValue(-1d);
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
                return new JsValue(-1d);
            }

            startIndexGeneric = candidate;
        }

        for (var i = startIndexGeneric; i >= 0; i--)
        {
            if (TryGetExistingElement(accessor, i, out var value) && AreStrictlyEqual(value, searchElement))
            {
                return new JsValue((double)i);
            }
        }

        return new JsValue(-1d);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toLocaleString", Realm);

        var locales = args.Count > 0 ? args[0] : JsValue.Undefined;
        var options = args.Count > 1 ? args[1] : JsValue.Undefined;
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal) : 0d;
        var parts = new List<string>((int)length);

        for (var i = 0; i < length; i++)
        {
            if (!TryGetExistingElement(accessor, i, out var elementObj))
            {
                parts.Add(string.Empty);
                continue;
            }

            // elementObj is already a JsValue from TryGetExistingElement
            if (elementObj.IsNullOrUndefined)
            {
                parts.Add(string.Empty);
                continue;
            }

            string part;
            // method is already a JsValue from TryGetProperty
            if (elementObj.TryGetObject<IJsPropertyAccessor>(out var elementAccessor) &&
                elementAccessor.TryGetProperty("toLocaleString", out var method) &&
                method.TryGetObject<IJsCallable>(out var callable))
            {
                var result = callable.Invoke([locales, options], elementObj);
                part = JsOps.ToJsString(result);
            }
            else
            {
                part = JsOps.ToJsString(elementObj);
            }

            parts.Add(part);
        }

        return new JsValue(string.Join(',', parts));
    }

    [JsHostMethod("slice", Length = 2d)]
    public JsValue Slice(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.slice", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var endIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1]) : length;

        var from = ClampRelativeIndex(startIndex, length);
        var to = ClampRelativeIndex(endIndex, length);
        var count = Math.Max(to - from, 0);
        var result = ArraySpeciesCreate(thisValue, count, Realm);
        long targetIndex = 0;

        for (var k = from; k < to; k++)
        {
            CopyArrayElement(accessor, k, result, targetIndex++);
        }

        SetArrayLikeLength(result, targetIndex);
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("at", Length = 1d)]
    public JsValue At(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = EnsureArrayLikeReceiver(thisValue, "Array.prototype.at", Realm);
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var indexArg = args.GetArgument(0);
        var numericContext = Realm?.CreateContext(pushScope: false);
        var relativeIndex = ToIntegerOrInfinity(indexArg, numericContext);
        if (double.IsPositiveInfinity(relativeIndex) || double.IsNegativeInfinity(relativeIndex))
        {
            return JsValue.Undefined;
        }

        var index = relativeIndex < 0 ? length + (long)relativeIndex : (long)relativeIndex;

        if (index < 0 || index >= length)
        {
            return JsValue.Undefined;
        }

        // Handle case where element is already a boxed JsValue
        var element = GetElementOrUndefined(target, ToIndexString(index));
        return element is JsValue elemJs ? elemJs : JsValue.FromObjectUnsafe(element);
    }

    [JsHostMethod("flat", Length = 0d)]
    public JsValue Flat(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flat", Realm);
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

        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        var newLength = FlattenIntoArray(result, accessor, sourceLength, 0, depth, null, JsValue.Null, Realm,
            "Array.prototype.flat");
        SetArrayLikeLength(result, newLength);
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("flatMap", Length = 1d)]
    public JsValue FlatMap(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flatMap", Realm);
        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("Array.prototype.flatMap expects a callable mapper", realm: Realm);
        }

        var thisArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var sourceLength = (long)ToLengthOrZero(lengthValue);
        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        var newLength = FlattenIntoArray(result, accessor, sourceLength, 0, 1, callback, thisArg, Realm,
            "Array.prototype.flatMap");
        SetArrayLikeLength(result, newLength);
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("fill", Length = 1d)]
    public JsValue Fill(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = EnsureArrayLikeReceiver(thisValue, "Array.prototype.fill", Realm);
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

        return JsValue.FromObjectUnsafe(target);
    }

    [JsHostMethod("copyWithin", Length = 2d)]
    public JsValue CopyWithin(JsValue thisValue, IReadOnlyList<JsValue> args)
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
            return JsValue.FromObjectUnsafe(target);
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

        return JsValue.FromObjectUnsafe(target);
    }

    [JsHostMethod("toSorted", Length = 1d)]
    public JsValue ToSorted(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toSorted", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var values = new List<JsValue>((int)Math.Min(length, int.MaxValue));
        for (long k = 0; k < length; k++)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                values.Add(value);
            }
        }

        if (args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var compareFn))
        {
            values.Sort((a, b) =>
            {
                var cmp = compareFn.Invoke([a, b], JsValue.Null);
                if (!cmp.TryGetDouble(out var d))
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

        var result = ArraySpeciesCreate(thisValue, length, Realm);

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
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("toReversed", Length = 0d)]
    public JsValue ToReversed(JsValue thisValue)
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
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("toSpliced", Length = 2d)]
    public JsValue ToSpliced(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.toSpliced", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0]) : 0;
        var actualStart = ClampRelativeIndex(startIndex, length);

        var deleteCountIsUndefined = args.Count <= 1 || args[1].IsUndefined;
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
        if (newLength > MaxConcreteArrayLength)
        {
            throw ThrowTypeError("Array.prototype.toSpliced cannot exceed 2^32 - 1 elements", realm: Realm);
        }

        var result = ArraySpeciesCreate(thisValue, newLength, Realm);
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
        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("with", Length = 2d)]
    public JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
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

        var value = args.Count > 1 ? args[1] : JsValue.Undefined;
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
        return JsValue.FromObjectUnsafe(result);
    }
}
