using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("SharedArrayBuffer", PrototypeType = typeof(SharedArrayBufferPrototype), Length = 1d,
    DisplayName = "SharedArrayBuffer")]
public sealed partial class SharedArrayBufferConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

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

        var speciesKey = SymbolKeys.Species;
        constructor.DefineProperty(speciesKey,
            new PropertyDescriptor
            {
                Get = new HostFunction((thisVal, _) => thisVal),
                Enumerable = false,
                Configurable = true
            });
    }

    private object ConstructBuffer(IReadOnlyList<JsValue> args, IJsCallable newTarget)
    {
        if (newTarget is null)
        {
            throw ThrowTypeError("SharedArrayBuffer constructor requires 'new'", realm: Realm);
        }

        var byteLength = args.Count > 0 && !args[0].IsUndefined
            ? ToIndexAsLong(args[0], Realm)
            : 0L;

        var requestedMax = GetRequestedMaxByteLength(args.Count > 1 ? args[1] : JsValue.Undefined);
        if (requestedMax is { } maxValue && byteLength > maxValue)
        {
            throw ThrowRangeError("Invalid SharedArrayBuffer length", realm: Realm);
        }

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            var allocLength = RequireAllocatableLength(byteLength);
            int? allocMax = requestedMax is { } maxIndex ? RequireAllocatableLength(maxIndex) : null;
            var directBuffer = new JsArrayBuffer(allocLength, allocMax, Realm, isShared: true);
            directBuffer.SetPrototype(Prototype);
            return directBuffer;
        }

        var instance = PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        var derivedLength = RequireAllocatableLength(byteLength);
        int? derivedMax = requestedMax is { } maxValue2 ? RequireAllocatableLength(maxValue2) : null;
        var buffer = new JsArrayBuffer(derivedLength, derivedMax, Realm, isShared: true);
        StoreInternalArrayBuffer(instance, buffer);
        return instance;
    }

    private int RequireAllocatableLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw ThrowRangeError("Invalid SharedArrayBuffer length", realm: Realm);
        }

        return (int)length;
    }

    private long? GetRequestedMaxByteLength(JsValue options)
    {
        if (options.IsUndefined || options.IsNull)
        {
            return null;
        }

        if (!options.IsObject || options.AsObject() is not IJsPropertyAccessor accessor)
        {
            return null;
        }

        var context = Realm.CreateContext();
        if (!JsOps.TryGetPropertyValue(accessor, "maxByteLength", out var maxVal, context))
        {
            return null;
        }

        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        if (ReferenceEquals(maxVal, Symbol.Undefined))
        {
            return null;
        }

        return ToIndexAsLong(maxVal, Realm);
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("SharedArrayBuffer constructor not initialized");
}
