#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("FinalizationRegistry", PrototypeType = typeof(FinalizationRegistryPrototype), Length = 1d, DisplayName = "FinalizationRegistry")]
public sealed partial class FinalizationRegistryConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement FinalizationRegistry constructor
        // Creates a new FinalizationRegistry instance with a cleanup callback
        return CreateDefaultInstance();
    }
}
