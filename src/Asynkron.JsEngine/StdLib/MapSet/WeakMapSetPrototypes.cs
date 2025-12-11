using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakMap", ToStringTag = "WeakMap")]
public sealed partial class WeakMapPrototype
{
    [JsHostMethod("set", Length = 2d)]
    public object? Set(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireWeakMap(thisValue);
        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        try
        {
            return map.Set(key, value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    [JsHostMethod("get", Length = 1d)]
    public object? Get(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireWeakMap(thisValue);
        return map.Get(args.GetArgument(0));
    }

    [JsHostMethod("has", Length = 1d)]
    public object Has(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireWeakMap(thisValue);
        return map.Has(args.GetArgument(0));
    }

    [JsHostMethod("delete", Length = 1d)]
    public object Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        var map = RequireWeakMap(thisValue);
        return map.Delete(args.GetArgument(0));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakMapPrototype ??= Prototype as JsObject;
    }

    private JsWeakMap RequireWeakMap(object? candidate)
    {
        if (candidate is JsWeakMap weakMap)
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
    public object? Add(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireWeakSet(thisValue);
        var value = args.GetArgument(0);
        try
        {
            return set.Add(value);
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    [JsHostMethod("has", Length = 1d)]
    public object Has(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireWeakSet(thisValue);
        return set.Has(args.GetArgument(0));
    }

    [JsHostMethod("delete", Length = 1d)]
    public object Delete(object? thisValue, IReadOnlyList<object?> args)
    {
        var set = RequireWeakSet(thisValue);
        return set.Delete(args.GetArgument(0));
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject jsObj && jsObj.RealmState is null)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakSetPrototype ??= Prototype as JsObject;
    }

    private JsWeakSet RequireWeakSet(object? candidate)
    {
        if (candidate is JsWeakSet weakSet)
        {
            return weakSet;
        }

        throw ThrowTypeError("WeakSet method called on incompatible receiver", realm: Realm);
    }
}
