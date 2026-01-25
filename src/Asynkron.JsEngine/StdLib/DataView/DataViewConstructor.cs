#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ArrayBufferHelper;
using static Asynkron.JsEngine.StdLib.NumberHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("DataView", PrototypeType = typeof(DataViewPrototype), Length = 1d, DisplayName = "DataView")]
public sealed partial class DataViewConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("DataView constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        thisValue.TryGetObject<JsObject>(out var providedThis);
        return ConstructDataView(args, targetCtor, targetCtor, providedThis);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.DataViewPrototype ??= Prototype as JsObject;
        Realm.DataViewConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisVal, _, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("DataView constructor requires 'new'", realm: Realm);
            }

            var targetCtor = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : targetCtor;
            var thisObject = thisVal.TryGetObject<JsObject>(out var jsObj) ? jsObj : null;
            return ConstructDataView(args, effectiveNewTarget, targetCtor, thisObject);
        });
    }

    private JsValue ConstructDataView(
        IReadOnlyList<JsValue> args,
        IJsCallable newTarget,
        IJsCallable targetCtor,
        JsObject? providedThis)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("DataView requires an ArrayBuffer", realm: Realm);
        }

        var buffer = RequireArrayBuffer(args[0], Realm);

        var byteOffsetArg = args.Count > 1 ? args[1] : JsValue.Undefined;
        var byteOffset = byteOffsetArg.IsUndefined ? 0 : ToIndex(byteOffsetArg, Realm);

        // Spec order: Convert byteOffset before checking detached.
        if (buffer.IsDetached)
        {
            throw ThrowTypeError(
                "Cannot perform DataView operation on a detached ArrayBuffer",
                realm: Realm);
        }

        // Validate against the initial buffer byte length before accessing NewTarget.prototype.
        var initialBufferByteLength = buffer.ByteLength;
        if (byteOffset > initialBufferByteLength)
        {
            throw ThrowRangeError("Invalid DataView length", realm: Realm);
        }

        int? byteLength = null;
        if (args.Count > 2 && !args[2].IsUndefined)
        {
            byteLength = ToIndex(args[2], Realm);
            if ((long)byteOffset + byteLength.Value > initialBufferByteLength)
            {
                throw ThrowRangeError("Invalid DataView length", realm: Realm);
            }
        }

        var instance =
            PrepareThisObject(providedThis != null ? new JsValue(providedThis) : JsValue.Undefined,
                assignPrototype: false);

        instance.RealmState ??= Realm;

        if (instance.Prototype is null)
        {
            var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
            instance.SetPrototype(proto);
        }

        // Re-check detachment after OrdinaryCreateFromConstructor / GetPrototypeFromConstructor.
        if (buffer.IsDetached)
        {
            throw ThrowTypeError(
                "Cannot perform DataView operation on a detached ArrayBuffer",
                realm: Realm);
        }

        // For resizable buffers, user code may have resized during prototype access.
        // Ensure the view remains in bounds.
        var currentBufferByteLength = buffer.ByteLength;
        if (byteLength is null)
        {
            if (byteOffset > currentBufferByteLength)
            {
                throw ThrowRangeError("Invalid DataView length", realm: Realm);
            }
        }
        else
        {
            if ((long)byteOffset + byteLength.Value > currentBufferByteLength)
            {
                throw ThrowRangeError("Invalid DataView length", realm: Realm);
            }
        }

        var dataView = new JsDataView(buffer, byteOffset, byteLength);
        instance.SetProperty("_internalDataView", dataView);
        return JsValue.FromObjectUnsafe(instance);
    }
}
