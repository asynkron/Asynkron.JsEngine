using System.Collections.Generic;
using System.Numerics;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("BigInt", ToStringTag = "BigInt")]
public sealed partial class BigIntPrototype : JsPrototype
{
    [JsHostMethod("toString", Length = 0d)]
    public object ToString(object? thisValue, IReadOnlyList<object?> args)
    {
        var value = RequireBigIntValue(thisValue, Realm);
        var radixArg = args.GetArgument(0);
        var radixNumber = ReferenceEquals(radixArg, Symbol.Undefined)
            ? 10d
            : radixArg is JsBigInt biRadix
                ? (double)biRadix.Value
                : JsOps.ToNumber(radixArg);

        if (double.IsNaN(radixNumber) || Math.Abs(radixNumber % 1) > double.Epsilon)
        {
            throw ThrowRangeError("Invalid radix", realm: Realm);
        }

        var intRadix = (int)radixNumber;
        if (intRadix is < 2 or > 36)
        {
            throw ThrowRangeError("toString() radix argument must be between 2 and 36", realm: Realm);
        }

        return BigIntToString(value.Value, intRadix);
    }

    [JsHostMethod("valueOf", Length = 0d)]
    public object ValueOf(object? thisValue, IReadOnlyList<object?> _)
    {
        return RequireBigIntValue(thisValue, Realm);
    }

    [JsHostMethod("toLocaleString", Length = 0d)]
    public object? ToLocaleString(object? thisValue, IReadOnlyList<object?> args)
    {
        var value = RequireBigIntValue(thisValue, Realm);
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        if (TryFormatWithIntlNumberFormat(value, localesArg, optionsArg, Realm, out var formatted))
        {
            return formatted;
        }

        return BigIntToString(value.Value, 10);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        if (Prototype is IJsPropertyAccessor accessor && !accessor.TryGetProperty("__value__", out _))
        {
            accessor.SetProperty("__value__", new JsBigInt(BigInteger.Zero));
        }

        Realm.BigIntPrototype ??= Prototype as JsObject;
    }
}
