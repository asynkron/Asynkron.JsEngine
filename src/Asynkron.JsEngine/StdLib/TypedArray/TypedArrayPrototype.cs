#region

using System.Globalization;
using System.Reflection;
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
    #region Accessor Properties (buffer, byteLength, byteOffset, length)

    [JsHostGetter("buffer")]
    private JsValue GetBuffer(JsValue thisValue)
    {
        var typedArray = ValidateReceiverForGetter(thisValue, "%TypedArray%.prototype.buffer");
        return JsValue.FromObjectUnsafe(typedArray.Buffer);
    }

    [JsHostGetter("byteLength")]
    private JsValue GetByteLength(JsValue thisValue)
    {
        var typedArray = ValidateReceiverForGetter(thisValue, "%TypedArray%.prototype.byteLength");
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            return JsValue.FromDouble(0);
        }

        return JsValue.FromDouble(typedArray.ByteLength);
    }

    [JsHostGetter("byteOffset")]
    private JsValue GetByteOffset(JsValue thisValue)
    {
        var typedArray = ValidateReceiverForGetter(thisValue, "%TypedArray%.prototype.byteOffset");
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            return JsValue.FromDouble(0);
        }

        return JsValue.FromDouble(typedArray.ByteOffset);
    }

    [JsHostGetter("length")]
    private JsValue GetLength(JsValue thisValue)
    {
        var typedArray = ValidateReceiverForGetter(thisValue, "%TypedArray%.prototype.length");
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            return JsValue.FromDouble(0);
        }

        return JsValue.FromDouble(typedArray.Length);
    }

    private TypedArrayBase ValidateReceiverForGetter(JsValue thisValue, string methodName)
    {
        // Per spec, if this is not an object, throw TypeError
        if (thisValue.Kind != JsValueKind.Object)
        {
            throw ThrowTypeError($"{methodName} requires that 'this' be an Object", realm: Realm);
        }

        // Per spec, if this doesn't have [[TypedArrayName]] internal slot, throw TypeError
        if (thisValue.ObjectValue is not TypedArrayBase typedArray)
        {
            throw ThrowTypeError($"{methodName} requires that 'this' have a [[TypedArrayName]] internal slot", realm: Realm);
        }

        return typedArray;
    }

    #endregion

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
        return SomeImpl(thisValue, args);
    }

    [JsHostMethod("values", Length = 0d)]
    private JsValue Values(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.values");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, ArrayIteratorKind.Values, Realm));
    }

    [JsHostMethod("keys", Length = 0d)]
    private JsValue Keys(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.keys");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, ArrayIteratorKind.Keys, Realm));
    }

    [JsHostMethod("entries", Length = 0d)]
    private JsValue Entries(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.entries");
        return JsValue.FromObjectUnsafe(CreateArrayIteratorObject(typedArray, ArrayIteratorKind.Entries, Realm));
    }

    [JsHostMethod("join", Length = 1d)]
    private JsValue Join(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return JoinImpl(thisValue, args);
    }

    // NOTE: toString is NOT a [JsHostMethod] here because per ECMAScript spec,
    // TypedArray.prototype.toString === Array.prototype.toString (same function object).
    // We copy Array.prototype.toString to TypedArray.prototype in ConfigurePrototype().

    [JsHostMethod("toLocaleString", Length = 0d)]
    private JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ToLocaleStringImpl(thisValue, args);
    }

    protected override void ConfigurePrototype()
    {
        Realm.TypedArrayPrototype ??= Prototype as JsObject;

        // [Symbol.iterator] is registered via code generation from [JsSymbolAlias] attribute

        // Per ECMAScript spec, TypedArray.prototype.toString === Array.prototype.toString
        // They must be the exact same function object
        if (Realm.ArrayPrototype is IJsPropertyAccessor arrayProto &&
            arrayProto.TryGetProperty("toString", out var arrayToString))
        {
            (Prototype as JsObject)?.TryDefineProperty(
                "toString",
                new PropertyDescriptor
                {
                    Value = arrayToString,
                    Writable = true,
                    Enumerable = false,
                    Configurable = true
                });
        }
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

    private JsValue SomeImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "TypedArray.prototype.some");

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError("TypedArray.prototype.some expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        for (var k = 0; k < length; k++)
        {
            var value = typedArray.GetValueForIndex(k);
            var result = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(result))
            {
                return JsValue.True;
            }
        }

        return JsValue.False;
    }

    private JsValue FindImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "TypedArray.prototype.find", reverse: false);
        return result?.Value ?? JsValue.Undefined;
    }

    private JsValue FindIndexImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "TypedArray.prototype.findIndex", reverse: false);
        return result is { } r ? r.Index : -1d;
    }

    private JsValue FindLastImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "TypedArray.prototype.findLast", reverse: true);
        return result?.Value ?? JsValue.Undefined;
    }

    private JsValue FindLastIndexImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var result = FindCore(thisValue, args, "TypedArray.prototype.findLastIndex", reverse: true);
        return result is { } r ? (double)r.Index : -1d;
    }

    /// <summary>
    /// Core find implementation that returns both index and value.
    /// Used by find, findIndex, findLast, and findLastIndex.
    /// </summary>
    private (int Index, JsValue Value)? FindCore(
        JsValue thisValue,
        IReadOnlyList<JsValue> args,
        string methodName,
        bool reverse)
    {
        var typedArray = ValidateReceiver(thisValue, methodName);

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError($"{methodName} expects a callable callback", realm: Realm);
        }

        var thisArg = args.GetArgument(1);
        if (callback is IJsEnvironmentAwareCallable envAware && Realm.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var length = typedArray.Length;
        var start = reverse ? length - 1 : 0;
        var end = reverse ? -1 : length;
        var step = reverse ? -1 : 1;

        for (var k = start; k != end; k += step)
        {
            var key = k.ToString(CultureInfo.InvariantCulture);
            var value = typedArray.TryGetProperty(key, (JsValue)typedArray, out var candidate)
                ? candidate
                : JsValue.Undefined;
            var match = callback.Invoke([value, JsValue.FromNumber((double)k), (JsValue)typedArray], thisArg);
            if (IsTruthy(match))
            {
                return (k, value);
            }
        }

        return null;
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
        // Per spec: if end is undefined, use length
        var endIndex = args.Count > 2 && !args[2].IsUndefined ? ToIntegerOrInfinity(args[2], Realm.CreateContext()) : length;

        var start = ClampRelativeIndex(startIndex, length);
        var end = ClampRelativeIndex(endIndex, length);

        // Per spec, coerce fill value once before writing.
        var valueContext = Realm.CreateContext();
        if (typedArray.IsBigIntArray)
        {
            value = new JsValue(ToBigInt(value, valueContext, Realm));
        }
        else
        {
            if (value.IsBigInt)
            {
                throw ThrowTypeError("Cannot convert a BigInt value to a number", valueContext, Realm);
            }

            var numeric = JsOps.ToNumber(value, valueContext);
            if (valueContext.IsThrow == true)
            {
                throw new ThrowSignal(valueContext.FlowValue);
            }

            value = new JsValue(numeric);
        }

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
        // Per spec: if end is undefined, use length
        var endIndex = args.Count > 2 && !args[2].IsUndefined ? ToIntegerOrInfinity(args[2], Realm.CreateContext()) : length;

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
        // Per spec: TypedArrayCreateSameType - do NOT use species, always create same typed array
        var result = typedArray.CreateSpeciesDefault(length);
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

        // Wrap sort in try-catch to properly propagate ThrowSignal from compareFn
        // The .NET Sort may wrap the exception in InvalidOperationException
        try
        {
            values.Sort(Comparer);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is ThrowSignal ts)
        {
            // Re-throw the original ThrowSignal
            throw ts;
        }
        catch (ThrowSignal)
        {
            // Directly propagate ThrowSignal
            throw;
        }

        // Per spec: TypedArrayCreateSameType - do NOT use species, always create same typed array
        var result = typedArray.CreateSpeciesDefault(length);
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
                var context = Realm?.CreateContext();
                var numeric = JsOps.ToNumber(res, context);
                if (context?.IsThrow == true)
                {
                    throw new ThrowSignal(context.FlowValue);
                }
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

        // Per spec: TypedArrayCreateSameType - do NOT use species, always create same typed array
        var result = typedArray.CreateSpeciesDefault(newLength);
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

        var value = args[1];
        var valueContext = Realm.CreateContext();
        if (typedArray.IsBigIntArray)
        {
            value = new JsValue(ToBigInt(value, valueContext, Realm));
        }
        else
        {
            if (value.IsBigInt)
            {
                throw ThrowTypeError("Cannot convert a BigInt value to a number", valueContext, Realm);
            }

            var numeric = JsOps.ToNumber(value, valueContext);
            if (valueContext.IsThrow == true)
            {
                throw new ThrowSignal(valueContext.FlowValue);
            }

            value = new JsValue(numeric);
        }

        if (typedArray.IsDetachedOrOutOfBounds() || actualIndex < 0 || actualIndex >= typedArray.Length)
        {
            throw ThrowRangeError("Index out of range", realm: Realm);
        }

        // Per spec: TypedArrayCreateSameType - do NOT use species, always create same typed array
        var result = typedArray.CreateSpeciesDefault(length);
        for (var i = 0; i < length; i++)
        {
            var element = i == actualIndex ? value : typedArray.GetValueForIndex(i);
            result.SetValue(i, element);
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

        if (constructorValue.IsUndefined)
        {
            return CreateDefaultTypedArray(exemplar, length);
        }

        if (!constructorValue.IsObject)
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: Realm);
        }

        if (constructorValue.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor))
        {
            if (!ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
            {
                speciesValue = JsValue.Undefined;
            }

            if (speciesValue.IsNullOrUndefined)
            {
                return CreateDefaultTypedArray(exemplar, length);
            }

            constructorValue = speciesValue;
        }

        if (!JsOps.IsConstructor(constructorValue) || !constructorValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: Realm);
        }

        if (Realm is null)
        {
            throw new InvalidOperationException("Realm is required for TypedArray species construction.");
        }

        var constructed = ReflectHelper.Construct(callable, [JsValue.FromNumber((double)length)], callable, Realm);
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

    [JsHostMethod("set", Length = 1d)]
    private JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.set");

        if (args.Count == 0)
        {
            return JsValue.Undefined;
        }

        var source = args[0];

        // Branch: TypedArray source (22.2.3.23.2)
        if (source.TryGetObject<TypedArrayBase>(out var sourceTypedArray))
        {
            return SetFromTypedArray(typedArray, sourceTypedArray, args);
        }

        // Branch: Array-arg source (22.2.3.23.1)
        return SetFromArrayLike(typedArray, source, args);
    }

    private JsValue SetFromTypedArray(TypedArrayBase target, TypedArrayBase source, IReadOnlyList<JsValue> args)
    {
        // 1. Let targetOffset be ? ToIntegerOrInfinity(offset).
        var ctx = Realm.CreateContext();
        var offsetNumber = args.Count > 1 && !args[1].IsUndefined
            ? ToIntegerOrInfinity(args[1], ctx)
            : 0d;

        // 2. If targetOffset < 0, throw a RangeError.
        if (offsetNumber < 0 || double.IsPositiveInfinity(offsetNumber))
        {
            throw ThrowRangeError("offset is out of bounds", realm: Realm);
        }

        // 3. If source.[[IsDetachedBuffer]] is true, throw a TypeError.
        if (source.IsDetachedOrOutOfBounds())
        {
            throw source.CreateOutOfBoundsTypeError();
        }

        // 4. If target.[[IsDetachedBuffer]] is true, throw a TypeError.
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        var offset = (int)offsetNumber;

        // 5-9. Validate range: srcLength + targetOffset > targetLength
        if ((long)offset + source.Length > target.Length)
        {
            throw ThrowRangeError("offset is out of bounds", realm: Realm);
        }

        target.Set(source, offset);
        return JsValue.Undefined;
    }

    private JsValue SetFromArrayLike(TypedArrayBase target, JsValue source, IReadOnlyList<JsValue> args)
    {
        // 1. Let targetOffset be ? ToIntegerOrInfinity(offset).
        var ctx = Realm.CreateContext();
        var offsetNumber = args.Count > 1 && !args[1].IsUndefined
            ? ToIntegerOrInfinity(args[1], ctx)
            : 0d;

        // 2. If targetOffset < 0, throw a RangeError.
        if (offsetNumber < 0 || double.IsPositiveInfinity(offsetNumber))
        {
            throw ThrowRangeError("offset is out of bounds", realm: Realm);
        }

        // 3. If target.[[IsDetachedBuffer]] is true, throw a TypeError.
        if (target.IsDetachedOrOutOfBounds())
        {
            throw target.CreateOutOfBoundsTypeError();
        }

        // Per spec step 15: Let src be ? ToObject(array).
        // ToObject(undefined) and ToObject(null) throw TypeError.
        if (source.IsUndefined || source.IsNull)
        {
            throw ThrowTypeError("Cannot convert undefined or null to object", realm: Realm);
        }

        var offset = (int)offsetNumber;

        // 4. Let src be ? ToObject(source).
        // For JsArray, use directly; for primitives, convert to wrapper object.
        if (source.TryGetObject<JsArray>(out var sourceArray))
        {
            // Fast path for JsArray
            var srcLen = (int)sourceArray.Length;
            if (offset + srcLen > target.Length)
            {
                throw ThrowRangeError("offset is out of bounds", realm: Realm);
            }

            for (var i = 0; i < srcLen; i++)
            {
                var value = sourceArray.GetElement(i);
                if (target.IsDetachedOrOutOfBounds())
                {
                    throw target.CreateOutOfBoundsTypeError();
                }

                target.SetValue(offset + i, value);
            }

            return JsValue.Undefined;
        }

        // Handle array-like objects via IJsPropertyAccessor (ToObject path)
        if (source.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return SetFromAccessor(target, accessor, source, offset);
        }

        // Primitive values: convert to object wrapper (ToObject)
        // string => String object with indexed chars and length
        if (source.IsString)
        {
            var str = source.AsString() ?? string.Empty;
            var srcLen = str.Length;
            if (offset + srcLen > target.Length)
            {
                throw ThrowRangeError("offset is out of bounds", realm: Realm);
            }

            for (var i = 0; i < srcLen; i++)
            {
                var ch = new string(str[i], 1);
                var numValue = JsOps.ToNumber((JsValue)ch, ctx);
                if (ctx.IsThrow)
                {
                    throw new ThrowSignal(ctx.FlowValue);
                }

                if (target.IsDetachedOrOutOfBounds())
                {
                    throw target.CreateOutOfBoundsTypeError();
                }

                target.SetValue(offset + i, JsValue.FromDouble(numValue));
            }

            return JsValue.Undefined;
        }

        // For number, boolean, symbol, bigint primitives: ToObject produces wrapper with length 0
        // (no indexed properties), so nothing to set.
        return JsValue.Undefined;
    }

    private JsValue SetFromAccessor(TypedArrayBase target, IJsPropertyAccessor accessor, JsValue source, int offset)
    {
        // 5. Let srcLength be ? LengthOfArrayLike(src).
        if (!accessor.TryGetProperty("length", source, out var lengthVal))
        {
            lengthVal = JsValue.FromDouble(0);
        }

        var ctx = Realm.CreateContext();
        var srcLenNumber = JsOps.ToNumber(lengthVal, ctx);
        if (ctx.IsThrow)
        {
            throw new ThrowSignal(ctx.FlowValue);
        }

        var srcLen = double.IsNaN(srcLenNumber) || srcLenNumber < 0
            ? 0
            : (int)Math.Min(srcLenNumber, int.MaxValue);

        // 8. If srcLength + targetOffset > targetLength, throw a RangeError.
        if (srcLen + offset > target.Length)
        {
            throw ThrowRangeError("offset is out of bounds", realm: Realm);
        }

        // 9. Set each element
        for (var i = 0; i < srcLen; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture);
            if (!accessor.TryGetProperty(key, source, out var value))
            {
                value = JsValue.Undefined;
            }

            // ToNumber / ToBigInt conversion
            var numValue = JsOps.ToNumber(value, ctx);
            if (ctx.IsThrow)
            {
                throw new ThrowSignal(ctx.FlowValue);
            }

            if (target.IsDetachedOrOutOfBounds())
            {
                // Per spec: detached during iteration is not an error for array-arg
                return JsValue.Undefined;
            }

            if (offset + i < target.Length)
            {
                target.SetValue(offset + i, JsValue.FromDouble(numValue));
            }
        }

        return JsValue.Undefined;
    }

    [JsHostMethod("slice", Length = 2d)]
    private JsValue Slice(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.slice");
        if (typedArray.IsDetachedOrOutOfBounds())
        {
            throw typedArray.CreateOutOfBoundsTypeError();
        }

        var length = typedArray.Length;
        var startIndex = args.Count > 0 && !args[0].IsUndefined
            ? ToIntegerOrInfinity(args[0], Realm.CreateContext())
            : 0d;
        var endIndex = args.Count > 1 && !args[1].IsUndefined
            ? ToIntegerOrInfinity(args[1], Realm.CreateContext())
            : length;

        var start = ClampRelativeIndex(startIndex, length);
        var end = ClampRelativeIndex(endIndex, length);
        var count = Math.Max(end - start, 0);
        var result = SpeciesCreate(typedArray, count);

        for (var i = 0; i < count; i++)
        {
            if (typedArray.IsDetachedOrOutOfBounds())
            {
                throw typedArray.CreateOutOfBoundsTypeError();
            }

            var sourceIndex = start + i;
            if (sourceIndex >= typedArray.Length)
            {
                break;
            }

            result.SetValue(i, typedArray.GetValueForIndex(sourceIndex));
        }

        return JsValue.FromObjectUnsafe(result);
    }

    [JsHostMethod("subarray", Length = 2d)]
    private JsValue Subarray(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.subarray");

        // 3. Let buffer be O.[[ViewedArrayBuffer]].
        var buffer = typedArray.Buffer;

        // 4. Let srcLength be O.[[ArrayLength]] (use 0 if out of bounds per spec).
        var srcLength = typedArray.IsDetachedOrOutOfBounds() ? 0 : typedArray.Length;

        // 5. Let relativeBegin be ? ToIntegerOrInfinity(begin).
        var ctx = Realm.CreateContext();
        var relativeBegin = args.Count > 0 && !args[0].IsUndefined
            ? ToIntegerOrInfinity(args[0], ctx)
            : 0d;

        // 6. Let relativeEnd be ? ToIntegerOrInfinity(end).
        var relativeEnd = args.Count > 1 && !args[1].IsUndefined
            ? ToIntegerOrInfinity(args[1], ctx)
            : (double)srcLength;

        // 7. If relativeBegin is -Infinity, beginIndex = 0.
        //    If relativeBegin < 0, beginIndex = max(srcLength + relativeBegin, 0).
        //    Else, beginIndex = min(relativeBegin, srcLength).
        int beginIndex;
        if (double.IsNegativeInfinity(relativeBegin))
        {
            beginIndex = 0;
        }
        else if (relativeBegin < 0)
        {
            beginIndex = (int)Math.Max(srcLength + relativeBegin, 0);
        }
        else
        {
            beginIndex = (int)Math.Min(relativeBegin, srcLength);
        }

        // 8. If relativeEnd is -Infinity, endIndex = 0.
        //    If relativeEnd < 0, endIndex = max(srcLength + relativeEnd, 0).
        //    Else, endIndex = min(relativeEnd, srcLength).
        int endIndex;
        if (double.IsNegativeInfinity(relativeEnd))
        {
            endIndex = 0;
        }
        else if (relativeEnd < 0)
        {
            endIndex = (int)Math.Max(srcLength + relativeEnd, 0);
        }
        else
        {
            endIndex = (int)Math.Min(relativeEnd, srcLength);
        }

        var newLength = Math.Max(endIndex - beginIndex, 0);

        // 9. Let constructorName be the String value of O.[[TypedArrayName]].
        // 10. Let elementSize be the Element Size value specified in Table for constructorName.
        var elementSize = typedArray.BytesPerElement;

        // 11. Let srcByteOffset be O.[[ByteOffset]].
        var srcByteOffset = typedArray.ByteOffset;

        // 12. Let beginByteOffset be srcByteOffset + beginIndex * elementSize.
        var beginByteOffset = srcByteOffset + beginIndex * elementSize;

        // 13-17. Use TypedArraySpeciesCreate or Subarray with proper prototype.
        // Use SpeciesCreate to properly handle constructors and prototypes.
        var result = SubarraySpeciesCreate(typedArray, buffer, beginByteOffset, newLength);

        return JsValue.FromObjectUnsafe(result);
    }

    /// <summary>
    /// Creates a subarray via the species constructor pattern, properly preserving prototype chains.
    /// Per spec: TypedArraySpeciesCreate for subarray passes (buffer, byteOffset, length).
    /// </summary>
    private TypedArrayBase SubarraySpeciesCreate(TypedArrayBase exemplar, JsArrayBuffer buffer, int byteOffset, int length)
    {
        var constructorValue = JsValue.Undefined;

        if (exemplar.TryGetProperty("constructor", (JsValue)exemplar, out var ctorValue))
        {
            constructorValue = ctorValue;
        }

        if (constructorValue.IsUndefined)
        {
            return CreateSubarrayDefault(exemplar, buffer, byteOffset, length);
        }

        if (!constructorValue.IsObject)
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: Realm);
        }

        if (constructorValue.TryGetObject<IJsPropertyAccessor>(out var ctorAccessor))
        {
            if (!ctorAccessor.TryGetProperty(SymbolSpeciesKey, out var speciesValue))
            {
                speciesValue = JsValue.Undefined;
            }

            if (speciesValue.IsNullOrUndefined)
            {
                return CreateSubarrayDefault(exemplar, buffer, byteOffset, length);
            }

            constructorValue = speciesValue;
        }

        if (!JsOps.IsConstructor(constructorValue) || !constructorValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError("TypedArray species constructor must be a constructor", realm: Realm);
        }

        if (Realm is null)
        {
            throw new InvalidOperationException("Realm is required for TypedArray species construction.");
        }

        // Call species constructor with (buffer, byteOffset, length)
        var constructed = ReflectHelper.Construct(callable,
            [JsValue.FromObjectUnsafe(buffer), JsValue.FromDouble(byteOffset), JsValue.FromDouble(length)],
            callable, Realm);

        if (!constructed.TryGetObject<TypedArrayBase>(out var typedResult))
        {
            throw ThrowTypeError("TypedArray species constructor did not return a TypedArray instance", realm: Realm);
        }

        return typedResult;
    }

    private static TypedArrayBase CreateSubarrayDefault(TypedArrayBase exemplar, JsArrayBuffer buffer, int byteOffset, int length)
    {
        var result = exemplar.CreateSubarrayView(buffer, byteOffset, length);
        if (exemplar.Prototype is not null)
        {
            result.SetPrototype(exemplar.Prototype);
        }

        return result;
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

        // Read all values into a list (use original indices for stable sort)
        var values = new List<(JsValue Value, int Index)>(length);
        for (var i = 0; i < length; i++)
        {
            values.Add((typedArray.GetValueForIndex(i), i));
        }

        // Sort the values (stable, and allow comparefn exceptions to propagate)
        StableSort(values, Comparer);

        var currentLength = typedArray.Length;
        var writeLength = Math.Min(length, currentLength);
        for (var i = 0; i < writeLength; i++)
        {
            typedArray.SetValue(i, values[i].Value);
        }

        return (JsValue)typedArray;

        int Comparer((JsValue Value, int Index) left, (JsValue Value, int Index) right)
        {
            var result = CompareValues(left.Value, right.Value);
            if (result != 0)
            {
                return result;
            }

            return left.Index.CompareTo(right.Index);
        }

        int CompareValues(JsValue left, JsValue right)
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

            return CompareNumbers(JsOps.ToNumber(left), JsOps.ToNumber(right));
        }

        static int CompareNumbers(double leftNum, double rightNum)
        {
            if (double.IsNaN(leftNum))
            {
                return double.IsNaN(rightNum) ? 0 : 1;
            }

            if (double.IsNaN(rightNum))
            {
                return -1;
            }

            if (leftNum == 0 && rightNum == 0)
            {
                var leftNegZero = IsNegativeZero(leftNum);
                var rightNegZero = IsNegativeZero(rightNum);
                if (leftNegZero == rightNegZero)
                {
                    return 0;
                }

                return leftNegZero ? -1 : 1;
            }

            return leftNum.CompareTo(rightNum);
        }

        static bool IsNegativeZero(double value)
        {
            return value == 0 && BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(-0d);
        }

        static void StableSort(List<(JsValue Value, int Index)> items,
            Comparison<(JsValue Value, int Index)> comparer)
        {
            var count = items.Count;
            if (count <= 1)
            {
                return;
            }

            var src = items.ToArray();
            var dst = new (JsValue Value, int Index)[count];

            for (var width = 1; width < count; width *= 2)
            {
                for (var i = 0; i < count; i += 2 * width)
                {
                    var left = i;
                    var mid = Math.Min(i + width, count);
                    var right = Math.Min(i + 2 * width, count);
                    Merge(src, dst, left, mid, right, comparer);
                }

                var temp = src;
                src = dst;
                dst = temp;
            }

            for (var i = 0; i < count; i++)
            {
                items[i] = src[i];
            }
        }

        static void Merge((JsValue Value, int Index)[] src, (JsValue Value, int Index)[] dst,
            int left, int mid, int right, Comparison<(JsValue Value, int Index)> comparer)
        {
            var i = left;
            var j = mid;
            var k = left;

            while (i < mid && j < right)
            {
                if (comparer(src[i], src[j]) <= 0)
                {
                    dst[k++] = src[i++];
                }
                else
                {
                    dst[k++] = src[j++];
                }
            }

            while (i < mid)
            {
                dst[k++] = src[i++];
            }

            while (j < right)
            {
                dst[k++] = src[j++];
            }
        }
    }

    private JsValue JoinImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.join");

        var length = typedArray.Length;
        var separator = args.Count > 0 && !args[0].IsUndefined
            ? args[0].ToJsString()
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

            var element = typedArray.GetValueForIndex(i);
            if (!element.IsNullOrUndefined)
            {
                if (element.IsNumber)
                {
                    sb.Append(NumberHelper.NumberToString(element.AsDouble(), 10));
                }
                else if (element.IsBigInt && element.ObjectValue is JsBigInt bigInt)
                {
                    sb.Append(bigInt.ToString());
                }
                else
                {
                    sb.Append(JsOps.ToJsString(element));
                }
            }
        }

        return JsValue.FromString(sb.ToString());
    }

    private JsValue ToLocaleStringImpl(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var typedArray = ValidateReceiver(thisValue, "%TypedArray%.prototype.toLocaleString");

        var locales = args.Count > 0 ? args[0] : JsValue.Undefined;
        var options = args.Count > 1 ? args[1] : JsValue.Undefined;
        var length = typedArray.Length;
        if (length == 0)
        {
            return JsValue.FromString(string.Empty);
        }

        var parts = new List<string>(length);
        for (var i = 0; i < length; i++)
        {
            var element = typedArray.GetValueForIndex(i);
            if (element.IsNullOrUndefined)
            {
                parts.Add(string.Empty);
                continue;
            }

            string part;
            IJsPropertyAccessor? elementAccessor;
            if (element.TryGetObject<IJsPropertyAccessor>(out var objAccessor))
            {
                elementAccessor = objAccessor;
            }
            else
            {
                elementAccessor = GetPrimitivePrototype(element, Realm);
            }

            if (elementAccessor is not null &&
                elementAccessor.TryGetProperty("toLocaleString", element, out var toLocaleMethod) &&
                toLocaleMethod.TryGetObject<IJsCallable>(out var callable))
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

        return JsValue.FromString(string.Join(',', parts));
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

    #endregion
}
