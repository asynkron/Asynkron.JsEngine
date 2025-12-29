#region

using System.Text;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.JsArrayConstants;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    [JsHostMethod("join", Length = 1d)]
    public JsValue Join(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.join", Realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var evalContext = Realm?.CreateContext();
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

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

            var element = GetElementOrUndefinedJsValue(accessor, ToIndexString(k));
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
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);

        // Per spec: If len is 0, return false BEFORE calling ToIntegerOrInfinity on fromIndex
        if (length == 0)
        {
            return new JsValue(false);
        }

        var fromIndexArg = args.Count > 1 ? args[1] : JsValue.FromDouble(0d);
        var fromIndex = ToIntegerOrInfinity(fromIndexArg, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
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

        var searchElement = args.Count > 0 ? args[0] : JsValue.Undefined;
        var evalContext = Realm?.CreateContext();
        var length = accessor.TryGetProperty("length", out var lenVal) ? ToLengthOrZero(lenVal, evalContext) : 0d;

        // Per spec: If len is 0, return -1 BEFORE calling ToIntegerOrInfinity on fromIndex
        if (length == 0)
        {
            return new JsValue(-1d);
        }

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
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
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
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], evalContext) : 0;
        // Per spec: if end is undefined, use length
        var endIndex = args.Count > 1 && !args[1].IsUndefined ? ToIntegerOrInfinity(args[1], evalContext) : length;

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
        var evalContext = Realm?.CreateContext();
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);

        var indexArg = args.GetArgument(0);
        var relativeIndex = ToIntegerOrInfinity(indexArg, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
        if (double.IsPositiveInfinity(relativeIndex) || double.IsNegativeInfinity(relativeIndex))
        {
            return JsValue.Undefined;
        }

        var index = relativeIndex < 0 ? length + (long)relativeIndex : (long)relativeIndex;

        if (index < 0 || index >= length)
        {
            return JsValue.Undefined;
        }

        return GetElementOrUndefinedJsValue(target, ToIndexString(index));
    }

    [JsHostMethod("flat", Length = 0d)]
    public JsValue Flat(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.flat", Realm);
        var evalContext = Realm?.CreateContext();
        var depthNum = args.Count > 0 ? ToIntegerOrInfinity(args[0], evalContext) : 1;
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

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var sourceLength = (long)ToLengthOrZero(lengthValue, evalContext);

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
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var sourceLength = (long)ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);
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
        var evalContext = Realm?.CreateContext();
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

        var value = args.GetArgument(0);
        var startIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0;
        // Per spec: if end is undefined, use length
        var endIndex = args.Count > 2 && !args[2].IsUndefined ? ToIntegerOrInfinity(args[2], evalContext) : length;

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
        var evalContext = Realm?.CreateContext();
        var lengthValue = target.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

        var toIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], evalContext) : 0;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], evalContext) : 0;
        // Per spec: if end is undefined, use length
        var endIndex = args.Count > 2 && !args[2].IsUndefined ? ToIntegerOrInfinity(args[2], evalContext) : length;

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
        const string MethodName = "Array.prototype.toSorted";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);

        if (args.Count > 0 && !args[0].IsUndefined && !args[0].TryGetObject<IJsCallable>(out _))
        {
            throw ThrowTypeError($"{MethodName} comparefn must be callable", realm: Realm);
        }

        IJsCallable? compareFn = null;
        if (args.Count > 0 && args[0].TryGetObject<IJsCallable>(out var callable))
        {
            compareFn = callable;
        }

        // Snapshot elements before sorting; comparefn must not observe changes during enumeration.
        var values = new List<(JsValue Value, long OriginalIndex)>((int)Math.Min(length, int.MaxValue));
        for (long k = 0; k < length; k++)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                values.Add((value, k));
            }
        }

        // Keep the sort stable by falling back to the original index on ties.
        values.Sort(Comparer);

        var result = CreateCopyArray(length, Realm, MethodName);
        long targetIndex = 0;
        foreach (var pair in values)
        {
            result.SetProperty(ToIndexString(targetIndex++), pair.Value);
        }

        SetArrayLikeLength(result, length);
        return JsValue.FromObjectUnsafe(result);

        int Comparer((JsValue Value, long OriginalIndex) a, (JsValue Value, long OriginalIndex) b)
        {
            if (compareFn is not null)
            {
                var cmp = compareFn.Invoke([a.Value, b.Value], JsValue.Undefined);
                var context = Realm?.CreateContext();
                var numeric = JsOps.ToNumber(cmp, context);
                if (context?.IsThrow == true)
                {
                    throw new ThrowSignal(context.FlowValue);
                }

                if (double.IsNaN(numeric))
                {
                    return a.OriginalIndex.CompareTo(b.OriginalIndex);
                }

                var result = numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
                return result != 0 ? result : a.OriginalIndex.CompareTo(b.OriginalIndex);
            }

            var aStr = JsValueToString(a.Value);
            var bStr = JsValueToString(b.Value);
            var ordinal = string.CompareOrdinal(aStr, bStr);
            return ordinal != 0 ? ordinal : a.OriginalIndex.CompareTo(b.OriginalIndex);
        }
    }

    [JsHostMethod("toReversed", Length = 0d)]
    public JsValue ToReversed(JsValue thisValue)
    {
        const string MethodName = "Array.prototype.toReversed";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true) throw new ThrowSignal(evalContext.FlowValue);

        var result = CreateCopyArray(length, Realm, MethodName);
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
        const string MethodName = "Array.prototype.toSpliced";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

        var startIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], evalContext) : 0;
        var actualStart = ClampRelativeIndex(startIndex, length);

        var deleteCountIsUndefined = args.Count <= 1 || args[1].IsUndefined;
        long actualDeleteCount;
        if (deleteCountIsUndefined)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCountArg = ToIntegerOrInfinity(args[1], evalContext);
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
            throw ThrowRangeError($"{MethodName} result exceeds 2^32 - 1 elements", realm: Realm);
        }

        var result = CreateCopyArray(newLength, Realm, MethodName);
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
        const string MethodName = "Array.prototype.with";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);

        if (args.Count == 0)
        {
            throw ThrowTypeError($"{MethodName} requires an index argument", realm: Realm);
        }

        var indexNumber = ToIntegerOrInfinity(args[0], evalContext);
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
            throw ThrowRangeError($"{MethodName} index out of range", realm: Realm);
        }

        var value = args.Count > 1 ? args[1] : JsValue.Undefined;
        var result = CreateCopyArray(length, Realm, MethodName);

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
