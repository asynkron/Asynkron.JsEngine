using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ArrayBuffer", PrototypeType = typeof(ArrayBufferPrototype), Length = 1d, DisplayName = "ArrayBuffer")]
public sealed partial class ArrayBufferConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var target = _constructor ?? ConstructFallback;
        return ConstructBuffer(args, target);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ArrayBufferPrototype ??= Prototype as JsObject;
        Realm.ArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget is null)
            {
                throw ThrowTypeError("ArrayBuffer constructor requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? target;
            return ConstructBuffer(args, effectiveNewTarget);
        });

        var speciesKey = SymbolKeys.GetSpecies(Realm);
        var speciesGetter = new HostFunction((thisVal, _) => thisVal, Realm)
        {
            IsConstructor = false
        };
        AttachBuiltinMetadata(speciesGetter, "get [Symbol.species]", 0d);

        constructor.DefineProperty(speciesKey, new PropertyDescriptor
        {
            Get = speciesGetter,
            Enumerable = false,
            Configurable = true
        });

        var isView = new HostFunction(ArrayBufferIsView, Realm) { IsConstructor = false };
        AttachBuiltinMetadata(isView, "isView", 1d);
        constructor.DefineProperty("isView", new PropertyDescriptor
        {
            Value = isView,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
    }

    private object ConstructBuffer(IReadOnlyList<object?> args, IJsCallable newTarget)
    {
        if (newTarget is null)
        {
            throw ThrowTypeError("ArrayBuffer constructor requires 'new'", realm: Realm);
        }

        var byteLength = args.Count > 0 && !ReferenceEquals(args[0], Symbol.Undefined)
            ? ToIndexAsLong(args[0], Realm)
            : 0L;

        var requestedMax = GetRequestedMaxByteLength(args.Count > 1 ? args[1] : null);
        if (requestedMax is { } maxValue && byteLength > maxValue)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: Realm);
        }

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            var allocLength = RequireAllocatableLength(byteLength);
            int? allocMax = requestedMax is { } maxIndex ? RequireAllocatableLength(maxIndex) : (int?)null;
            return new JsArrayBuffer(allocLength, allocMax, Realm);
        }

        var instance = PrepareThisObject(null, assignPrototype: false);
        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        var derivedLength = RequireAllocatableLength(byteLength);
        int? derivedMax = requestedMax is { } maxValue2 ? RequireAllocatableLength(maxValue2) : (int?)null;
        var buffer = new JsArrayBuffer(derivedLength, derivedMax, Realm);
        StoreInternalArrayBuffer(instance, buffer);
        return instance;
    }

    private int RequireAllocatableLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: Realm);
        }

        return (int)length;
    }

    private long? GetRequestedMaxByteLength(object? options)
    {
        if (options is null || ReferenceEquals(options, Symbol.Undefined))
        {
            return null;
        }

        if (options is not IJsPropertyAccessor accessor)
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
        _constructor ?? throw new InvalidOperationException("ArrayBuffer constructor not initialized");
}
