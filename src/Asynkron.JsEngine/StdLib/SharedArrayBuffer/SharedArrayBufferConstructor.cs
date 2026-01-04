#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
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

    private object ConstructBuffer(IReadOnlyList<JsValue> args, IJsCallable newTarget)
    {
        if (newTarget is null)
        {
            throw ThrowTypeError("SharedArrayBuffer constructor requires 'new'", realm: Realm);
        }

        var byteLength = args.Count > 0 && !args[0].IsUndefined
            ? NumberHelper.ToIndexAsLong(args[0], Realm)
            : 0L;

        var requestedMax = ArrayBufferHelper.GetRequestedMaxByteLength(args.Count > 1 ? args[1] : JsValue.Undefined, Realm);
        if (requestedMax is { } maxValue && byteLength > maxValue)
        {
            throw ThrowRangeError("Invalid SharedArrayBuffer length", realm: Realm);
        }

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            var allocLength = ArrayBufferHelper.RequireAllocatableLength(byteLength, Realm);
            int? allocMax = requestedMax is { } maxIndex ? ArrayBufferHelper.RequireAllocatableLength(maxIndex, Realm) : null;
            var directBuffer = new JsArrayBuffer(allocLength, allocMax, Realm, true);
            directBuffer.SetPrototype(Prototype);
            return directBuffer;
        }

        var instance = PrepareThisObject(JsValue.Undefined, false);
        var proto = ReflectHelper.ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        var derivedLength = ArrayBufferHelper.RequireAllocatableLength(byteLength, Realm);
        int? derivedMax = requestedMax is { } maxValue2 ? ArrayBufferHelper.RequireAllocatableLength(maxValue2, Realm) : null;
        var buffer = new JsArrayBuffer(derivedLength, derivedMax, Realm, true);
        ArrayBufferHelper.StoreInternalArrayBuffer(instance, buffer);
        return instance;
    }
}
