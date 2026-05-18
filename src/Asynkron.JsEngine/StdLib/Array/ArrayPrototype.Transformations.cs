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

        // Per spec step 3: ToString(separator) MUST happen BEFORE checking if len = 0
        // This ensures separator conversion errors are thrown even for empty arrays
        var separator = args.Count == 0 || args[0].IsUndefined
            ? ","
            : args[0].ToJsString();

        if (length == 0)
        {
            return new JsValue(string.Empty);
        }

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
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        // Per spec: If len is 0, return false BEFORE calling ToIntegerOrInfinity on fromIndex
        if (length == 0)
        {
            return new JsValue(false);
        }

        var fromIndexArg = args.Count > 1 ? args[1] : JsValue.FromDouble(0d);
        var fromIndex = ToIntegerOrInfinity(fromIndexArg, evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

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

        // Spec: Let elementK be Get(O, ToString(k)) for every k (holes become undefined, prototype chain applies).
        for (var i = start; i < lenLong; i++)
        {
            var key = ToIndexString(i);
            _ = JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(accessor), key, out var value, evalContext);
            if (evalContext?.IsThrow == true)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            // Even if the property is missing (got == false), value will be undefined which matches spec behaviour.
            if (SameValueZero(value, searchElement))
            {
                return JsValue.True;
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
                return JsValue.FromDouble((double)i);
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
                return JsValue.FromDouble(i);
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
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

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
            // Get the appropriate accessor - either the object itself or its prototype for primitives
            IJsPropertyAccessor? elementAccessor;
            if (elementObj.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
            {
                elementAccessor = objAccessor;
            }
            else
            {
                // For primitives, get the wrapper prototype
                elementAccessor = GetPrimitivePrototype(elementObj, Realm);
            }

            // Look up toLocaleString using the receiver (important for getters in strict mode)
            if (elementAccessor is not null &&
                elementAccessor.TryGetProperty("toLocaleString", elementObj, out var method) &&
                method.TryGetObject<IJsCallable>(out var callable))
            {
                // Call with the original primitive/object as this
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
            CopyArrayElement(accessor, k, result, targetIndex++, Realm, "Array.prototype.slice");
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
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var indexArg = args.GetArgument(0);
        var relativeIndex = ToIntegerOrInfinity(indexArg, evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

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
        // ES spec doesn't set length on result - JsArray auto-updates length via [[DefineOwnProperty]],
        // and custom species results should not have length property added
        FlattenIntoArray(result, accessor, sourceLength, 0, depth, null, JsValue.Null, Realm,
            "Array.prototype.flat");
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
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        // ES spec doesn't set length on result - JsArray auto-updates length via [[DefineOwnProperty]],
        // and custom species results should not have length property added
        FlattenIntoArray(result, accessor, sourceLength, 0, 1, callback, thisArg, Realm,
            "Array.prototype.flatMap");
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

        if (target is TypedArrayBase typedArray && typedArray.IsDetachedOrOutOfBounds())
        {
            return JsValue.FromObjectUnsafe(target);
        }

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

        // Per spec: Step 2 - check compareFn is callable BEFORE reading length (Step 3)
        if (args.Count > 0 && !args[0].IsUndefined && !args[0].TryGetObject<IJsCallable>(out _))
        {
            throw ThrowTypeError($"{MethodName} comparefn must be callable", realm: Realm);
        }

        // Per spec step 1: If comparefn is not undefined and IsCallable(comparefn) is false, throw a TypeError.
        // This MUST happen BEFORE getting the length (step 3).
        IJsCallable? compareFn = null;
        if (args.Count > 0 && !args[0].IsUndefined)
        {
            if (!args[0].TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError($"{MethodName} comparefn must be callable", realm: Realm);
            }
            compareFn = callable;
        }

        // Per spec: Step 3 - Get length AFTER checking compareFn
        var evalContext = Realm?.CreateContext();
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : JsValue.FromDouble(0d);
        var length = (long)ToLengthOrZero(lengthValue, evalContext);
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        // Per spec: ArrayCreate throws RangeError if length > 2^32 - 1
        if (length > MaxConcreteArrayLength)
        {
            throw ThrowRangeError($"{MethodName} array length exceeds maximum (2^32 - 1)", realm: Realm);
        }

        // Per spec SortIndexedProperties with skipHoles=true: read all existing elements first.
        // Holes ARE skipped - they will end up at the end of the result array.
        // Use Get(O, k) which traverses the prototype chain but only add to list if property exists.
        var values = new List<(JsValue Value, long OriginalIndex)>((int)Math.Min(length, int.MaxValue));
        for (long k = 0; k < length; k++)
        {
            // Check HasProperty first (which includes prototype chain), then Get if true
            if (HasProperty(accessor, ToIndexString(k)))
            {
                var value = GetElementOrUndefinedJsValue(accessor, ToIndexString(k));
                values.Add((value, k));
            }
        }

        // Keep the sort stable by falling back to the original index on ties.
        // Wrap in try-catch to properly propagate ThrowSignal from compareFn
        try
        {
            values.Sort(Comparer);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is ThrowSignal ts)
        {
            // Re-throw the original ThrowSignal
            throw ts;
        }

        var result = CreateCopyArray(length, Realm, MethodName);

        // Per spec step 8: Repeat while j < len, setting result[j] = sortedList[j]
        // For j < sortedList.length, we use the sorted values
        // For j >= sortedList.length (holes moved to end), we use undefined
        // This is CreateDataPropertyOrThrow which always creates an own property (holes become undefined)
        for (long j = 0; j < length; j++)
        {
            var value = j < values.Count ? values[(int)j].Value : JsValue.Undefined;
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(j), value, Realm, MethodName);
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

                var cmpResult = numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
                return cmpResult != 0 ? cmpResult : a.OriginalIndex.CompareTo(b.OriginalIndex);
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
        if (evalContext?.IsThrow == true)
        {
            throw new ThrowSignal(evalContext.FlowValue);
        }

        var result = CreateCopyArray(length, Realm, MethodName);
        // Per spec: toReversed does NOT preserve holes. Use Get(O, from) which returns
        // undefined for holes (both own and prototype), then CreateDataPropertyOrThrow
        // which always creates an own property. This means holes become undefined own properties.
        for (long k = 0; k < length; k++)
        {
            var from = length - 1 - k;
            // Get(O, from) - returns undefined if property doesn't exist (including prototype)
            var fromValue = GetElementOrUndefinedJsValue(accessor, ToIndexString(from));
            // CreateDataPropertyOrThrow - always create an own data property
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(k), fromValue, Realm, MethodName);
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

        // Per spec step 8: If start is not present, then actualDeleteCount = 0.
        // Per spec step 9: Else if deleteCount is not present, use len - actualStart.
        // NOTE: "not present" means the argument wasn't passed at all, NOT that it's undefined.
        // If deleteCount is passed as undefined, we use ToIntegerOrInfinity(undefined) = 0.
        var startIsNotPresent = args.Count == 0;
        var deleteCountIsNotPresent = args.Count == 1;  // Only when deleteCount arg is truly missing
        long actualDeleteCount;
        if (startIsNotPresent)
        {
            // Per spec step 8: If start is not present, actualDeleteCount = 0
            actualDeleteCount = 0;
        }
        else if (deleteCountIsNotPresent)
        {
            // Per spec step 9: Else if deleteCount is not present, actualDeleteCount = len - actualStart
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
        // Per spec: If newLen > 2^53 - 1, throw TypeError (guard must run before ArrayCreate / 2^32 - 1 checks)
        if (newLength > MaxArrayLength)
        {
            throw ThrowTypeError($"{MethodName} result exceeds 2^53 - 1 elements", realm: Realm);
        }

        if (newLength > MaxConcreteArrayLength)
        {
            throw ThrowRangeError($"{MethodName} result exceeds 2^32 - 1 elements", realm: Realm);
        }

        var result = CreateCopyArray(newLength, Realm, MethodName);
        long targetIndex = 0;

        // Per spec: Array.prototype.toSpliced does NOT preserve holes. Use Get(O, k) which returns
        // undefined for holes, then CreateDataPropertyOrThrow which always creates an own property.
        for (long k = 0; k < actualStart; k++)
        {
            var fromValue = GetElementOrUndefinedJsValue(accessor, ToIndexString(k));
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(targetIndex++), fromValue, Realm, MethodName);
        }

        for (var i = 0; i < insertCount; i++)
        {
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(targetIndex++), args[i + 2], Realm, MethodName);
        }

        for (var k = actualStart + actualDeleteCount; k < length; k++)
        {
            var fromValue = GetElementOrUndefinedJsValue(accessor, ToIndexString(k));
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(targetIndex++), fromValue, Realm, MethodName);
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
            // -Infinity is always out of range per ES spec (len + (-Infinity) = -Infinity < 0)
            throw ThrowRangeError($"{MethodName} index out of range", realm: Realm);
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

        // Per spec: Array.prototype.with does NOT preserve holes. Use Get(O, Pk) which returns
        // undefined for holes, then CreateDataPropertyOrThrow which always creates an own property.
        for (long k = 0; k < length; k++)
        {
            JsValue fromValue;
            if (k == integer)
            {
                fromValue = value;
            }
            else
            {
                // Get(O, Pk) - returns undefined if property doesn't exist (including prototype)
                fromValue = GetElementOrUndefinedJsValue(accessor, ToIndexString(k));
            }

            // CreateDataPropertyOrThrow - always create an own data property
            CreateDataPropertyOrThrowJsValue(result, ToIndexString(k), fromValue, Realm, MethodName);
        }

        SetArrayLikeLength(result, length);
        return JsValue.FromObjectUnsafe(result);
    }

    private static IJsPropertyAccessor? GetPrimitivePrototype(JsValue value, RealmState? realm)
    {
        if (value.IsBoolean) return realm?.BooleanPrototype;
        if (value.IsNumber) return realm?.NumberPrototype;
        if (value.IsString) return realm?.StringPrototype;
        if (value.IsSymbol) return realm?.SymbolPrototype;
        if (value.IsBigInt) return realm?.BigIntPrototype;
        return realm?.ObjectPrototype;
    }
}
