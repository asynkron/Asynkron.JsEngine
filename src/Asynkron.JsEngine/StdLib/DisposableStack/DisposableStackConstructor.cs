#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("DisposableStack", PrototypeType = typeof(DisposableStackPrototype), Length = 0d, DisplayName = "DisposableStack")]
public sealed partial class DisposableStackConstructor(IJsObjectLike prototype, RealmState realm)
    : SimpleInstanceConstructorBase<JsDisposableStack>(prototype, realm, "DisposableStack")
{
    protected override JsDisposableStack CreateInstance()
    {
        return new JsDisposableStack();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.DisposableStackConstructor ??= constructor;
        Realm.DisposableStackPrototype ??= Prototype as JsObject;
    }
}
