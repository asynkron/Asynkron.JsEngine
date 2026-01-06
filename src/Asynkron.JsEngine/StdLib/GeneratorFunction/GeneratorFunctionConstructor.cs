#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// GeneratorFunction constructor for creating generator functions dynamically
/// </summary>
[JsConstructor("GeneratorFunction", PrototypeType = typeof(GeneratorPrototype), Length = 1d, DisplayName = "GeneratorFunction")]
public sealed partial class GeneratorFunctionConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement GeneratorFunction constructor
        // Creates a new generator function from string code
        throw new NotImplementedException("GeneratorFunction constructor is not yet implemented");
    }
}
