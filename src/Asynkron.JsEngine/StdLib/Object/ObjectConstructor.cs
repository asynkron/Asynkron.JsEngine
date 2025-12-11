using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Object", PrototypeType = typeof(ObjectPrototype), Length = 1d, DisplayName = "Object")]
public sealed partial class ObjectConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue is JsObject { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, targetCtor);
            return ConstructCore(args, targetCtor, constructing);
        }

        return ConstructCore(args, targetCtor, null);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ObjectPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget as IJsCallable ?? target;
            return ConstructCore(args, newTargetCallable, null);
        });

        AttachStatics(constructor);
        AttachPrototypeShortcut(constructor);
    }

    private object ConstructCore(IReadOnlyList<object?> args, IJsCallable newTarget, JsObject? existing)
    {
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return CreateBlank(newTarget, existing);
        }

        var value = args[0];
        return value switch
        {
            TypedAstSymbol sym => CreateSymbolWrapper(sym, realm: Realm),
            JsBigInt bigInt => CreateBigIntWrapper(bigInt, realm: Realm),
            bool b => CreateBooleanWrapper(b, realm: Realm),
            string s => CreateStringWrapper(s, realm: Realm),
            double or float or decimal or int or uint or long or ulong or short or ushort or byte or sbyte =>
                CreateNumberWrapper(JsOps.ToNumber(value), realm: Realm),
            IJsPropertyAccessor accessor => accessor,
            _ => CreateBlank(newTarget, existing)
        };
    }

    private JsObject CreateBlank(IJsCallable newTarget, JsObject? existing)
    {
        var targetCtor = _constructor ?? newTarget;
        var obj = existing ?? PrepareThisObject(null, assignPrototype: false);
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        if (proto is not null && obj.Prototype is null)
        {
            obj.SetPrototype(proto);
        }

        obj.RealmState ??= Realm;
        return obj;
    }

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("defineProperties", ObjectDefineProperties, Realm);
        constructor.SetHostedProperty("setPrototypeOf", ObjectSetPrototypeOf, Realm);
        constructor.SetHostedProperty("preventExtensions", ObjectPreventExtensions, Realm);
        constructor.SetHostedProperty("isExtensible", ObjectIsExtensible, Realm);
        constructor.SetHostedProperty("getOwnPropertySymbols", ObjectGetOwnPropertySymbols, Realm);
        constructor.SetHostedProperty("keys", ObjectKeys, Realm);
        constructor.SetHostedProperty("values", ObjectValues, Realm);
        constructor.SetHostedProperty("entries", ObjectEntries, Realm);
        constructor.SetHostedProperty("assign", ObjectAssign, Realm);
        constructor.SetHostedProperty("fromEntries", ObjectFromEntries, Realm);
        constructor.SetHostedProperty("hasOwn", ObjectHasOwn, Realm);
        constructor.SetHostedProperty("freeze", ObjectFreeze, Realm);
        constructor.SetHostedProperty("seal", ObjectSeal, Realm);
        constructor.SetHostedProperty("isFrozen", ObjectIsFrozen, Realm);
        constructor.SetHostedProperty("isSealed", ObjectIsSealed, Realm);
        constructor.SetHostedProperty("is", ObjectIs, Realm);
        constructor.SetHostedProperty("create", ObjectCreate, Realm);
        constructor.SetHostedProperty("getOwnPropertyNames", ObjectGetOwnPropertyNames, Realm);
        constructor.SetHostedProperty("getOwnPropertyDescriptor", ObjectGetOwnPropertyDescriptor, Realm);
        constructor.SetHostedProperty("getOwnPropertyDescriptors", ObjectGetOwnPropertyDescriptors, Realm);
        constructor.SetHostedProperty("getPrototypeOf", ObjectGetPrototypeOf, Realm);
        constructor.SetHostedProperty("defineProperty", ObjectDefineProperty, Realm);
    }

    private void AttachPrototypeShortcut(HostFunction constructor)
    {
        if (Prototype.TryGetProperty("hasOwnProperty", out var hasOwn))
        {
            constructor.SetProperty("hasOwnProperty", hasOwn);
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Object constructor not initialized");
}
