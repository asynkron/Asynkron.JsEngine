using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    internal static bool AreStrictlyEqual(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    internal static IJsObjectLike ToArrayLike(object? value, RealmState? realm)
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

    internal static int GetArrayLikeLength(IJsObjectLike obj)
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

    internal static void AttachBuiltinMetadata(HostFunction fn, string name, double length)
    {
        fn.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });
        fn.DefineProperty("length",
            new PropertyDescriptor { Value = length, Writable = false, Enumerable = false, Configurable = true });
    }

    internal static void SetArrayLikeLength(IJsPropertyAccessor target, long length)
    {
        target.SetProperty("length", (double)Math.Max(length, 0));
    }

    internal static long LengthOfArrayLike(object? target, RealmState? realm, string operation)
    {
        if (target is null || ReferenceEquals(target, Symbol.Undefined))
        {
            throw ThrowTypeError($"{operation} requires an object", realm: realm);
        }

        var accessor = target as IJsPropertyAccessor ?? ToPropertyAccessor(target, operation, realm);
        var context = realm?.CreateContext();
        var value = accessor.TryGetProperty("length", out var lengthValue) ? lengthValue : 0d;
        return (long)ToLengthOrZero(value, context);
    }

    internal static (IJsPropertyAccessor Accessor, long Length, IJsCallable Callback, JsValue ThisArg)
        PrepareArrayIteration(JsValue receiver, IReadOnlyList<JsValue> args, RealmState? realm, string methodName)
    {
        var accessor = EnsureArrayLikeReceiver(receiver, methodName, realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var context = realm?.CreateContext();
        var length = (long)ToLengthOrZero(lengthValue, context);

        if (args.Count == 0 || args[0].ToObject() is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} requires a callable callback", realm: realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var thisArg = args.GetArgument(1);
        return (accessor, length, callback, thisArg);
    }

    internal static string ToIndexString(long index)
    {
        return index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// JsValue overload that avoids boxing for the common case of numeric lengths.
    /// </summary>
    internal static double ToLengthOrZero(JsValue value)
    {
        // Fast path: if already a number, skip the conversion
        double number;
        if (value.Kind == JsValueKind.Number)
        {
            number = value.NumberValue;
        }
        else
        {
            // Fall back to object-based conversion for non-numbers
            number = JsOps.ToNumber(value.ToObject());
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLength ? MaxArrayLength : truncated;
    }

    internal static double ToLengthOrZero(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLength ? MaxArrayLength : truncated;
    }

    internal static double ToIntegerOrInfinity(object? value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumberWithContext(value, context);
        if (context?.IsThrow == true)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (double.IsNaN(number) || number == 0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(number) || double.IsNegativeInfinity(number))
        {
            return number;
        }

        return Math.Sign(number) * Math.Floor(Math.Abs(number));
    }

    internal static long ClampRelativeIndex(double index, long length)
    {
        if (double.IsNegativeInfinity(index))
        {
            return 0;
        }

        if (index < 0)
        {
            return Math.Max(length + (long)index, 0);
        }

        return Math.Min((long)index, length);
    }

    internal static object? ReduceLike(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm,
        string methodName, bool fromRight)
    {
        var accessor = EnsureArrayLikeReceiver(thisValue, methodName, realm);
        var lengthValue = accessor.TryGetProperty("length", out var lenVal) ? lenVal : 0d;
        var lengthContext = realm?.CreateContext();
        var length = (long)ToLengthOrZero(lengthValue, lengthContext);

        if (args.Count == 0 || args[0].ToObject() is not IJsCallable callback)
        {
            throw ThrowTypeError($"{methodName} requires a callable accumulator", realm: realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var hasInitial = args.Count > 1;
        var accumulator = hasInitial ? args[1].ToObject() : Symbol.Undefined;
        var start = fromRight ? length - 1 : 0;
        var step = fromRight ? -1 : 1;

        var k = start;
        var accumulatorSet = hasInitial;
        while (k >= 0 && k < length)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                if (!accumulatorSet)
                {
                    // No initialValue provided: first present element becomes accumulator
                    accumulator = value.ToObject();
                    accumulatorSet = true;
                }
                else
                {
                    accumulator = callback.Invoke([JsValue.FromObjectUnsafe(accumulator), value, new JsValue((double)k), JsValue.FromObjectUnsafe(accessor)], JsValue.Undefined).ToObject();
                }
            }

            k += step;
        }

        if (!accumulatorSet)
        {
            throw ThrowTypeError($"{methodName} requires at least one element", realm: realm);
        }

        return accumulator;
    }

    internal static object? SomeLike(JsValue thisValue, IReadOnlyList<JsValue> args, RealmState? realm,
        string methodName)
    {
        var (accessor, length, callback, thisArg) =
            PrepareArrayIteration(thisValue, args, realm, methodName);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var result = callback.Invoke([value, new JsValue((double)k), JsValue.FromObjectUnsafe(accessor)], thisArg).ToObject();
            if (IsTruthy(result))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool SameValueZero(object? x, object? y)
    {
        if (x is double.NaN && y is double.NaN)
        {
            return true;
        }

        return JsOps.StrictEquals(x, y);
    }

    /// <summary>
    /// JsValue overload that avoids boxing when the receiver is already a JsValue.
    /// </summary>
    internal static IJsPropertyAccessor EnsureArrayLikeReceiver(JsValue thisValue, string methodName, RealmState? realm)
    {
        if (thisValue.IsNullOrUndefined)
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (thisValue.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        // Fall back to object-based path for primitives that need boxing
        return EnsureArrayLikeReceiverObject(thisValue.ToObject(), methodName, realm);
    }

    internal static IJsPropertyAccessor EnsureArrayLikeReceiver(object? receiver, string methodName, RealmState? realm)
    {
        // Unwrap JsValue first - this path is for callers that pass object?
        if (receiver is JsValue jsValue)
        {
            return EnsureArrayLikeReceiver(jsValue, methodName, realm);
        }

        return EnsureArrayLikeReceiverObject(receiver, methodName, realm);
    }

    private static IJsPropertyAccessor EnsureArrayLikeReceiverObject(object? receiver, string methodName, RealmState? realm)
    {
        if (receiver is null || ReferenceEquals(receiver, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (receiver is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (receiver is JsObject jsObj)
        {
            return jsObj;
        }

        if (TryGetObject(receiver, realm, out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} receiver is not object-like", realm: realm);
    }

    internal static bool TryGetCallableMethod(object? target, string propertyKey, string operation, RealmState? realm,
        out IJsCallable? callable)
    {
        callable = null;
        if (target is null || ReferenceEquals(target, Symbol.Undefined))
        {
            return false;
        }

        var accessor = target as IJsPropertyAccessor ?? ToPropertyAccessor(target, operation, realm);
        if (!accessor.TryGetProperty(propertyKey, out var candidate) ||
            candidate.IsNullOrUndefined)
        {
            return false;
        }

        if (!candidate.TryGetObject<IJsCallable>(out var resolved))
        {
            throw ThrowTypeError($"{operation} expected a function", realm: realm);
        }

        callable = resolved;
        return true;
    }

    internal static IJsPropertyAccessor ToPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm, out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} could not convert the source to an object", realm: realm);
    }

    internal static IJsPropertyAccessor ToObjectPropertyAccessor(object? value, string methodName, RealmState? realm)
    {
        if (value is IJsPropertyAccessor accessor)
        {
            return accessor;
        }

        if (value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm, out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} called on non-object", realm: realm);
    }

    internal static void IteratorClose(IJsPropertyAccessor iterator, RealmState? realm, string operation)
    {
        if (!iterator.TryGetProperty("return", out var returnValue) ||
            returnValue.IsNullOrUndefined)
        {
            return;
        }

        if (!returnValue.TryGetObject<IJsCallable>(out var callable))
        {
            throw ThrowTypeError($"{operation} iterator return is not callable", realm: realm);
        }

        callable.Invoke([], JsValue.FromObjectUnsafe(iterator));
    }

    internal static void CreateDataPropertyOrThrow(IJsObjectLike target, string propertyKey, object? value,
        RealmState? realm, string methodName)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };

        if (target is IPropertyDefinitionHost definitionHost)
        {
            var defined = definitionHost.TryDefineProperty(propertyKey, descriptor);
            if (!defined)
            {
                throw ThrowTypeError($"{methodName} could not define property '{propertyKey}'", realm: realm);
            }

            return;
        }

        target.DefineProperty(propertyKey, descriptor);
    }

    internal static bool TryGetExistingElement(IJsPropertyAccessor accessor, long index, out JsValue value)
    {
        return TryGetExistingElement(accessor, ToIndexString(index), out value);
    }

    internal static bool TryGetExistingElement(IJsPropertyAccessor accessor, string propertyKey, out JsValue value)
    {
        if (!HasProperty(accessor, propertyKey))
        {
            value = JsValue.Undefined;
            return false;
        }

        value = accessor.TryGetProperty(propertyKey, out var obtained) ? obtained : JsValue.Undefined;
        return true;
    }

    internal static object? GetElementOrUndefined(IJsPropertyAccessor accessor, string propertyKey)
    {
        return accessor.TryGetProperty(propertyKey, out var value) ? value : Symbol.Undefined;
    }

    /// <summary>
    /// Returns the element at the given property key as JsValue, avoiding boxing.
    /// </summary>
    internal static JsValue GetElementOrUndefinedJsValue(IJsPropertyAccessor accessor, string propertyKey)
    {
        return accessor.TryGetProperty(propertyKey, out var value) ? value : JsValue.Undefined;
    }

    /// <summary>
    /// Returns the element at the given index as JsValue, avoiding boxing.
    /// </summary>
    internal static JsValue GetElementOrUndefinedJsValue(IJsPropertyAccessor accessor, uint index)
    {
        return accessor.TryGetProperty(ToIndexString(index), out var value) ? value : JsValue.Undefined;
    }

    internal static object? InvokeDefaultObjectToString(object? target, RealmState? realm)
    {
        if (realm?.ObjectPrototype is IJsPropertyAccessor objectPrototype &&
            objectPrototype.TryGetProperty("toString", out var toStringValue) &&
            toStringValue.TryGetObject<IJsCallable>(out var callable))
        {
            return callable.Invoke([], JsValue.FromObjectUnsafe(target)).ToObject();
        }

        return "[object Object]";
    }
}
