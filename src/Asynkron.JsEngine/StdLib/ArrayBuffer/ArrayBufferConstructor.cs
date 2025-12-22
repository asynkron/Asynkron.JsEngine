#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayBufferHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("ArrayBuffer", PrototypeType = typeof(ArrayBufferPrototype), Length = 1d, DisplayName = "ArrayBuffer")]
public sealed partial class ArrayBufferConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("ArrayBuffer constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = _constructor ?? ConstructFallback;
        return JsValue.FromObjectUnsafe(ConstructBuffer(args, target));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.ArrayBufferPrototype ??= Prototype as JsObject;
        Realm.ArrayBufferConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("ArrayBuffer constructor requires 'new'", realm: Realm);
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            return JsValue.FromObjectUnsafe(ConstructBuffer(args, effectiveNewTarget));
        });
        // [Symbol.species] is registered via code generation from attribute
    }

    [JsConstructorMethod("isView", Length = 1d)]
    public static JsValue IsView(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].IsNullOrUndefined)
        {
            return JsValue.False;
        }

        var arg = args[0];
        if (arg.TryGetObject<TypedArrayBase>(out _) || arg.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        if (arg.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
            accessor.TryGetProperty("_internalDataView", out var dv) &&
            dv.TryGetObject<JsDataView>(out _))
        {
            return JsValue.True;
        }

        return JsValue.False;
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
            throw ThrowTypeError("ArrayBuffer constructor requires 'new'", realm: Realm);
        }

        var byteLength = args.Count > 0 && !args[0].IsUndefined
            ? ToIndexAsLong(args[0], Realm)
            : 0L;

        var requestedMax = GetRequestedMaxByteLength(args.Count > 1 ? args[1] : JsValue.Undefined);
        if (requestedMax is { } maxValue && byteLength > maxValue)
        {
            throw ThrowRangeError("Invalid ArrayBuffer length", realm: Realm);
        }

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            var allocLength = RequireAllocatableLength(byteLength);
            int? allocMax = requestedMax is { } maxIndex ? RequireAllocatableLength(maxIndex) : null;
            return new JsArrayBuffer(allocLength, allocMax, Realm);
        }

        var instance = PrepareThisObject(JsValue.Undefined, false);
        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        var derivedLength = RequireAllocatableLength(byteLength);
        int? derivedMax = requestedMax is { } maxValue2 ? RequireAllocatableLength(maxValue2) : null;
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

        return ToIndexAsLong(JsValue.FromObjectUnsafe(maxVal), Realm);
    }
}
