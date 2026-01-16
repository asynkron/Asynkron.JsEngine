#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;
using static Asynkron.JsEngine.StdLib.JsArrayConstants;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    internal static bool IsTruthy(JsValue value)
    {
        return JsOps.ToBoolean(in value);
    }

    internal static bool AreStrictlyEqual(JsValue left, JsValue right)
    {
        return JsOps.StrictEquals(left, right);
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

    internal static IJsObjectLike CreateCopyArray(long length, RealmState? realm, string methodName)
    {
        // Change-array-by-copy methods ignore @@species and always create a fresh Array.
        if (length > MaxConcreteArrayLength)
        {
            throw ThrowRangeError($"{methodName} result exceeds 2^32 - 1 elements", realm: realm);
        }

        var array = new JsArray(realm);
        array.SetProperty("length", (double)Math.Max(length, 0));
        return array;
    }

    internal static long LengthOfArrayLike(IJsPropertyAccessor accessor, RealmState? realm)
    {
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

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
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
            number = JsOps.ToNumber(value);
        }

        if (double.IsNaN(number) || number <= 0)
        {
            return 0;
        }

        var truncated = Math.Floor(number);
        return truncated > MaxArrayLength ? MaxArrayLength : truncated;
    }

    internal static double ToLengthOrZero(JsValue value, EvaluationContext? context = null)
    {
        var number = JsOps.ToNumber(value, context);
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

    /// <summary>
    /// JsValue overload for ToIntegerOrInfinity that avoids boxing.
    /// </summary>
    internal static double ToIntegerOrInfinity(JsValue value, EvaluationContext? context = null)
    {
        double number;
        if (value.Kind == JsValueKind.Number)
        {
            number = value.NumberValue;
        }
        else
        {
            number = JsOps.ToNumber(value, context);
            if (context?.IsThrow == true)
            {
                throw new ThrowSignal(context.FlowValue);
            }
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

        if (args.Count == 0 || !args[0].TryGetObject<IJsCallable>(out var callback))
        {
            throw ThrowTypeError($"{methodName} requires a callable accumulator", realm: realm);
        }

        if (callback is IJsEnvironmentAwareCallable envAware && realm?.Engine?.GlobalEnvironment is { } globalEnv)
        {
            envAware.CallingJsEnvironment = globalEnv;
        }

        var hasInitial = args.Count > 1;
        var accumulator = hasInitial ? args[1] : JsValue.Undefined;
        var start = fromRight ? length - 1 : 0;
        var step = fromRight ? -1 : 1;

        var k = start;
        var accumulatorSet = hasInitial;
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);
        while (k >= 0 && k < length)
        {
            if (TryGetExistingElement(accessor, k, out var value))
            {
                if (!accumulatorSet)
                {
                    // No initialValue provided: first present element becomes accumulator
                    accumulator = value;
                    accumulatorSet = true;
                }
                else
                {
                    accumulator =
                        callback.Invoke(
                            [accumulator, value, new JsValue((double)k), accessorJsValue],
                            JsValue.Undefined);
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
        // Cache accessor JsValue once before loop - FromObjectUnsafe uses IAsJsValue.AsJsValue if available
        var accessorJsValue = JsValue.FromObjectUnsafe(accessor);

        for (long k = 0; k < length; k++)
        {
            if (!TryGetExistingElement(accessor, k, out var value))
            {
                continue;
            }

            var result = callback.Invoke([value, new JsValue((double)k), accessorJsValue], thisArg);
            if (result.IsTruthy)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool SameValueZero(JsValue x, JsValue y)
    {
        if (x.IsNumber && y.IsNumber && double.IsNaN(x.AsDouble()) && double.IsNaN(y.AsDouble()))
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

        // Handle primitives that need boxing by using the JsValue-aware TryGetObject
        if (TryGetObject(thisValue, realm, out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} called on incompatible receiver", realm: realm);
    }

    internal static bool TryGetCallableMethod(JsValue target, string propertyKey, string operation, RealmState? realm,
        out IJsCallable? callable)
    {
        callable = null;
        if (target.IsNullOrUndefined)
        {
            return false;
        }

        var accessor = ToPropertyAccessor(target, operation, realm);
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

    internal static IJsPropertyAccessor ToPropertyAccessor(JsValue value, string methodName, RealmState? realm)
    {
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        if (value.IsNullOrUndefined)
        {
            throw ThrowTypeError($"{methodName} called on null or undefined", realm: realm);
        }

        if (TryGetObject(value, realm, out var boxed))
        {
            return boxed;
        }

        throw ThrowTypeError($"{methodName} could not convert the source to an object", realm: realm);
    }

    internal static IJsPropertyAccessor ToObjectPropertyAccessor(JsValue value, string methodName, RealmState? realm)
    {
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor;
        }

        if (value.IsNullOrUndefined)
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

    /// <summary>
    /// JsValue overload to avoid boxing. Prefer this over the object? overload.
    /// </summary>
    internal static void CreateDataPropertyOrThrowJsValue(IJsObjectLike target, string propertyKey, JsValue value,
        RealmState? realm, string methodName)
    {
        var descriptor = new PropertyDescriptor
        {
            JsValue = value,
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

    internal static void SetPropertyOrThrow(IJsPropertyAccessor accessor, string propertyKey, JsValue value,
        RealmState? realm, string methodName)
    {
        if (accessor is not JsProxy && accessor is IJsObjectLike objectLike)
        {
            var descriptor = objectLike.GetOwnPropertyDescriptor(propertyKey);
            if (descriptor is not null)
            {
                if (descriptor.IsAccessorDescriptor)
                {
                    if (descriptor.Set is null)
                    {
                        throw ThrowTypeError($"{methodName} could not set property '{propertyKey}'", realm: realm);
                    }

                    descriptor.Set.Invoke(new SingleValueArgs(value), JsValue.FromObjectUnsafe(objectLike));
                    return;
                }

                if (!descriptor.Writable)
                {
                    throw ThrowTypeError($"{methodName} could not set property '{propertyKey}'", realm: realm);
                }
            }
            else if (objectLike is IExtensibilityControl { IsExtensible: false } &&
                     !HasProperty(accessor, propertyKey))
            {
                throw ThrowTypeError($"{methodName} could not set property '{propertyKey}'", realm: realm);
            }
        }

        accessor.SetProperty(propertyKey, value);
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

    internal static object? InvokeDefaultObjectToString(object? target, RealmState? _)
    {
        // ES spec: Use the intrinsic %Object.prototype.toString%, not the user-modifiable version
        // This ensures correct behavior even if Object.prototype.toString has been deleted/modified
        return ObjectPrototype.IntrinsicToString(JsValue.FromObjectUnsafe(target));
    }
}
