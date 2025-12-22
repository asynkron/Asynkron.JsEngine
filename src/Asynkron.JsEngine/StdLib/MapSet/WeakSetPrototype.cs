using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

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
        if (Prototype is JsObject { RealmState: null } jsObj)
        {
            jsObj.RealmState = Realm;
        }

        Realm.WeakSetPrototype ??= Prototype as JsObject;
    }

    private JsWeakSet RequireWeakSet(JsValue receiver)
    {
        if (receiver.TryGetObject<JsWeakSet>(out var weakSet))
        {
            return weakSet;
        }

        throw StandardLibrary.ThrowTypeError("WeakSet method called on incompatible receiver", realm: Realm);
    }
}
