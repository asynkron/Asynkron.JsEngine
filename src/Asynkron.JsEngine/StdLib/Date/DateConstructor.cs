using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Date", PrototypeType = typeof(DatePrototype), Length = 7d, DisplayName = "Date")]
public sealed partial class DateConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() as JsObject : null;
        return new JsValue(ConstructDate(args, targetCtor, targetCtor, providedThis, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.DatePrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, thisValue, context, newTarget) =>
        {
            if (newTarget is null)
            {
                var current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var local = ConvertMillisecondsToLocal(current, Realm);
                return FormatDateToJsString(local, Realm);
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? target;
            return ConstructDate(args, effectiveNewTarget, target, thisValue as JsObject, context);
        });

        AttachStatics(constructor);
    }

    private object ConstructDate(
        IReadOnlyList<JsValue> args,
        IJsCallable newTarget,
        IJsCallable targetCtor,
        JsObject? providedThis,
        EvaluationContext? context)
    {
        var instance = PrepareThisObject(providedThis != null ? new JsValue(providedThis) : JsValue.Undefined, assignPrototype: false);
        instance.RealmState ??= Realm;

        if (instance.Prototype is null)
        {
            var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
            if (proto is not null)
            {
                instance.SetPrototype(proto);
            }
        }

        var timeValue = ComputeDateTimeValue(args, Realm, context);
        StoreInternalDateValue(instance, timeValue);
        return instance;
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("now",
            new HostFunction(_ => (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Realm, isConstructor: false),
            Realm);

        constructor.SetHostedProperty("UTC", new HostFunction(args => DateUtc(args, Realm), Realm, isConstructor: false),
            Realm);

        constructor.SetHostedProperty("parse",
            new HostFunction(args => DateParse(args, Realm), Realm, isConstructor: false), Realm);
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Date constructor not initialized");
}
