using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("DataView", PrototypeType = typeof(DataViewPrototype), Length = 1d, DisplayName = "DataView")]
public sealed partial class DataViewConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var target = _constructor ?? ConstructFallback;
        return ConstructDataView(args, target, thisValue as JsObject);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.DataViewPrototype ??= Prototype as JsObject;
        Realm.DataViewConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisVal, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? target;
            return ConstructDataView(args, effectiveNewTarget, thisVal as JsObject);
        });
    }

    private object ConstructDataView(IReadOnlyList<object?> args, IJsCallable newTarget, JsObject? providedThis)
    {
        if (args.Count == 0)
        {
            throw ThrowTypeError("DataView requires an ArrayBuffer", realm: Realm);
        }

        var buffer = RequireArrayBuffer(args[0], Realm);
        var byteOffset = args.Count > 1 && !ReferenceEquals(args[1], Symbol.Undefined)
            ? (int)JsOps.ToNumber(args[1])
            : 0;

        int? byteLength = null;
        if (args.Count > 2 && !ReferenceEquals(args[2], Symbol.Undefined))
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

        var instance = PrepareThisObject(providedThis, assignPrototype: false);
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
