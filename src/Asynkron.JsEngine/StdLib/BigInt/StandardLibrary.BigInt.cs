using System.Numerics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static IJsCallable CreateBigIntFunction(RealmState realm)
    {
        return BigIntConstructor.CreateConstructor(realm);
    }

    public static JsObject CreateBigIntWrapper(JsBigInt value, EvaluationContext? context = null,
        RealmState? realm = null)
    {
        var wrapper = new JsObject { ["__value__"] = value };

        var prototype = context?.RealmState?.BigIntPrototype ?? realm?.BigIntPrototype;
        if (prototype is not null)
        {
            wrapper.SetPrototype(prototype);
        }

        return wrapper;
    }

    internal static JsBigInt RequireBigIntValue(JsValue receiver, RealmState realm)
    {
        var candidate = receiver.ToObject();

        return candidate switch
        {
            JsBigInt bi => bi,
            JsObject obj when obj.TryGetValue("__value__", out var inner) && inner is JsBigInt wrapped => wrapped,
            IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var slot) &&
                                              slot.TryGetObject<JsBigInt>(out var wrapped) => wrapped,
            _ => throw ThrowTypeError("BigInt.prototype method called on incompatible receiver", realm: realm),
        };
    }

    internal static string BigIntToString(BigInteger value, int radix)
    {
        if (radix < 2 || radix > 36)
        {
            throw ThrowRangeError("radix must be between 2 and 36", realm: null);
        }

        if (value.IsZero)
        {
            return "0";
        }

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = "";
        var current = BigInteger.Abs(value);
        while (current > 0)
        {
            current = BigInteger.DivRem(current, radix, out var remainder);
            result = digits[(int)remainder] + result;
        }

        return value.Sign < 0 ? "-" + result : result;
    }

    internal static BigInteger AsIntN(int bits, BigInteger value)
    {
        if (bits == 0)
        {
            return BigInteger.Zero;
        }

        var modulus = BigInteger.One << bits;
        var unsigned = value % modulus;
        if (unsigned.Sign < 0)
        {
            unsigned += modulus;
        }

        var threshold = modulus >> 1;
        return unsigned >= threshold ? unsigned - modulus : unsigned;
    }

    internal static BigInteger AsUintN(int bits, BigInteger value)
    {
        if (bits == 0)
        {
            return BigInteger.Zero;
        }

        var modulus = BigInteger.One << bits;
        var result = value % modulus;
        if (result.Sign < 0)
        {
            result += modulus;
        }

        return result;
    }
}
