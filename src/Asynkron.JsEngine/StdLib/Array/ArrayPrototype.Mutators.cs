#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public sealed partial class ArrayPrototype
{
    /* FLAKY */
    [JsHostMethod("push", Length = 1d)]
    public JsValue Push(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.push", Realm);

        // Re-entrancy guard: prevent infinite recursion when length getter calls push
        const string ReentrancyKey = "__inPush__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inPushFlag) && !inPushFlag.IsUndefined)
        {
            // Already in push, return current length to break recursion
            return new JsValue(0d);
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (long)ToLengthOrZero(lengthValue);
            var newLength = length + args.Count;
            if (newLength > ArrayHelper.MaxConcreteArrayLength)
            {
                throw ThrowTypeError("Array.prototype.push cannot exceed 2^32 - 1 elements", realm: Realm);
            }

            for (var i = 0; i < args.Count; i++)
            {
                var index = length + i;
                accessor.SetProperty(ToIndexString(index), args[i]);
            }

            // Set length using ToUint32 semantics
            accessor.SetProperty("length", (double)newLength);
            return new JsValue((double)newLength);
        }
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("pop", Length = 0d)]
    public JsValue Pop(JsValue thisValue)
    {
        const string MethodName = "Array.prototype.pop";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        // Re-entrancy: if sorting in progress, avoid mutating length/elements
        if (accessor.TryGetProperty("__sorting__", out var sortingFlag) && !sortingFlag.IsUndefined)
        {
            return JsValue.Undefined;
        }

        // Re-entrancy guard: prevent infinite recursion when length getter calls pop
        const string ReentrancyKey = "__inPop__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inPopFlag) && !inPopFlag.IsUndefined)
        {
            // Already in pop, return undefined to break recursion
            return JsValue.Undefined;
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
            var objectLike = accessor as IJsObjectLike;
            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (long)ToLengthOrZero(lengthValue);
            if (length == 0)
            {
                accessor.SetProperty("length", 0d);
                return JsValue.Undefined;
            }

            var newLength = length - 1;
            var key = ToIndexString(newLength);
            var elementExists = TryGetExistingElement(accessor, key, out var element);
            DeletePropertyOrThrow(objectLike, key, elementExists, MethodName, Realm);
            accessor.SetProperty("length", (double)newLength);
            return elementExists ? element : JsValue.Undefined;
        }
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("shift", Length = 0d)]
    public JsValue Shift(JsValue thisValue)
    {
        const string MethodName = "Array.prototype.shift";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        // Re-entrancy: if sorting in progress, avoid mutating length/elements
        if (accessor.TryGetProperty("__sorting__", out var sortingFlag) && !sortingFlag.IsUndefined)
        {
            return JsValue.Undefined;
        }

        // Re-entrancy guard: prevent infinite recursion when length getter calls shift
        const string ReentrancyKey = "__inShift__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inShiftFlag) && !inShiftFlag.IsUndefined)
        {
            // Already in shift, return undefined to break recursion
            return JsValue.Undefined;
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
            var objectLike = accessor as IJsObjectLike;
            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (long)ToLengthOrZero(lengthValue);
            if (length == 0)
            {
                accessor.SetProperty("length", 0d);
                return JsValue.Undefined;
            }

            var firstElement = JsValue.Undefined;
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
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("unshift", Length = 1d)]
    public JsValue Unshift(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        const string MethodName = "Array.prototype.unshift";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);

        // Re-entrancy guard: prevent infinite recursion when length getter calls unshift
        const string ReentrancyKey = "__inUnshift__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inUnshiftFlag) && !inUnshiftFlag.IsUndefined)
        {
            // Already in unshift, return 0 to break recursion
            return new JsValue(0d);
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
            var objectLike = accessor as IJsObjectLike;
            var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
            var length = (long)ToLengthOrZero(lengthValue);
            var argCount = args.Count;

            if (length + argCount > ArrayHelper.MaxConcreteArrayLength)
            {
                throw ThrowTypeError("Array.prototype.unshift cannot exceed 2^32 - 1 elements", realm: Realm);
            }

            for (var k = length - 1; k >= 0; k--)
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
                    DeletePropertyOrThrow(objectLike, toKey, toExists, MethodName, Realm);
                }
            }

            for (var j = 0; j < argCount; j++)
            {
                accessor.SetProperty(ToIndexString(j), args[j]);
            }

            var newLength = length + argCount;
            accessor.SetProperty("length", (double)newLength);
            return new JsValue((double)newLength);
        }
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("splice", Length = 2d)]
    public JsValue Splice(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        const string MethodName = "Array.prototype.splice";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);

        // Re-entrancy guard: prevent infinite recursion when length getter calls splice
        const string ReentrancyKey = "__inSplice__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inSpliceFlag) && !inSpliceFlag.IsUndefined)
        {
            // Already in splice, return empty array to break recursion
            return JsValue.FromObjectUnsafe(ArraySpeciesCreate(thisValue, 0, Realm));
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
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
            if (newLength > ArrayHelper.MaxConcreteArrayLength)
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
                for (var k = actualStart; k < length - actualDeleteCount; k++)
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

                for (var k = length; k > length - (actualDeleteCount - insertCount); k--)
                {
                    var key = ToIndexString(k - 1);
                    var existed = HasProperty(accessor, key);
                    DeletePropertyOrThrow(objectLike, key, existed, MethodName, Realm);
                }
            }
            else if (insertCount > actualDeleteCount)
            {
                for (var k = length - actualDeleteCount; k > actualStart; k--)
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
            return JsValue.FromObjectUnsafe(result);
        }
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("reverse", Length = 0d)]
    public JsValue Reverse(JsValue thisValue)
    {
        const string MethodName = "Array.prototype.reverse";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);

        // Re-entrancy guard: prevent infinite recursion when length getter calls reverse
        const string ReentrancyKey = "__inReverse__";
        if (accessor.TryGetProperty(ReentrancyKey, out var inReverseFlag) && !inReverseFlag.IsUndefined)
        {
            // Already in reverse, return the array to break recursion
            return JsValue.FromObjectUnsafe(accessor);
        }

        try
        {
            accessor.SetProperty(ReentrancyKey, true);
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
                    lowerValue = JsValue.Undefined;
                }

                var upperExists = TryGetExistingElement(accessor, upperKey, out var upperValue);
                if (!upperExists)
                {
                    upperValue = JsValue.Undefined;
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

            return JsValue.FromObjectUnsafe(accessor);
        }
        finally
        {
            accessor.SetProperty(ReentrancyKey, JsValue.Undefined);
        }
    }

    /* FLAKY */
    [JsHostMethod("concat", Length = 1d)]
    public JsValue Concat(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        const string MethodName = "Array.prototype.concat";
        var accessor = EnsureArrayLikeReceiver(thisValue, MethodName, Realm);
        var result = ArraySpeciesCreate(thisValue, 0, Realm);
        long resultIndex = 0;

        var sources = new JsValue[args.Count + 1];
        sources[0] = JsValue.FromObjectUnsafe(accessor);
        for (var i = 0; i < args.Count; i++)
        {
            sources[i + 1] = args[i];
        }

        foreach (var sourceValue in sources)
        {
            if (IsConcatSpreadable(sourceValue, Realm, out var spreadAccessor))
            {
                var spreadLength = LengthOfArrayLike(spreadAccessor, Realm);
                const long MaxSafeIntegerLength = 9007199254740991L; // 2^53 - 1
                if (resultIndex + spreadLength > MaxSafeIntegerLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^53 - 1", realm: Realm);
                }

                for (long k = 0; k < spreadLength; k++)
                {
                    var fromKey = ToIndexString(k);
                    var toKey = ToIndexString(resultIndex);
                    if (TryGetExistingElement(spreadAccessor, fromKey, out var value))
                    {
                        CreateDataPropertyOrThrow(result, toKey, value, Realm, MethodName);
                    }

                    resultIndex++;
                }
            }
            else
            {
                const long MaxSafeIntegerLength = 9007199254740991L; // 2^53 - 1
                if (resultIndex >= MaxSafeIntegerLength)
                {
                    throw ThrowTypeError("Array length exceeds 2^53 - 1", realm: Realm);
                }

                CreateDataPropertyOrThrow(result, ToIndexString(resultIndex++), sourceValue, Realm, MethodName);
            }
        }

        SetArrayLikeLength(result, resultIndex);
        return JsValue.FromObjectUnsafe(result);
    }

    /* FLAKY */
    [JsHostMethod("sort", Length = 1d)]
    public JsValue Sort(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var realm = Realm;
        var accessor = EnsureArrayLikeReceiver(thisValue, "Array.prototype.sort", realm);
        var objectLike = accessor as IJsObjectLike;

        // Validate comparefn before touching length (per spec)
        IJsCallable? compareFn = null;
        if (args.Count > 0 && !args[0].IsUndefined)
        {
            if (!args[0].TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("Array.prototype.sort comparefn must be callable", realm: realm);
            }

            compareFn = callable;
        }

        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var length = (long)ToLengthOrZero(lengthValue);

        // Collect all values upfront - this is the "snapshot" approach required by ES spec
        // We need to materialize values before sorting because the comparator could mutate the array
        var elements = new List<(JsValue Value, long OriginalIndex)>((int)Math.Min(length, int.MaxValue));
        var holes = new List<long>(); // Track holes (sparse array indices with no property)

        for (long k = 0; k < length; k++)
        {
            var key = ToIndexString(k);
            if (HasProperty(accessor, key))
            {
                var value = GetElementOrUndefinedJsValue(accessor, key);
                elements.Add((value, k));
            }
            else
            {
                holes.Add(k);
            }
        }

        elements.Sort(Comparer);

        // Write sorted values back to the array
        long index = 0;
        foreach (var pair in elements)
        {
            accessor.SetProperty(ToIndexString(index++), pair.Value);
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
                accessor.SetProperty(ToIndexString(k), JsValue.Undefined);
            }
        }

        // Clear re-entrancy guard
        accessor.SetProperty("__sorting__", JsValue.Undefined);

        return JsValue.FromObjectUnsafe(accessor);

        int Comparer((JsValue Value, long OriginalIndex) a, (JsValue Value, long OriginalIndex) b)
        {
            var aVal = a.Value;
            var bVal = b.Value;

            if (compareFn is not null)
            {
                var result = compareFn.Invoke([aVal, bVal], JsValue.Undefined);
                var ctx = realm?.CreateContext();
                var num = JsOps.ToNumber(result, ctx);
                if (ctx?.IsThrow == true)
                {
                    throw new ThrowSignal(ctx.FlowValue);
                }

                if (double.IsNaN(num))
                {
                    return a.OriginalIndex.CompareTo(b.OriginalIndex);
                }

                var cmp = num > 0 ? 1 : num < 0 ? -1 : 0;
                return cmp != 0 ? cmp : a.OriginalIndex.CompareTo(b.OriginalIndex);
            }

            var aUndef = aVal.IsUndefined;
            var bUndef = bVal.IsUndefined;
            if (aUndef || bUndef)
            {
                if (aUndef && bUndef)
                {
                    return a.OriginalIndex.CompareTo(b.OriginalIndex);
                }

                return aUndef ? 1 : -1;
            }

            var aStr = aVal.ToJsString();
            var bStr = bVal.ToJsString();
            var ord = string.CompareOrdinal(aStr, bStr);
            return ord != 0 ? ord : a.OriginalIndex.CompareTo(b.OriginalIndex);
        }
    }
}
