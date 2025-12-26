#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncGeneratorFunction constructor for creating async generator functions dynamically
/// </summary>
[JsConstructor("AsyncGeneratorFunction", PrototypeType = typeof(AsyncGeneratorPrototype), Length = 1d, DisplayName = "AsyncGeneratorFunction")]
public sealed partial class AsyncGeneratorFunctionConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncGeneratorFunction constructor
        // Creates a new async generator function from string code
        throw new NotImplementedException("AsyncGeneratorFunction constructor is not yet implemented");
    }
}
