#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("WeakSet", PrototypeType = typeof(WeakSetPrototype), Length = 0d, DisplayName = "WeakSet")]
public sealed partial class WeakSetConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsWeakSet>(prototype, realm, "WeakSet")
{
    protected override JsWeakSet CreateInstance()
    {
        return new JsWeakSet();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.WeakSetConstructor ??= constructor;
        Realm.WeakSetPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsWeakSet instance, IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNull || args[0].IsUndefined)
        {
            return;
        }

        if (!instance.TryGetProperty("add", out var adderValue) ||
            !adderValue.TryGetObject<IJsCallable>(out var adder))
        {
            throw StandardLibrary.ThrowTypeError("WeakSet.prototype.add is not callable", realm: Realm);
        }

        var instanceValue = JsValue.FromObjectUnsafe(instance);
        MapSetIterationHelper.Iterate(args[0], Realm, "WeakSet constructor", entry =>
        {
            adder.Invoke([entry], instanceValue);
        });
    }
}
