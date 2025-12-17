using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("DataView", PrototypeType = typeof(DataViewPrototype), Length = 1d, DisplayName = "DataView")]
public sealed partial class DataViewConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var target = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() as JsObject : null;
        return JsValue.FromObjectUnsafe(ConstructDataView(args, target, providedThis));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.DataViewPrototype ??= Prototype as JsObject;
        Realm.DataViewConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisVal, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            var thisObject = thisVal.TryGetObject<JsObject>(out var jsObj) ? jsObj : null;
            return JsValue.FromObjectUnsafe(ConstructDataView(args, effectiveNewTarget, thisObject));
        });
    }

    private object ConstructDataView(IReadOnlyList<JsValue> args, IJsCallable newTarget, JsObject? providedThis)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("DataView requires an ArrayBuffer", realm: Realm);
        }

        var buffer = RequireArrayBuffer(args[0], Realm);
        var byteOffset = args.Count > 1 && !args[1].IsUndefined
            ? (int)JsOps.ToNumber(args[1])
            : 0;

        int? byteLength = null;
        if (args.Count > 2 && !args[2].IsUndefined)
        {
            byteLength = (int)JsOps.ToNumber(args[2]);
        }

        JsDataView dataView;
        try
        {
            dataView = new JsDataView(buffer, byteOffset, byteLength);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw ThrowRangeError("Invalid DataView length", realm: Realm);
        }

        var proto = ResolveConstructPrototype(newTarget, _constructor ?? newTarget, Realm) ?? Prototype;

        if (ReferenceEquals(newTarget, _constructor ?? newTarget))
        {
            if (proto is not null)
            {
                dataView.SetPrototype(proto);
            }

            return dataView;
        }

        var instance = PrepareThisObject(providedThis != null ? new JsValue(providedThis) : JsValue.Undefined, assignPrototype: false);
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }

        StoreInternalDataView(instance, dataView);
        return instance;
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("DataView constructor not initialized");
}
