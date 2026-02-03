#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.DateHelper;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("Date", PrototypeType = typeof(DatePrototype), Length = 7d, DisplayName = "Date")]
public sealed partial class DateConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Date constructor not initialized");
    // Static methods registered via code generation

    [JsConstructorMethod("now", Length = 0d)]
    public static JsValue Now()
    {
        return new JsValue((double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [JsConstructorMethod("UTC", Length = 7d)]
    public static JsValue UTC(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var y = JsOps.ToNumber(args[0]);
        var m = args.Count > 1 ? JsOps.ToNumber(args[1]) : 0d;
        var dt = args.Count > 2 ? JsOps.ToNumber(args[2]) : 1d;
        var h = args.Count > 3 ? JsOps.ToNumber(args[3]) : 0d;
        var min = args.Count > 4 ? JsOps.ToNumber(args[4]) : 0d;
        var s = args.Count > 5 ? JsOps.ToNumber(args[5]) : 0d;
        var ms = args.Count > 6 ? JsOps.ToNumber(args[6]) : 0d;

        var year = MakeFullYear(y);
        var day = MakeDay(year, m, dt);
        var time = MakeTime(h, min, s, ms);
        var clipped = TimeClip(MakeDate(day, time));
        return double.IsNaN(clipped) ? JsValue.NaN : new JsValue(clipped);
    }

    [JsConstructorMethod("parse", Length = 1d)]
    public static JsValue Parse(IReadOnlyList<JsValue> args, RealmState? realm)
    {
        if (realm is null)
        {
            throw new InvalidOperationException("Realm is required for Date.parse.");
        }

        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        var dateStr = JsOps.ToJsString(args[0]);
        var parsed = ParseDateTimeString(dateStr, realm);
        return double.IsNaN(parsed) ? JsValue.NaN : new JsValue(parsed);
    }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() : null;
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
        var instance = PrepareThisObject(providedThis != null ? new JsValue(providedThis) : JsValue.Undefined, false);
        instance.RealmState ??= Realm;

        if (instance.Prototype is null)
        {
            var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
            if (true)
            {
                instance.SetPrototype(proto);
            }
        }

        var timeValue = ComputeDateTimeValue(args, Realm, context);
        StoreInternalDateValue(instance, timeValue);
        return instance;
    }
}
