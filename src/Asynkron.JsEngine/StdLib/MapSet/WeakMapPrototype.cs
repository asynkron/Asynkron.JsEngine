#region

using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("WeakMap", ToStringTag = "WeakMap", InstanceType = typeof(JsWeakMap))]
public sealed partial class WeakMapPrototype
{
    [JsHostMethod("set", Length = 2d)]
    public JsValue Set(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        return (JsValue)map.Set(key, value);
    }

    [JsHostMethod("get", Length = 1d)]
    public JsValue Get(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return map.Get(args.GetArgument(0));
    }

    [JsHostMethod("has", Length = 1d)]
    public JsValue Has(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Has(args.GetArgument(0)));
    }

    [JsHostMethod("delete", Length = 1d)]
    public JsValue Delete(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        return new JsValue(map.Delete(args.GetArgument(0)));
    }

    [JsHostMethod("getOrInsert", Length = 2d)]
    public JsValue GetOrInsert(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        var key = args.GetArgument(0);
        var value = args.GetArgument(1);
        if (map.Has(key))
        {
            return map.Get(key);
        }

        map.Set(key, value);
        return value;
    }

    [JsHostMethod("getOrInsertComputed", Length = 2d)]
    public JsValue GetOrInsertComputed(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var map = RequireInstance(thisValue);
        var callbackfn = args.GetArgument(1);

        // Per spec step 3: validate callable BEFORE checking if key exists
        if (!callbackfn.TryGetCallable(out var callable))
        {
            throw StandardLibrary.ThrowTypeError("WeakMap.prototype.getOrInsertComputed callback must be callable", realm: Realm);
        }

        var key = args.GetArgument(0);
        if (map.Has(key))
        {
            return map.Get(key);
        }

        var value = callable.Invoke([key], JsValue.Undefined);
        map.Set(key, value);
        return value;
    }

    protected override void ConfigurePrototype()
    {
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakMapPrototype ??= Prototype as JsObject;
    }
}
