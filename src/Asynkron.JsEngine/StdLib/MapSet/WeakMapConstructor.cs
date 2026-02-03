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

        if (!instance.TryGetProperty("set", out var adderValue) ||
            !adderValue.TryGetObject<IJsCallable>(out var adder))
        {
            throw StandardLibrary.ThrowTypeError("WeakMap.prototype.set is not callable", realm: Realm);
        }

        var instanceValue = JsValue.FromObjectUnsafe(instance);

        MapSetIterationHelper.Iterate(args[0], Realm, "WeakMap constructor", entry =>
        {
            JsValue key;
            JsValue value;

            if (entry.TryGetObject<IJsPropertyAccessor>(out var accessor))
            {
                accessor.TryGetProperty("0", out key);
                accessor.TryGetProperty("1", out value);
            }
            else
            {
                throw StandardLibrary.ThrowTypeError("WeakMap constructor expects iterable entries", realm: Realm);
            }

            adder.Invoke([key, value], instanceValue);
        });
    }
}
