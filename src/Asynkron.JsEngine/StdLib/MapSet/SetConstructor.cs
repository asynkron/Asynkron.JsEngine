#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Set", PrototypeType = typeof(SetPrototype), Length = 0d, DisplayName = "Set")]
public sealed partial class SetConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsSet>(prototype, realm, "Set")
{
    protected override JsSet CreateInstance()
    {
        return new JsSet();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.SetConstructor ??= constructor;
        Realm.SetPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsSet instance, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        MapSetIterationHelper.Iterate(args[0], Realm, "Set constructor", value => instance.Add(value));
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }
}
