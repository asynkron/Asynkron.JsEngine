using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakSet", PrototypeType = typeof(WeakSetPrototype), Length = 0d, DisplayName = "WeakSet")]
public sealed partial class WeakSetConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsWeakSet>(prototype, realm, "WeakSet")
{
    protected override JsWeakSet CreateInstance() => new();

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.WeakSetConstructor ??= constructor;
        Realm.WeakSetPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsWeakSet instance, IReadOnlyList<JsValue> args)
    {
        PopulateCollectionFromIterable(instance, args, "add", "WeakSet.prototype.add is not callable", "WeakSet constructor",
            (adder, entry, instanceValue) => adder.Invoke([entry], instanceValue));
    }
}
