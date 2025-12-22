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
        return map.Get(args.GetArgument(0));
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
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakMapPrototype ??= Prototype as JsObject;
    }

    private JsWeakMap RequireWeakMap(JsValue receiver)
    {
        if (receiver.TryGetObject<JsWeakMap>(out var weakMap))
        {
            return weakMap;
        }

        throw ThrowTypeError("WeakMap method called on incompatible receiver", realm: Realm);
    }
}
