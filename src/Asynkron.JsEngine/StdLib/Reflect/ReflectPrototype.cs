using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Reflect", ToStringTag = "Reflect", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ReflectPrototype : JsPrototype
{
    [JsHostMethod("apply", Length = 3d)]
    public JsValue Apply(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectApply(thisValue, args);
    }

    [JsHostMethod("construct", Length = 2d)]
    public JsValue Construct(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectConstruct(thisValue, args, Realm);
    }

    [JsHostMethod("defineProperty", Length = 3d)]
    public JsValue DefineProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectDefineProperty(thisValue, args, Realm);
    }

    [JsHostMethod("deleteProperty", Length = 2d)]
    public JsValue DeleteProperty(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectDeleteProperty(thisValue, args, Realm);
    }

    [JsHostMethod("get", Length = 2d)]
    public JsValue Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGet(thisValue, args, Realm);
    }

    [JsHostMethod("getOwnPropertyDescriptor", Length = 2d)]
    public JsValue GetOwnPropertyDescriptor(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGetOwnPropertyDescriptor(thisValue, args, Realm);
    }

    [JsHostMethod("getPrototypeOf", Length = 1d)]
    public JsValue GetPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectGetPrototypeOf(thisValue, args, Realm);
    }

    [JsHostMethod("has", Length = 2d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectHas(thisValue, args, Realm);
    }

    [JsHostMethod("isExtensible", Length = 1d)]
    public JsValue IsExtensible(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectIsExtensible(thisValue, args, Realm);
    }

    [JsHostMethod("ownKeys", Length = 1d)]
    public JsValue OwnKeys(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectOwnKeys(thisValue, args, Realm);
    }

    [JsHostMethod("preventExtensions", Length = 1d)]
    public JsValue PreventExtensions(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectPreventExtensions(thisValue, args, Realm);
    }

    [JsHostMethod("set", Length = 3d)]
    public JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ReflectSet(thisValue, args, Realm);
    }

    [JsHostMethod("setPrototypeOf", Length = 2d)]
    public JsValue SetPrototypeOf(JsValue thisValue, IReadOnlyList<JsValue> args)
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
