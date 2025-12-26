#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

/// <summary>
/// AsyncFunction constructor for creating async functions dynamically
/// </summary>
[JsConstructor("AsyncFunction", PrototypeType = typeof(FunctionPrototype), Length = 1d, DisplayName = "AsyncFunction")]
public sealed partial class AsyncFunctionConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncFunction constructor
        // Creates a new async function from string code
        throw new NotImplementedException("AsyncFunction constructor is not yet implemented");
    }
}
