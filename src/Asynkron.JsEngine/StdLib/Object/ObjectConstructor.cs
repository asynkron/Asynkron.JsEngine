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

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, targetCtor);
            return JsValue.FromObject(ConstructCore(args, targetCtor, constructing));
        }

        return JsValue.FromObject(ConstructCore(args, targetCtor, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ObjectPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            if (newTarget.TryGetObject<IJsCallable>(out var newTargetCallable))
            {
                return JsValue.FromObject(ConstructCore(args, newTargetCallable!, null));
            }
            return JsValue.FromObject(ConstructCore(args, target, null));
        });

        AttachStatics(constructor);
        AttachPrototypeShortcut(constructor);
    }

    private object ConstructCore(IReadOnlyList<JsValue> args, IJsCallable newTarget, JsObject? existing)
    {
        if (args.Count == 0 || args[0].IsUndefined || args[0].IsNull)
        {
            return CreateBlank(newTarget, existing);
        }

        var value = args[0];

        // Check if it's a TypedAstSymbol (stored in ObjectValue when Kind is Symbol)
        if (value.IsSymbol && value.ObjectValue is TypedAstSymbol typedSym)
        {
            return CreateSymbolWrapper(typedSym, realm: Realm);
        }
        if (value.TryGetBigInt(out var bigInt))
        {
            return CreateBigIntWrapper(bigInt!, realm: Realm);
        }
        if (value.TryGetBoolean(out var boolValue))
        {
            return CreateBooleanWrapper(boolValue, realm: Realm);
        }
        if (value.TryGetString(out var strValue))
        {
            return CreateStringWrapper(strValue!, realm: Realm);
        }
        if (value.TryGetDouble(out var numValue))
        {
            return CreateNumberWrapper(numValue, realm: Realm);
        }
        if (value.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            return accessor!;
        }

        return CreateBlank(newTarget, existing);
    }

    private JsObject CreateBlank(IJsCallable newTarget, JsObject? existing)
    {
        var targetCtor = _constructor ?? newTarget;
        var obj = existing ?? PrepareThisObject(JsValue.Undefined, assignPrototype: false);
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
        constructor.SetHostedProperty("defineProperties", (thisArg, args, realm) => ObjectDefineProperties(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("setPrototypeOf", (thisArg, args, realm) => ObjectSetPrototypeOf(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("preventExtensions", (thisArg, args, realm) => ObjectPreventExtensions(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("isExtensible", (thisArg, args, realm) => ObjectIsExtensible(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("getOwnPropertySymbols", (thisArg, args, realm) => ObjectGetOwnPropertySymbols(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("keys", (thisArg, args, realm) => ObjectKeys(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("values", (thisArg, args, realm) => ObjectValues(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("entries", (thisArg, args, realm) => ObjectEntries(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("assign", (thisArg, args, realm) => ObjectAssign(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("fromEntries", (thisArg, args, realm) => ObjectFromEntries(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("hasOwn", (thisArg, args, realm) => ObjectHasOwn(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("freeze", (thisArg, args, realm) => ObjectFreeze(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("seal", (thisArg, args, realm) => ObjectSeal(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("isFrozen", (thisArg, args, realm) => ObjectIsFrozen(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("isSealed", (thisArg, args, realm) => ObjectIsSealed(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("is", (thisArg, args, realm) => ObjectIs(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("create", (thisArg, args, realm) => ObjectCreate(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("getOwnPropertyNames", (thisArg, args, realm) => ObjectGetOwnPropertyNames(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("getOwnPropertyDescriptor", (thisArg, args, realm) => ObjectGetOwnPropertyDescriptor(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("getOwnPropertyDescriptors", (thisArg, args, realm) => ObjectGetOwnPropertyDescriptors(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("getPrototypeOf", (thisArg, args, realm) => ObjectGetPrototypeOf(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
        constructor.SetHostedProperty("defineProperty", (thisArg, args, realm) => ObjectDefineProperty(thisArg, (IReadOnlyList<JsValue>)args.Select(JsValue.FromObject).ToList(), realm), Realm);
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
