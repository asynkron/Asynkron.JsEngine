using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static HostFunction EnsureTypedArrayIntrinsic(RealmState realm)
    {
        if (realm.TypedArrayPrototype is null || realm.TypedArrayConstructor is null)
        {
            return TypedArrayConstructor.CreateConstructor(realm);
        }

        return realm.TypedArrayConstructor!;
    }

    internal static TypedArrayBase ValidateTypedArrayReceiverInternal(JsValue thisValue, string methodName, RealmState? realm)
    {
        var obj = thisValue.ToObject();
        if (obj is JsValue jsVal)
        {
            obj = jsVal.ToObject();
        }

        if (obj is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        return typedArray;
    }

    internal static object? TypedArrayReduceInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm,
        string methodName, bool fromRight)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError($"{methodName} requires a callable accumulator", realm: realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var step = fromRight ? -1 : 1;
        var k = fromRight ? length - 1 : 0;

        object? accumulator = Symbol.Undefined;
        var hasAccumulator = false;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            accumulator = args[1];
            hasAccumulator = true;
        }

        var visited = 0;

        while (k >= 0 && k < length)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            visited++;

            if (!hasAccumulator)
            {
                accumulator = value;
                hasAccumulator = true;
            }
            else
            {
                var accumulatorJs = accumulator is JsValue accJs ? accJs : JsValue.FromObjectUnsafe(accumulator);
                accumulator = callback.Invoke([accumulatorJs, value, JsValue.FromNumber((double)k), (JsValue)typedArray], JsValue.Undefined);
            }

            k += step;
        }

        if (!hasAccumulator)
        {
            throw ThrowTypeError($"{methodName} requires at least one element", realm: realm);
        }

        return accumulator;
    }

    internal static object? TypedArrayMapInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is JsValue jsVal) thisValue = jsVal.ToObject();
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.map called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.map expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var result = TypedArraySpeciesCreateInternal(typedArray, length, realm);
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            var mapped = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            result.SetValue(k, mapped);
        }

        return result;
    }

    internal static object? TypedArrayFilterInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.filter called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.filter expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var kept = new List<JsValue>();
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(result))
            {
                kept.Add(value);
            }
        }

        var filtered = TypedArraySpeciesCreateInternal(typedArray, kept.Count, realm);
        for (var i = 0; i < kept.Count; i++)
        {
            filtered.SetValue(i, kept[i]);
        }

        return filtered;
    }

    internal static object? TypedArrayEveryInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.every called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.every expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (realm?.Logger is { } logger)
        {
            var strictField = callback.GetType().GetField("_isStrict", BindingFlags.NonPublic | BindingFlags.Instance);
            var strictValue = strictField?.GetValue(callback);
            var thisArgKind = thisArg.IsUndefined
                ? "undefined"
                : thisArg.GetType().Name;
            logger.LogInformation(
                "TypedArray.every callback type={Type} strict={Strict} thisArg={ThisArg}",
                callback.GetType().Name,
                strictValue ?? "null",
                thisArgKind);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (!IsTruthy(result))
            {
                return false;
            }
        }

        return true;
    }

    internal static object? TypedArrayFindInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.find called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.find expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate) ? candidate : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    internal static object? TypedArrayFindIndexInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findIndex called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findIndex expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate) ? candidate : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    internal static object? TypedArrayFindLastInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findLast called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findLast expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate) ? candidate : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    internal static object? TypedArrayFindLastIndexInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.findLastIndex called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findLastIndex expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate) ? candidate : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    internal static object? TypedArrayForEachInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.forEach called on incompatible receiver", realm: realm);
        }

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.forEach expects a callable callback", realm: realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(k);
            callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
        }

        return Symbol.Undefined;
    }

    internal static object? TypedArrayFillInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.fill called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var value = args.GetArgument(0);
        var startIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], realm?.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], realm?.CreateContext()) : length;

        var start = ClampRelativeIndexInternal(startIndex, length);
        var end = ClampRelativeIndexInternal(endIndex, length);

        for (var k = start; k < end; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            if (k >= typedArray.Length)
            {
                break;
            }

            typedArray.SetValue(k, value);
        }

        return typedArray;
    }

    internal static object? TypedArrayCopyWithinInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.copyWithin called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var toIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], realm?.CreateContext()) : 0;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], realm?.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], realm?.CreateContext()) : length;

        var to = ClampRelativeIndexInternal(toIndex, length);
        var from = ClampRelativeIndexInternal(fromIndex, length);
        var final = ClampRelativeIndexInternal(endIndex, length);

        var count = Math.Min(final - from, length - to);
        if (count <= 0)
        {
            return typedArray;
        }

        var direction = 1;
        if (from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }

        for (var i = 0; i < count; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var currentLength = typedArray.Length;
            if (from < 0 || from >= currentLength || to < 0 || to >= currentLength)
            {
                break;
            }

            var value = typedArray.GetValueForIndex(from);
            typedArray.SetValue(to, value);

            from += direction;
            to += direction;
        }

        return typedArray;
    }

    internal static object? TypedArrayReverseInternal(object? thisValue, IReadOnlyList<JsValue> _, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.reverse called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var middle = length / 2;

        for (var lower = 0; lower < middle; lower++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var upper = length - lower - 1;
            var lowerValue = typedArray.GetValueForIndex(lower);

            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var upperValue = typedArray.GetValueForIndex(upper);
            typedArray.SetValue(lower, upperValue);
            typedArray.SetValue(upper, lowerValue);
        }

        return typedArray;
    }

    internal static object? TypedArrayToReversedInternal(object? thisValue, IReadOnlyList<JsValue> _, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toReversed called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var result = TypedArraySpeciesCreateInternal(typedArray, length, realm);
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = typedArray.GetValueForIndex(length - 1 - k);
            result.SetValue(k, value);
        }

        return result;
    }

    internal static object? TypedArrayToSortedInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toSorted called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        IJsCallable? compareFn = null;
        if (args.Count > 0 && !args[0].IsUndefined)
        {
            if (!args[0].TryGetObject<IJsCallable>(out var callable) )
            {
                throw ThrowTypeError("TypedArray.prototype.toSorted comparator must be callable", realm: realm);
            }

            compareFn = callable;
        }

        var length = typedArray.Length;
        var values = new List<JsValue>(length);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            values.Add(typedArray.GetValueForIndex(i));
        }

        values.Sort(Comparer);

        var result = TypedArraySpeciesCreateInternal(typedArray, length, realm);
        for (var i = 0; i < values.Count; i++)
        {
            result.SetValue(i, values[i]);
        }

        return result;

        int Comparer(JsValue left, JsValue right)
        {
            if (compareFn is not null)
            {
                var result = compareFn.Invoke([left, right], JsValue.Undefined);
                var numeric = JsOps.ToNumber(result);
                return numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
            }

            if (typedArray.IsBigIntArray)
            {
                var leftObj = left.ToObject();
                var rightObj = right.ToObject();
                var leftBig = leftObj as JsBigInt ?? ToBigInt(leftObj, realmState: realm);
                var rightBig = rightObj as JsBigInt ?? ToBigInt(rightObj, realmState: realm);
                return leftBig.Value.CompareTo(rightBig.Value);
            }

            var leftNum = JsOps.ToNumber(left);
            var rightNum = JsOps.ToNumber(right);
            if (double.IsNaN(leftNum))
            {
                return double.IsNaN(rightNum) ? 0 : 1;
            }

            if (double.IsNaN(rightNum))
            {
                return -1;
            }

            return leftNum.CompareTo(rightNum);
        }
    }

    internal static object? TypedArrayToSplicedInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.toSpliced called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var start = args.Count > 0 ? ToIntegerOrInfinity(args[0], realm?.CreateContext()) : 0;
        var actualStart = ClampRelativeIndexInternal(start, length);

        var deleteCountIsUndefined = args.Count <= 1 || args[1].IsUndefined;
        int actualDeleteCount;
        if (deleteCountIsUndefined)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCount = ToIntegerOrInfinity(args[1], realm?.CreateContext());
            if (double.IsPositiveInfinity(deleteCount))
            {
                actualDeleteCount = length - actualStart;
            }
            else
            {
                var bounded = Math.Max(deleteCount, 0);
                bounded = Math.Min(bounded, length - actualStart);
                actualDeleteCount = (int)bounded;
            }
        }
        var insertCount = Math.Max(args.Count - 2, 0);
        var newLength = length - actualDeleteCount + insertCount;

        var result = TypedArraySpeciesCreateInternal(typedArray, newLength, realm);
        var targetIndex = 0;

        for (var i = 0; i < actualStart; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            result.SetValue(targetIndex++, typedArray.GetValueForIndex(i));
        }

        for (var i = 0; i < insertCount; i++)
        {
            result.SetValue(targetIndex++, args[i + 2]);
        }

        for (var i = actualStart + actualDeleteCount; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            result.SetValue(targetIndex++, typedArray.GetValueForIndex(i));
        }

        return result;
    }

    internal static object? TypedArrayWithInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError("TypedArray.prototype.with called on incompatible receiver", realm: realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        if (args.Count < 2)
        {
            throw ThrowTypeError("TypedArray.prototype.with requires index and value arguments", realm: realm);
        }

        var length = typedArray.Length;
        var indexNumber = ToIntegerOrInfinity(args[0], realm?.CreateContext());
        int actualIndex;
        if (double.IsPositiveInfinity(indexNumber) || double.IsNegativeInfinity(indexNumber))
        {
            actualIndex = indexNumber > 0 ? length : -1;
        }
        else
        {
            var truncated = (int)Math.Truncate(indexNumber);
            actualIndex = truncated < 0 ? length + truncated : truncated;
        }

        if (actualIndex < 0 || actualIndex >= length)
        {
            throw ThrowRangeError("Index out of range", realm: realm);
        }

        var result = TypedArraySpeciesCreateInternal(typedArray, length, realm);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = i == actualIndex ? args[1] : typedArray.GetValueForIndex(i);
            result.SetValue(i, value);
        }

        return result;
    }

    internal static object? TypedArrayIndexOfInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.indexOf called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.IndexOfInternal(typed, args);
    }

    internal static object? TypedArrayLastIndexOfInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.lastIndexOf called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.LastIndexOfInternal(typed, args);
    }

    internal static object? TypedArrayIncludesInternal(object? thisValue, IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (thisValue is not TypedArrayBase typed)
        {
            throw ThrowTypeError("TypedArray.prototype.includes called on incompatible receiver", realm: realm);
        }

        return TypedArrayBase.IncludesInternal(typed, args);
    }

    private static TypedArrayBase TypedArraySpeciesCreateInternal(TypedArrayBase exemplar, int length, RealmState? realm)
    {
        length = Math.Max(length, 0);
        var constructorValue = JsValue.Undefined;

        if (exemplar.TryGetProperty("constructor", (JsValue)exemplar, out var ctorValue))
        {
            constructorValue = ctorValue;
        }

        if (constructorValue.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor) &&
            ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
        {
            constructorValue = speciesValue;
        }

        if (constructorValue.IsNullOrUndefined)
        {
            return CreateDefaultTypedArray(exemplar, length);
        }

        if (!JsOps.IsConstructor(constructorValue) || !constructorValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: realm);
        }

        var constructed = callable.Invoke([JsValue.FromNumber((double)length)], JsValue.Undefined);
        if (!constructed.TryGetObject<TypedArrayBase>(out var typedResult))
        {
            throw ThrowTypeError("TypedArray species constructor did not return a TypedArray instance", realm: realm);
        }

        if (typedResult.Length < length)
        {
            throw ThrowTypeError("TypedArray species constructor result has insufficient length", realm: realm);
        }

        return typedResult;

        static TypedArrayBase CreateDefaultTypedArray(TypedArrayBase exemplarArray, int len)
        {
            var fallback = exemplarArray.CreateSpeciesDefault(len);
            if (exemplarArray.Prototype is not null)
            {
                fallback.SetPrototype(exemplarArray.Prototype);
            }

            return fallback;
        }
    }

    private static int ClampRelativeIndexInternal(double index, int length)
    {
        if (double.IsNegativeInfinity(index))
        {
            return 0;
        }

        if (double.IsPositiveInfinity(index))
        {
            return length;
        }

        var integer = (int)Math.Truncate(index);
        if (integer >= 0)
        {
            return integer > length ? length : integer;
        }

        var relative = length + integer;
        return relative < 0 ? 0 : relative;
    }
}
