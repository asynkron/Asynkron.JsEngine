#region

using System.Globalization;
using System.Reflection;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using Microsoft.Extensions.Logging;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("TypedArray", ToStringTag = "TypedArray")]
[JsSymbolAlias("iterator", "values", Writable = false)]
public sealed partial class TypedArrayPrototype
{
    [JsHostMethod("reduce", Length = 1d)]
    private JsValue Reduce(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReduceImpl(thisValue, args, "%TypedArray%.prototype.reduce", false);
    }

    [JsHostMethod("reduceRight", Length = 1d)]
    private JsValue ReduceRight(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReduceImpl(thisValue, args, "%TypedArray%.prototype.reduceRight", true);
    }

    [JsHostMethod("map", Length = 1d)]
    private JsValue Map(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return MapImpl(thisValue, args);
    }

    [JsHostMethod("filter", Length = 1d)]
    private JsValue Filter(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FilterImpl(thisValue, args);
    }

    [JsHostMethod("every", Length = 1d)]
    private JsValue Every(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return EveryImpl(thisValue, args);
    }

    [JsHostMethod("find", Length = 1d)]
    private JsValue Find(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FindImpl(thisValue, args);
    }

    [JsHostMethod("findIndex", Length = 1d)]
    private JsValue FindIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FindIndexImpl(thisValue, args);
    }

    [JsHostMethod("findLast", Length = 1d)]
    private JsValue FindLast(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FindLastImpl(thisValue, args);
    }

    [JsHostMethod("findLastIndex", Length = 1d)]
    private JsValue FindLastIndex(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FindLastIndexImpl(thisValue, args);
    }

    [JsHostMethod("forEach", Length = 1d)]
    private JsValue ForEach(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ForEachImpl(thisValue, args);
    }

    [JsHostMethod("fill", Length = 1d)]
    private JsValue Fill(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return FillImpl(thisValue, args);
    }

    [JsHostMethod("copyWithin", Length = 2d)]
    private JsValue CopyWithin(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return CopyWithinImpl(thisValue, args);
    }

    [JsHostMethod("reverse", Length = 0d)]
    private JsValue Reverse(JsValue thisValue)
    {
        return ReverseImpl(thisValue);
    }

    [JsHostMethod("sort", Length = 1d)]
    private JsValue Sort(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return SortImpl(thisValue, args);
    }

    [JsHostMethod("toReversed", Length = 0d)]
    private JsValue ToReversed(JsValue thisValue)
    {
        return ToReversedImpl(thisValue);
    }

    [JsHostMethod("toSorted", Length = 1d)]
    private JsValue ToSorted(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ToSortedImpl(thisValue, args);
    }

    [JsHostMethod("toSpliced", Length = 2d)]
    private JsValue ToSpliced(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ToSplicedImpl(thisValue, args);
    }

    [JsHostMethod("with", Length = 2d)]
    private JsValue With(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return WithImpl(thisValue, args);
    }

    [JsHostMethod("at", Length = 1d)]
    private JsValue At(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.at");
        var length = typedArray.Length;

        var indexArg = args.GetArgument(0);
        var relativeIndex = ToIntegerOrInfinity(indexArg, Realm?.CreateContext(pushScope: false));

        if (double.IsPositiveInfinity(relativeIndex) || double.IsNegativeInfinity(relativeIndex))
        {
            return JsValue.Undefined;
        }

        var index = relativeIndex < 0 ? length + (long)relativeIndex : (long)relativeIndex;

        if (index < 0 || index >= length)
        {
            return JsValue.Undefined;
        }

        return typedArray.GetValueForIndex((int)index);
    }

    [JsHostMethod("indexOf", Length = 1d)]
    private JsValue IndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.indexOf");
        return TypedArrayBase.IndexOfInternal(typedArray, args);
    }

    [JsHostMethod("lastIndexOf", Length = 1d)]
    private JsValue LastIndexOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.lastIndexOf");
        return TypedArrayBase.LastIndexOfInternal(typedArray, args);
    }

    [JsHostMethod("includes", Length = 1d)]
    private JsValue Includes(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.includes");
        return TypedArrayBase.IncludesInternal(typedArray, args);
    }

    [JsHostMethod("some", Length = 1d)]
    private JsValue Some(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JsValue.FromObjectUnsafe(SomeLike(thisValue, args.ToList(), Realm, "%TypedArray%.prototype.some"));
    }

    [JsHostMethod("values", Length = 0d)]
    private JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.values");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray,
            idx => typedArray.GetValueForIndex((int)idx), Realm));
    }

    [JsHostMethod("keys", Length = 0d)]
    private JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.keys");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, idx => new JsValue((double)idx), Realm));
    }

    [JsHostMethod("entries", Length = 0d)]
    private JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.entries");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(
            typedArray,
            idx =>
            {
                var pair = new JsArray(Realm);
                pair.Push((double)idx);
                pair.Push(typedArray.GetValueForIndex((int)idx));
                return JsValue.FromJsArray(pair);
            },
            Realm));
    }

    [JsHostMethod("join", Length = 1d)]
    private JsValue Join(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JoinImpl(thisValue, args);
    }

    [JsHostMethod("toString", Length = 0d)]
    private JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ToStringImpl(thisValue, args);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    private JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ToLocaleStringImpl(thisValue, args);
    }

    protected override void ConfigurePrototype()
    {
        Realm.TypedArrayPrototype ??= Prototype as JsObject;

        // [Symbol.iterator] is registered via code generation from [JsSymbolAlias] attribute
    }

    #region Private Implementation Methods

    private TypedArrayBase ValidateReceiver(JsValue thisValue, string methodName)
    {
        if (thisValue.Kind != JsValueKind.Object || thisValue.ObjectValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: Realm);
        }

        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        return typedArray;
    }

    private JsValue ReduceImpl(JsValue thisValue, IReadOnlyList<JsValue> args, string methodName, bool fromRight)
    {
        var typedArray = ValidateReceiver(thisValue, methodName);

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError($"{methodName} requires a callable accumulator", realm: Realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        var step = fromRight ? -1 : 1;
        var k = fromRight ? length - 1 : 0;

        var accumulator = JsValue.Undefined;
        var hasAccumulator = false;
        if (args.Count > 1 && !args[1].IsUndefined)
        {
            accumulator = args[1];
            hasAccumulator = true;
        }

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

            if (!hasAccumulator)
            {
                accumulator = value;
                hasAccumulator = true;
            }
            else
            {
                accumulator = callback.Invoke([accumulator, value, JsValue.FromNumber((double)k), (JsValue)typedArray],
                    JsValue.Undefined);
            }

            k += step;
        }

        if (!hasAccumulator)
        {
            throw ThrowTypeError($"{methodName} requires at least one element", realm: Realm);
        }

        return accumulator;
    }

    private JsValue MapImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.map");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.map expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        var result = SpeciesCreate(typedArray, length);
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

        return (JsValue)result;
    }

    private JsValue FilterImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.filter");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.filter expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
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

        var filtered = SpeciesCreate(typedArray, kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            filtered.SetValue(i, kept[i]);
        }

        return (JsValue)filtered;
    }

    private JsValue EveryImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.every");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.every expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        if (Realm.Logger is { } logger)
        {
            var strictField = callback.GetType().GetField("_isStrict", BindingFlags.NonPublic | BindingFlags.Instance);
            var strictValue = strictField?.GetValue(callback);
            var thisArgKind = thisArg.IsUndefined ? "undefined" : thisArg.GetType().Name;
            logger.LogInformation(
                "TypedArray.every callback type={Type} strict={Strict} thisArg={ThisArg}",
                callback.GetType().Name,
                strictValue ?? "null",
                thisArgKind);
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (!IsTruthy(result))
            {
                return JsValue.False;
            }
        }

        return JsValue.True;
    }

    private JsValue FindImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.find");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.find expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate)
                ? candidate
                : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    private JsValue FindIndexImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.findIndex");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findIndex expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate)
                ? candidate
                : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    private JsValue FindLastImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.findLast");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findLast expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate)
                ? candidate
                : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return value;
            }
        }

        return JsValue.Undefined;
    }

    private JsValue FindLastIndexImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.findLastIndex");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.findLastIndex expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        for (var k = length - 1; k >= 0; k--)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate)
                ? candidate
                : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (double)k;
            }
        }

        return -1d;
    }

    private JsValue ForEachImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.forEach");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.forEach expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
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

        return JsValue.Undefined;
    }

    private JsValue FillImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.fill");

        var length = typedArray.Length;
        var value = args.GetArgument(0);
        var startIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], Realm.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], Realm.CreateContext()) : length;

        var start = ClampRelativeIndex(startIndex, length);
        var end = ClampRelativeIndex(endIndex, length);

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

        return (JsValue)typedArray;
    }

    private JsValue CopyWithinImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.copyWithin");

        var length = typedArray.Length;
        var toIndex = args.Count > 0 ? ToIntegerOrInfinity(args[0], Realm.CreateContext()) : 0;
        var fromIndex = args.Count > 1 ? ToIntegerOrInfinity(args[1], Realm.CreateContext()) : 0;
        var endIndex = args.Count > 2 ? ToIntegerOrInfinity(args[2], Realm.CreateContext()) : length;

        var to = ClampRelativeIndex(toIndex, length);
        var from = ClampRelativeIndex(fromIndex, length);
        var final = ClampRelativeIndex(endIndex, length);

        var count = Math.Min(final - from, length - to);
        if (count <= 0)
        {
            return (JsValue)typedArray;
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

            var srcValue = typedArray.GetValueForIndex(from);
            typedArray.SetValue(to, srcValue);

            from += direction;
            to += direction;
        }

        return (JsValue)typedArray;
    }

    private JsValue ReverseImpl(JsValue thisValue)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.reverse");

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

        return (JsValue)typedArray;
    }

    private JsValue ToReversedImpl(JsValue thisValue)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.toReversed");

        var length = typedArray.Length;
        var result = SpeciesCreate(typedArray, length);
        for (var k = 0; k < length; k++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = typedArray.GetValueForIndex(length - 1 - k);
            result.SetValue(k, value);
        }

        return (JsValue)result;
    }

    private JsValue ToSortedImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.toSorted");

        IJsCallable? compareFn = null;
        if (args.Count > 0 && !args[0].IsUndefined)
        {
            if (!args[0].TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("TypedArray.prototype.toSorted comparator must be callable", realm: Realm);
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

        var result = SpeciesCreate(typedArray, length);
        for (var i = 0; i < values.Count; i++)
        {
            result.SetValue(i, values[i]);
        }

        return (JsValue)result;

        int Comparer(JsValue left, JsValue right)
        {
            if (compareFn is not null)
            {
                var res = compareFn.Invoke([left, right], JsValue.Undefined);
                var numeric = JsOps.ToNumber(res);
                return numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
            }

            if (typedArray.IsBigIntArray)
            {
                var leftBig = left.TryGetObject<JsBigInt>(out var lb) ? lb : ToBigInt(left, realmState: Realm);
                var rightBig = right.TryGetObject<JsBigInt>(out var rb) ? rb : ToBigInt(right, realmState: Realm);
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

    private JsValue ToSplicedImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.toSpliced");

        var length = typedArray.Length;
        var start = args.Count > 0 ? ToIntegerOrInfinity(args[0], Realm.CreateContext()) : 0;
        var actualStart = ClampRelativeIndex(start, length);

        var deleteCountIsUndefined = args.Count <= 1 || args[1].IsUndefined;
        int actualDeleteCount;
        if (deleteCountIsUndefined)
        {
            actualDeleteCount = length - actualStart;
        }
        else
        {
            var deleteCount = ToIntegerOrInfinity(args[1], Realm.CreateContext());
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

        var result = SpeciesCreate(typedArray, newLength);
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

        return (JsValue)result;
    }

    private JsValue WithImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.with");

        if (args.Count < 2)
        {
            throw ThrowTypeError("TypedArray.prototype.with requires index and value arguments", realm: Realm);
        }

        var length = typedArray.Length;
        var indexNumber = ToIntegerOrInfinity(args[0], Realm.CreateContext());
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
            throw ThrowRangeError("Index out of range", realm: Realm);
        }

        var result = SpeciesCreate(typedArray, length);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var value = i == actualIndex ? args[1] : typedArray.GetValueForIndex(i);
            result.SetValue(i, value);
        }

        return (JsValue)result;
    }

    private TypedArrayBase SpeciesCreate(TypedArrayBase exemplar, int length)
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
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: Realm);
        }

        var constructed = callable.Invoke([JsValue.FromNumber((double)length)], JsValue.Undefined);
        if (!constructed.TryGetObject<TypedArrayBase>(out var typedResult))
        {
            throw ThrowTypeError("TypedArray species constructor did not return a TypedArray instance", realm: Realm);
        }

        if (typedResult.Length < length)
        {
            throw ThrowTypeError("TypedArray species constructor result has insufficient length", realm: Realm);
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

    private static int ClampRelativeIndex(double index, int length)
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

    [JsHostMethod("subarray", Length = 2d)]
    public JsValue Subarray(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.subarray");

        var begin = 0;
        var end = typedArray.Length;

        if (args.Count > 0 && !args[0].IsUndefined)
        {
            begin = (int)args[0].ToNumber();
        }

        if (args.Count > 1 && !args[1].IsUndefined)
        {
            end = (int)args[1].ToNumber();
        }

        return (JsValue)typedArray.Subarray(begin, end);
    }

    private JsValue SortImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.sort");

        IJsCallable? compareFn = null;
        if (args.Count > 0 && !args[0].IsUndefined)
        {
            if (!args[0].TryGetObject<IJsCallable>(out var callable))
            {
                throw ThrowTypeError("TypedArray.prototype.sort comparator must be callable", realm: Realm);
            }

            compareFn = callable;
        }

        var length = typedArray.Length;
        if (length <= 1)
        {
            return (JsValue)typedArray;
        }

        // Read all values into a list
        var values = new List<JsValue>(length);
        for (var i = 0; i < length; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            values.Add(typedArray.GetValueForIndex(i));
        }

        // Sort the values
        values.Sort(Comparer);

        // Write sorted values back
        for (var i = 0; i < values.Count; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            typedArray.SetValue(i, values[i]);
        }

        return (JsValue)typedArray;

        int Comparer(JsValue left, JsValue right)
        {
            if (compareFn is not null)
            {
                var res = compareFn.Invoke([left, right], JsValue.Undefined);
                var numeric = JsOps.ToNumber(res);
                return numeric > 0 ? 1 : numeric < 0 ? -1 : 0;
            }

            if (typedArray.IsBigIntArray)
            {
                var leftBig = left.TryGetObject<JsBigInt>(out var lb) ? lb : ToBigInt(left, realmState: Realm);
                var rightBig = right.TryGetObject<JsBigInt>(out var rb) ? rb : ToBigInt(right, realmState: Realm);
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

    private JsValue JoinImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.join");

        var length = typedArray.Length;
        var separator = args.Count > 0 && !args[0].IsUndefined
            ? JsOps.ToJsString(args[0], Realm?.CreateContext(pushScope: false))
            : ",";

        if (length == 0)
        {
            return JsValue.FromString(string.Empty);
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var element = typedArray.GetValueForIndex(i);
            if (!element.IsNullOrUndefined)
            {
                sb.Append(JsOps.ToJsString(element, Realm?.CreateContext(pushScope: false)));
            }
        }

        return JsValue.FromString(sb.ToString());
    }

    private JsValue ToStringImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // %TypedArray%.prototype.toString is the same as Array.prototype.toString
        // which calls the 'join' method on the object
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.toString");

        // Look up the join method and call it
        if (typedArray.TryGetProperty("join", (JsValue)typedArray, out var joinMethod) &&
            joinMethod.TryGetObject<IJsCallable>(out var joinCallable))
        {
            return joinCallable.Invoke([], (JsValue)typedArray);
        }

        // Fallback to direct join if no join method found
        return JoinImpl(thisValue, []);
    }

    private JsValue ToLocaleStringImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.toLocaleString");

        var length = typedArray.Length;
        if (length == 0)
        {
            return JsValue.FromString(string.Empty);
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var element = typedArray.GetValueForIndex(i);
            if (!element.IsNullOrUndefined)
            {
                // Call toLocaleString on the element if it's an object with that method
                if (element.IsObject &&
                    element.TryGetObject<IJsPropertyAccessor>(out var obj) &&
                    obj.TryGetProperty("toLocaleString", out var toLocaleMethod) &&
                    toLocaleMethod.TryGetObject<IJsCallable>(out var callable))
                {
                    var result = callable.Invoke(args.ToList(), element);
                    sb.Append(JsOps.ToJsString(result, Realm?.CreateContext(pushScope: false)));
                }
                else
                {
                    // For primitive numbers, use InvariantCulture formatting
                    // to match JavaScript behavior
                    if (element.IsNumber)
                    {
                        sb.Append(element.AsDouble().ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(JsOps.ToJsString(element, Realm?.CreateContext(pushScope: false)));
                    }
                }
            }
        }

        return JsValue.FromString(sb.ToString());
    }

    #endregion
}
