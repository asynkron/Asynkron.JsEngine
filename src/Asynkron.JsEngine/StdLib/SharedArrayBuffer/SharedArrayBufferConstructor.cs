#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("SharedArrayBuffer", PrototypeType = typeof(SharedArrayBufferPrototype), Length = 1d,
    DisplayName = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("SharedArrayBuffer constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = _constructor ?? ConstructFallback;
        return JsValue.FromObjectUnsafe(ConstructBuffer(args, target));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.SharedArrayBufferPrototype ??= Prototype as JsObject;
        Realm.SharedArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("SharedArrayBuffer constructor requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            return JsValue.FromObjectUnsafe(ConstructBuffer(args, effectiveNewTarget));
        });
        // [Symbol.species] is registered via code generation from attribute
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        return thisValue;
    }

    private object ConstructBuffer(IReadOnlyList<JsValue> args, IJsCallable newTarget) =>
        ArrayBufferHelper.ConstructBufferCore(args, newTarget, _constructor, Prototype, Realm,
            isShared: true, "SharedArrayBuffer", PrepareThisObject);
}
