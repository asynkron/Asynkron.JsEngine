#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("AsyncDisposableStack", PrototypeType = typeof(AsyncDisposableStackPrototype), Length = 0d, DisplayName = "AsyncDisposableStack")]
public sealed partial class AsyncDisposableStackConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement AsyncDisposableStack constructor
        // Creates a new AsyncDisposableStack for async explicit resource management
        var obj = PrepareThisObject(JsValue.Undefined, false);
        if (Prototype is not null && obj.Prototype is null)
        {
            obj.SetPrototype(Prototype);
        }
        obj.RealmState ??= Realm;
        return new JsValue(obj);
    }
}
