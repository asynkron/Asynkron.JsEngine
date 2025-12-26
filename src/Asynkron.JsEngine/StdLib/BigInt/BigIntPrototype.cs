#region

using System.Numerics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("BigInt", ToStringTag = "BigInt")]
public sealed partial class BigIntPrototype : JsPrototype
{
    /* FLAKY */
    [JsHostMethod("toString", Length = 0d)]
    public JsValue ToString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = RequireBigIntValue(thisValue);
        var radixArg = args.GetArgument(0);
        double radixNumber;
        if (radixArg.IsUndefined)
        {
            radixNumber = 10d;
        }
        else if (radixArg.TryGetBigInt(out var biRadix))
        {
            radixNumber = (double)biRadix!.Value;
        }
        else
        {
            radixNumber = JsOps.ToNumber(radixArg);
        }

        if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
        {
            throw ThrowRangeError("Invalid radix", realm: Realm);
        }

        var intRadix = (int)radixNumber;
        if (intRadix is < 2 or > 36)
        {
            throw ThrowRangeError("toString() radix argument must be between 2 and 36", realm: Realm);
        }

        return new JsValue(BigIntToString(value.Value, intRadix));
    }

    /* FLAKY */
    [JsHostMethod("valueOf", Length = 0d)]
    public JsValue ValueOf(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        return new JsValue(RequireBigIntValue(thisValue));
    }

    /* FLAKY */
    [JsHostMethod("toLocaleString", Length = 0d)]
    public JsValue ToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var value = RequireBigIntValue(thisValue);
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        if (TryFormatWithIntlNumberFormatJsValue(new JsValue(value), localesArg, optionsArg, Realm, out var formatted))
        {
            return formatted;
        }

        return new JsValue(BigIntToString(value.Value, 10));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        if (Prototype is IJsPropertyAccessor accessor && !accessor.TryGetProperty("__value__", out _))
        {
            accessor.SetProperty("__value__", new JsBigInt(BigInteger.Zero));
        }

        Realm.BigIntPrototype ??= Prototype as JsObject;
    }

    private JsBigInt RequireBigIntValue(JsValue receiver)
    {
        // Fast path for direct BigInt
        if (receiver is { Kind: JsValueKind.BigInt, ObjectValue: JsBigInt directBi })
        {
            return directBi;
        }

        if (receiver.Kind != JsValueKind.Object)
        {
            throw ThrowTypeError("BigInt.prototype method called on incompatible receiver", realm: Realm);
        }

        return receiver.ObjectValue switch
        {
            JsBigInt bi => bi,
            JsObject obj when obj.TryGetValue("__value__", out var inner) && inner is JsBigInt wrapped => wrapped,
            IJsPropertyAccessor accessor when accessor.TryGetProperty("__value__", out var slot) &&
                                              slot.TryGetObject<JsBigInt>(out var wrapped) => wrapped,
            _ => throw ThrowTypeError("BigInt.prototype method called on incompatible receiver", realm: Realm)
        };
    }

    private static string BigIntToString(BigInteger value, int radix)
    {
        if (radix is < 2 or > 36)
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
}
