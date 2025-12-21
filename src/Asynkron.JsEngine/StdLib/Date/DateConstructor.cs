using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.DateHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Date", PrototypeType = typeof(DatePrototype), Length = 7d, DisplayName = "Date")]
public sealed partial class DateConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    // Static methods registered via code generation

    [JsConstructorMethod("now", Length = 0d)]
    public static object? Now()
    {
        return (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    [JsConstructorMethod("UTC", Length = 7d)]
    public static object? UTC(IReadOnlyList<JsValue> args)
    {
        return DateUtc(args);
    }

    [JsConstructorMethod("parse", Length = 1d)]
    public static object? Parse(IReadOnlyList<JsValue> args)
    {
        return DateParse(args);
    }

    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() as JsObject : null;
        return JsValue.FromObjectUnsafe(ConstructDate(args, targetCtor, targetCtor, providedThis, null));
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.DatePrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, thisValue, context, newTarget) =>
        {
            if (newTarget.IsUndefined)
            {
                var current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var local = ConvertMillisecondsToLocal(current, Realm);
                return new JsValue(FormatDateToJsString(local, Realm));
            }

            var target = _constructor ?? constructor;
            var effectiveNewTarget = newTarget.TryGetObject<IJsCallable>(out var nt) ? nt : target;
            var thisObj = thisValue.TryGetObject(out var jsObj) ? jsObj : null;
            return JsValue.FromObjectUnsafe(ConstructDate(args, effectiveNewTarget, target, thisObj, context));
        });

        // Static methods are now registered via code generation from [JsConstructorMethod] attributes
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

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Date constructor not initialized");
}
