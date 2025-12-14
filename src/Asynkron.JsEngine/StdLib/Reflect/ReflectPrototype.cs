using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Reflect", ToStringTag = "Reflect", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ReflectPrototype : JsPrototype
{
    [JsHostMethod("apply", Length = 3d)]
    public object? Apply(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectApply(thisValue, args, Realm);
    }

    [JsHostMethod("construct", Length = 2d)]
    public object? Construct(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectConstruct(thisValue, args, Realm);
    }

    [JsHostMethod("defineProperty", Length = 3d)]
    public object? DefineProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectDefineProperty(thisValue, args, Realm);
    }

    [JsHostMethod("deleteProperty", Length = 2d)]
    public object? DeleteProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectDeleteProperty(thisValue, args, Realm);
    }

    [JsHostMethod("get", Length = 2d)]
    public object? Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGet(thisValue, args, Realm);
    }

    [JsHostMethod("getOwnPropertyDescriptor", Length = 2d)]
    public object? GetOwnPropertyDescriptor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGetOwnPropertyDescriptor(thisValue, args, Realm);
    }

    [JsHostMethod("getPrototypeOf", Length = 1d)]
    public object? GetPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGetPrototypeOf(thisValue, args, Realm);
    }

    [JsHostMethod("has", Length = 2d)]
    public object? Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectHas(thisValue, args, Realm);
    }

    [JsHostMethod("isExtensible", Length = 1d)]
    public object? IsExtensible(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectIsExtensible(thisValue, args, Realm);
    }

    [JsHostMethod("ownKeys", Length = 1d)]
    public object? OwnKeys(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectOwnKeys(thisValue, args, Realm);
    }

    [JsHostMethod("preventExtensions", Length = 1d)]
    public object? PreventExtensions(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectPreventExtensions(thisValue, args, Realm);
    }

    [JsHostMethod("set", Length = 3d)]
    public object? Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectSet(thisValue, args, Realm);
    }

    [JsHostMethod("setPrototypeOf", Length = 2d)]
    public object? SetPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectSetPrototypeOf(thisValue, args, Realm);
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }
    }
}
