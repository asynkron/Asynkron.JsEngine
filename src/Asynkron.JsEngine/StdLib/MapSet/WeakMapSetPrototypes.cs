using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakMap", ToStringTag = "WeakMap")]
public sealed partial class WeakMapPrototype
{
    [JsHostMethod("set", Length = 2d)]
    public JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireWeakMap(thisValue);
        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        try
        {
            return (JsValue)map.Set(key, value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    [JsHostMethod("get", Length = 1d)]
    public JsValue Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireWeakMap(thisValue);
        return JsValue.FromObject(map.Get(args.GetArgument(0)));
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireWeakMap(thisValue);
        return new JsValue(map.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireWeakMap(thisValue);
        return new JsValue(map.Delete(args.GetArgument(0)));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakMapPrototype ??= Prototype as JsObject;
    }

    private JsWeakMap RequireWeakMap(JsValue receiver)
    {
        if (receiver.ToObject() is JsWeakMap weakMap)
        {
            return weakMap;
        }

        throw ThrowTypeError("WeakMap method called on incompatible receiver", realm: Realm);
    }
}

[JsPrototype("WeakSet", ToStringTag = "WeakSet")]
public sealed partial class WeakSetPrototype
{
    [JsHostMethod("add", Length = 1d)]
    public JsValue Add(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireWeakSet(thisValue);
        var value = args.GetArgument(0);
        try
        {
            return (JsValue)set.Add(value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireWeakSet(thisValue);
        return new JsValue(set.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var set = RequireWeakSet(thisValue);
        return new JsValue(set.Delete(args.GetArgument(0)));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakSetPrototype ??= Prototype as JsObject;
    }

    private JsWeakSet RequireWeakSet(JsValue receiver)
    {
        if (receiver.ToObject() is JsWeakSet weakSet)
        {
            return weakSet;
        }

        throw ThrowTypeError("WeakSet method called on incompatible receiver", realm: Realm);
    }
}
