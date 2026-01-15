#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

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

        // ES spec: Set constructor step 7-8
        // 7. Let adder be ? Get(set, "add").
        // 8. If IsCallable(adder) is false, throw a TypeError exception.
        if (!instance.TryGetProperty("add", out var adderValue) ||
            !adderValue.TryGetObject<IJsCallable>(out var adder))
        {
            throw ThrowTypeError("Set.prototype.add is not callable", realm: Realm);
        }

        var instanceValue = JsValue.FromObjectUnsafe(instance);

        // ES spec: Set constructor step 9
        // Call adder with set as this and nextValue as argument
        // If abrupt, close iterator (handled by MapSetIterationHelper.Iterate)
        MapSetIterationHelper.Iterate(args[0], Realm, "Set constructor",
            value => adder.Invoke(new SingleValueArgs(value), instanceValue));
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }
}
