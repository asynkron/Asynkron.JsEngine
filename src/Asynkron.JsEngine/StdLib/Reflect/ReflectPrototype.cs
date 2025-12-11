using System.Collections.Generic;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("Reflect", ToStringTag = "Reflect", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ReflectPrototype : JsPrototype
{
    [JsHostMethod("apply", Length = 3d)]
    public object? Apply(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectApply(thisValue, args, Realm);
    }

    [JsHostMethod("construct", Length = 2d)]
    public object? Construct(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectConstruct(thisValue, args, Realm);
    }

    [JsHostMethod("defineProperty", Length = 3d)]
    public object? DefineProperty(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectDefineProperty(thisValue, args, Realm);
    }

    [JsHostMethod("deleteProperty", Length = 2d)]
    public object? DeleteProperty(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectDeleteProperty(thisValue, args, Realm);
    }

    [JsHostMethod("get", Length = 2d)]
    public object? Get(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectGet(thisValue, args, Realm);
    }

    [JsHostMethod("getOwnPropertyDescriptor", Length = 2d)]
    public object? GetOwnPropertyDescriptor(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectGetOwnPropertyDescriptor(thisValue, args, Realm);
    }

    [JsHostMethod("getPrototypeOf", Length = 1d)]
    public object? GetPrototypeOf(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectGetPrototypeOf(thisValue, args, Realm);
    }

    [JsHostMethod("has", Length = 2d)]
    public object? Has(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectHas(thisValue, args, Realm);
    }

    [JsHostMethod("isExtensible", Length = 1d)]
    public object? IsExtensible(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectIsExtensible(thisValue, args, Realm);
    }

    [JsHostMethod("ownKeys", Length = 1d)]
    public object? OwnKeys(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectOwnKeys(thisValue, args, Realm);
    }

    [JsHostMethod("preventExtensions", Length = 1d)]
    public object? PreventExtensions(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectPreventExtensions(thisValue, args, Realm);
    }

    [JsHostMethod("set", Length = 3d)]
    public object? Set(object? thisValue, IReadOnlyList<object?> args)
    {
        return ReflectSet(thisValue, args, Realm);
    }

    [JsHostMethod("setPrototypeOf", Length = 2d)]
    public object? SetPrototypeOf(object? thisValue, IReadOnlyList<object?> args)
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
