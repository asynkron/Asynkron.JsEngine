#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("DisposableStack", PrototypeType = typeof(DisposableStackPrototype), Length = 0d, DisplayName = "DisposableStack")]
public sealed partial class DisposableStackConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        // TODO: Implement DisposableStack constructor
        // Creates a new DisposableStack for explicit resource management
        var obj = PrepareThisObject(JsValue.Undefined, false);
        if (Prototype is not null && obj.Prototype is null)
        {
            obj.SetPrototype(Prototype);
        }
        obj.RealmState ??= Realm;
        return new JsValue(obj);
    }
}
