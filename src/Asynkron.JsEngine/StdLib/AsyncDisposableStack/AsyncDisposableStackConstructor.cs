#region

using System;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("AsyncDisposableStack", PrototypeType = typeof(AsyncDisposableStackPrototype), Length = 0d, DisplayName = "AsyncDisposableStack")]
public sealed partial class AsyncDisposableStackConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("AsyncDisposableStack constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true })
        {
            var target = _constructor ?? ConstructFallback;
            return JsValue.FromObjectUnsafe(ConstructStack(target, target));
        }

        throw ThrowTypeError("Constructor AsyncDisposableStack requires 'new'", realm: Realm);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.AsyncDisposableStackConstructor ??= constructor;
        Realm.AsyncDisposableStackPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((_, _, _, newTarget) =>
        {
            if (!newTarget.TryGetCallable(out var callable))
            {
                throw ThrowTypeError("Constructor AsyncDisposableStack requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            return JsValue.FromObjectUnsafe(ConstructStack(callable, target));
        });
    }

    private object ConstructStack(IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = new JsAsyncDisposableStack();
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        return instance;
    }
}
