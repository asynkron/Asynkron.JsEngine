#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("AsyncDisposableStack", PrototypeType = typeof(AsyncDisposableStackPrototype), Length = 0d, DisplayName = "AsyncDisposableStack")]
public sealed partial class AsyncDisposableStackConstructor(IJsObjectLike prototype, RealmState realm)
    : SimpleInstanceConstructorBase<JsAsyncDisposableStack>(prototype, realm, "AsyncDisposableStack")
{
    protected override JsAsyncDisposableStack CreateInstance()
    {
        return new JsAsyncDisposableStack();
    }

    protected override void ConfigureRealmProperties(HostFunction constructor)
    {
        Realm.AsyncDisposableStackConstructor ??= constructor;
        Realm.AsyncDisposableStackPrototype ??= Prototype as JsObject;
    }
}
