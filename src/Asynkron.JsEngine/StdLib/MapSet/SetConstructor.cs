using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Set", PrototypeType = typeof(SetPrototype), Length = 0d, DisplayName = "Set")]
public sealed partial class SetConstructor(IJsObjectLike prototype, RealmState realm)
    : CollectionConstructorBase<JsSet>(prototype, realm, "Set")
{
    protected override JsSet CreateInstance() => new();

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.SetConstructor ??= constructor;
        Realm.SetPrototype ??= Prototype as JsObject;
    }

    protected override void PopulateInstance(JsSet instance, IReadOnlyList<JsValue> args)
    {
        // ES spec: Set constructor step 9
        // Call adder with set as this and nextValue as argument
        // If abrupt, close iterator (handled by MapSetIterationHelper.Iterate)
        PopulateCollectionFromIterable(instance, args, "add", "Set.prototype.add is not callable", "Set constructor",
            (adder, value, instanceValue) => adder.Invoke(new SingleValueArgs(value), instanceValue));
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue) => thisValue;
}
