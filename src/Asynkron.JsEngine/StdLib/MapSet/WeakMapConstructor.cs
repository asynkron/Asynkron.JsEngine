#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakMap", PrototypeType = typeof(WeakMapPrototype), Length = 0d, DisplayName = "WeakMap")]
public sealed partial class WeakMapConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsWeakMap>(prototype, realm, "WeakMap")
{
    protected override JsWeakMap CreateInstance()
    {
        return new JsWeakMap();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.WeakMapConstructor ??= constructor;
        Realm.WeakMapPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsWeakMap instance, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!args[0].TryGetObject<JsArray>(out var entries))
        {
            return;
        }

        foreach (var entry in entries.Items)
        {
            if (!entry.TryGetObject<JsArray>(out var pair) || pair.Items.Count < 2)
            {
                continue;
            }

            instance.Set(pair.GetElement(0), pair.GetElement(1));
        }
    }
}
