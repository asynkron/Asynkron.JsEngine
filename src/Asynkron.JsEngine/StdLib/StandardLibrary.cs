using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

/// <summary>
///     Provides standard JavaScript library objects and functions (Math, JSON, etc.)
/// </summary>
public static partial class StandardLibrary
{
    private static readonly BigInteger BigInt64Modulus = BigInteger.One << 64;
    private static readonly BigInteger BigInt64SignThreshold = BigInt64Modulus >> 1;

    internal static object CreateTypeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.TypeErrorConstructor is IJsCallable callable)
        {
            var result = callable.Invoke([new JsValue(message)], JsValue.Null);
            if (result.IsUndefined)
            {
                return CreateErrorFallback("TypeError", message, realm);
            }

            return result.ToObject()!;
        }

        return CreateErrorFallback("TypeError", message, realm);
    }

    internal static object CreateRangeError(string message, EvaluationContext? context = null, RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.RangeErrorConstructor is IJsCallable callable)
        {
            var result = callable.Invoke([new JsValue(message)], JsValue.Null);
            return result.IsUndefined ? CreateErrorFallback("RangeError", message, realm) : result.ToObject()!;
        }

        return CreateErrorFallback("RangeError", message, realm);
    }

    internal static object CreateReferenceError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.ReferenceErrorConstructor is IJsCallable callable)
        {
            var result = callable.Invoke([new JsValue(message)], JsValue.Null);
            return result.IsUndefined ? CreateErrorFallback("ReferenceError", message, realm) : result.ToObject()!;
        }

        return CreateErrorFallback("ReferenceError", message, realm);
    }

    internal static ThrowSignal ThrowTypeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateTypeError(message, context, realm));
    }

    internal static ThrowSignal ThrowRangeError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateRangeError(message, context, realm));
    }

    internal static ThrowSignal ThrowReferenceError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateReferenceError(message, context, realm));
    }

    internal static ThrowSignal ThrowSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        return new ThrowSignal(CreateSyntaxError(message, context, realm));
    }

    internal static void DefineConstantProperty(
        IJsPropertyAccessor target,
        string name,
        object? value,
        bool configurable = false)
    {
        var descriptor = new PropertyDescriptor
        {
            Value = value,
            Writable = false,
            Enumerable = false,
            Configurable = configurable,
            HasValue = true,
            HasWritable = true,
            HasEnumerable = true,
            HasConfigurable = true
        };

        if (target is IPropertyDefinitionHost definable && definable.TryDefineProperty(name, descriptor))
        {
            return;
        }

        if (target is IJsObjectLike objectLike)
        {
            objectLike.DefineProperty(name, descriptor);
            return;
        }

        target.SetProperty(name, JsValue.FromObject(value));
    }

    internal static void DefineBuiltinFunction(
        JsObject target,
        string name,
        HostFunction function,
        int length,
        bool isConstructor = false,
        bool writable = true,
        bool enumerable = false,
        bool configurable = true,
        bool stripPrototypeWhenNotConstructor = true)
    {
        function.IsConstructor = isConstructor;
        function.DefineProperty("length",
            new PropertyDescriptor
            {
                Value = (double)length, Writable = false, Enumerable = false, Configurable = true
            });
        function.DefineProperty("name",
            new PropertyDescriptor { Value = name, Writable = false, Enumerable = false, Configurable = true });

        if (!isConstructor && stripPrototypeWhenNotConstructor)
        {
            function.PropertiesObject.DeleteOwnProperty("prototype");
        }

        target.DefineProperty(name,
            new PropertyDescriptor
            {
                Value = function, Writable = writable, Enumerable = enumerable, Configurable = configurable
            });
    }

    internal static object CreateSyntaxError(string message, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        realm ??= context?.RealmState;
        if (realm?.SyntaxErrorConstructor is IJsCallable callable)
        {
            var result = callable.Invoke([new JsValue(message)], JsValue.Null);
            return result.IsUndefined ? CreateErrorFallback("SyntaxError", message, realm) : result.ToObject()!;
        }

        return CreateErrorFallback("SyntaxError", message, realm);
    }

    private static JsObject CreateErrorFallback(string name, string message, RealmState? realm)
    {
        var error = new JsObject { RealmState = realm };
        if (realm?.ErrorPrototype is not null)
        {
            error.SetPrototype(realm.ErrorPrototype);
        }

        error.SetProperty("name", JsValue.FromObject(name));
        error.SetProperty("message", JsValue.FromObject(message));
        return error;
    }

    internal static JsBigInt ToBigInt(object? value, EvaluationContext? context = null, RealmState? realmState = null)
    {
        realmState ??= context?.RealmState;
        var localContext = context ?? realmState?.CreateContext();

        while (true)
        {
            if (ReferenceEquals(value, Symbol.Undefined))
            {
                throw ThrowTypeError("Cannot convert undefined to a BigInt", localContext, realmState);
            }

            if (value is JsObject jsObj && jsObj.TryGetValue("__value__", out var inner))
            {
                if (ReferenceEquals(inner, value))
                {
                    throw ThrowTypeError("Cannot convert object to a BigInt", localContext, realmState);
                }

                value = inner;
                continue;
            }

            switch (value)
            {
                case JsValue jsValue:
                    value = jsValue.ToObject();
                    continue;
                case JsBigInt bigInt:
                    return bigInt;
                case JsObject or IJsPropertyAccessor:
                    value = JsOps.ToPrimitive(value, ToPrimitiveHint.Number, localContext);
                    if (localContext is not null && localContext.IsThrow)
                    {
                        throw new ThrowSignal(localContext.FlowValue);
                    }

                    continue;
                case double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte:
                    var numberValue = JsOps.ToNumber(value);
                    if (double.IsNaN(numberValue) || double.IsInfinity(numberValue) ||
                        Math.Floor(numberValue) != numberValue)
                    {
                        throw ThrowRangeError("Cannot convert a non-integer number to a BigInt", localContext,
                            realmState);
                    }

                    return new JsBigInt(new BigInteger(numberValue));
                case null:
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                case IIsHtmlDda:
                    throw ThrowTypeError("Cannot convert undefined to a BigInt", localContext, realmState);
                case bool flag:
                    return flag ? JsBigInt.One : JsBigInt.Zero;
                case string s:
                    return new JsBigInt(ParseBigIntString(s, localContext, realmState));
            }

            throw ThrowTypeError($"Cannot convert {value?.GetType().Name ?? "null"} to a BigInt", localContext,
                realmState);
        }
    }

    internal static long ToBigInt64(object? value, RealmState? realmState = null)
    {
        var bigInt = ToBigInt(value, realmState: realmState);
        return ToBigInt64(bigInt.Value);
    }

    internal static ulong ToBigUint64(object? value, RealmState? realmState = null)
    {
        var bigInt = ToBigInt(value, realmState: realmState);
        return ToBigUint64(bigInt.Value);
    }

    internal static long ToBigInt64(BigInteger value)
    {
        var wrapped = value % BigInt64Modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += BigInt64Modulus;
        }

        if (wrapped >= BigInt64SignThreshold)
        {
            wrapped -= BigInt64Modulus;
        }

        return (long)wrapped;
    }

    internal static ulong ToBigUint64(BigInteger value)
    {
        var wrapped = value % BigInt64Modulus;
        if (wrapped.Sign < 0)
        {
            wrapped += BigInt64Modulus;
        }

        return (ulong)wrapped;
    }

    private static BigInteger ParseBigIntString(string value, EvaluationContext? context = null,
        RealmState? realmState = null)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        if (text.EndsWith('n'))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        var sign = 1;
        if (text.StartsWith('+') || text.StartsWith('-'))
        {
            if (text[0] == '-')
            {
                sign = -1;
            }

            text = text[1..];
        }

        if (text.Length == 0)
        {
            return BigInteger.Zero;
        }

        var numberBase = 10;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 16;
            text = text[2..];
        }
        else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 2;
            text = text[2..];
        }
        else if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            numberBase = 8;
            text = text[2..];
        }
        else if (text.StartsWith("0") && text.Length > 1 && char.IsDigit(text[1]))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        // A sign is only permitted with decimal strings.
        if (sign < 0 && numberBase != 10)
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        // For decimal strings, reject any non-digit content.
        if (numberBase == 10)
        {
            foreach (var t in text)
            {
                if (t is < '0' or > '9')
                {
                    throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
                }
            }
        }

        if (text.Length == 0 || !TryParseBigIntWithBase(text, numberBase, sign, out var parsed))
        {
            throw ThrowSyntaxError("Invalid BigInt literal", context, realmState);
        }

        return parsed;
    }

    private static bool TryParseBigIntWithBase(string digits, int numberBase, int sign, out BigInteger result)
    {
        result = BigInteger.Zero;
        foreach (var ch in digits)
        {
            var digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'z' => ch - 'a' + 10,
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1
            };

            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            result = result * numberBase + digit;
        }

        if (sign < 0)
        {
            result = BigInteger.Negate(result);
        }

        return true;
    }

    /// <summary>
    ///     Converts a JavaScript value to its string representation, handling functions appropriately.
    ///     Exposed internally so prototype helpers can reuse the same semantics.
    /// </summary>
    internal static string JsValueToString(object? value, RealmState? realm = null)
    {
        return value.ToJsString(null, realm);
    }

    internal static bool TryFormatWithIntlNumberFormat(
        object numericValue,
        object? localesArg,
        object? optionsArg,
        RealmState? realm,
        out object? formatted)
    {
        formatted = null;
        var constructor = ResolveIntlNumberFormatConstructor(realm);
        if (constructor is null)
        {
            return false;
        }

        var formatter = constructor.Invoke([JsValue.FromObject(localesArg), JsValue.FromObject(optionsArg)], JsValue.Null);
        if (!formatter.TryGetObject<IJsPropertyAccessor>(out var accessor) || accessor is null ||
            !accessor.TryGetProperty("format", out var formatValue) ||
            !formatValue.TryGetObject<IJsCallable>(out var formatFn) || formatFn is null)
        {
            return false;
        }

        var result = formatFn.Invoke([JsValue.FromObject(numericValue)], formatter);
        formatted = result.ToObject();
        return true;
    }

    private static IJsCallable? ResolveIntlNumberFormatConstructor(RealmState? realm)
    {
        var intl = realm?.Engine?.GlobalObject;
        if (intl is null)
        {
            return null;
        }

        if (!intl.TryGetProperty("Intl", out var intlValue) || !intlValue.TryGetObject<IJsPropertyAccessor>(out var intlAccessor) || intlAccessor is null)
        {
            return null;
        }

        if (!intlAccessor.TryGetProperty("NumberFormat", out var ctorValue) || !ctorValue.TryGetObject<IJsCallable>(out var ctor) || ctor is null)
        {
            return null;
        }

        return ctor;
    }
}
