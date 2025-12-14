using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("BigInt", PrototypeType = typeof(BigIntPrototype), Length = 1d, DisplayName = "BigInt")]
public sealed partial class BigIntConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        _ = thisValue;
        _ = args;
        throw ThrowTypeError("BigInt is not a constructor", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.BigIntPrototype ??= Prototype as JsObject;

        constructor.DisallowConstruct = true;
        constructor.ConstructErrorMessage = "BigInt is not a constructor";
        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget is not null)
            {
                throw ThrowTypeError("BigInt is not a constructor", realm: Realm);
            }

            return InvokeBigInt(args);
        });

        AttachStatics(constructor);
    }

    private JsValue InvokeBigInt(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: Realm);
        }

        return new JsValue(ToBigInt(args[0].ToObject(), realmState: Realm));
    }

    private void AttachStatics(HostFunction constructor)
    {
        DefineBuiltinFunction(constructor.PropertiesObject, "asIntN", new HostFunction(BigIntAsIntN, Realm), 2);
        DefineBuiltinFunction(constructor.PropertiesObject, "asUintN", new HostFunction(BigIntAsUintN, Realm), 2);
    }

    private object BigIntAsIntN(IReadOnlyList<JsValue> args)
    {
        if (args.Count < 2)
        {
            throw ThrowTypeError("BigInt.asIntN requires bits and value", realm: Realm);
        }

        var bits = ToIndex(args[0], Realm);
        var value = args[1];
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: Realm);
        }

        var bigIntValue = ToBigInt(value, realmState: Realm);
        return new JsBigInt(AsIntN(bits, bigIntValue.Value));
    }

    private object BigIntAsUintN(IReadOnlyList<JsValue> args)
    {
        if (args.Count < 2)
        {
            throw ThrowTypeError("BigInt.asUintN requires bits and value", realm: Realm);
        }

        var bits = ToIndex(args[0], Realm);
        var value = args[1];
        if (ReferenceEquals(value, Symbol.Undefined))
        {
            throw ThrowTypeError("Cannot convert undefined to a BigInt", realm: Realm);
        }

        var bigIntValue = ToBigInt(value, realmState: Realm);
        return new JsBigInt(AsUintN(bits, bigIntValue.Value));
    }
}
