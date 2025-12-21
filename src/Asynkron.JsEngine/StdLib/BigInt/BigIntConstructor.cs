using System.Numerics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("BigInt", PrototypeType = typeof(BigIntPrototype), Length = 1d, DisplayName = "BigInt")]
public sealed partial class BigIntConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    // Static methods registered via code generation

    [JsConstructorMethod("asIntN", Length = 2d)]
    public static JsValue AsIntN(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (args.Count < 2)
        {
            throw ThrowTypeError("BigInt.asIntN requires bits and value", realm: realm);
        }

        var bits = ToIndex(args[0], realm);
        var value = args[1];
        if (value.IsUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: realm);
        }

        var bigIntValue = ToBigInt(value, realmState: realm);

        // Inline AsIntN implementation
        if (bits == 0)
        {
            return new JsValue(new JsBigInt(BigInteger.Zero));
        }

        var modulus = BigInteger.One << bits;
        var unsigned = bigIntValue.Value % modulus;
        if (unsigned.Sign < 0)
        {
            unsigned += modulus;
        }

        var threshold = modulus >> 1;
        var result = unsigned >= threshold ? unsigned - modulus : unsigned;
        return new JsValue(new JsBigInt(result));
    }

    [JsConstructorMethod("asUintN", Length = 2d)]
    public static JsValue AsUintN(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (args.Count < 2)
        {
            throw ThrowTypeError("BigInt.asUintN requires bits and value", realm: realm);
        }

        var bits = ToIndex(args[0], realm);
        var value = args[1];
        if (value.IsUndefined)
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: realm);
        }

        var bigIntValue = ToBigInt(value, realmState: realm);

        // Inline AsUintN implementation
        if (bits == 0)
        {
            return new JsValue(new JsBigInt(BigInteger.Zero));
        }

        var modulus = BigInteger.One << bits;
        var result = bigIntValue.Value % modulus;
        if (result.Sign < 0)
        {
            result += modulus;
        }

        return new JsValue(new JsBigInt(result));
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("BigInt is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        Realm.BigIntPrototype ??= Prototype as JsObject;

        constructor.DisallowConstruct = true;
        constructor.ConstructErrorMessage = "BigInt is not a constructor";
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (!newTarget.IsUndefined)
            {
                throw ThrowTypeError("BigInt is not a constructor", realm: Realm);
            }

            return InvokeBigInt(args);
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
    }

    private JsValue InvokeBigInt(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: Realm);
        }

        return new JsValue(ToBigInt(args[0], realmState: Realm));
    }
}
