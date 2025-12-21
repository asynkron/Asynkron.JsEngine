using System.Globalization;
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
    public static object Now()
    {
        return (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    [JsConstructorMethod("UTC", Length = 7d)]
    public static object UTC(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return JsValue.NaN;
        }

        static double ToNumberOrNaN(JsValue v) => v.TryGetDouble(out var d) ? d : double.NaN;

        var y = ToNumberOrNaN(args[0]);
        var m = args.Count > 1 ? ToNumberOrNaN(args[1]) : 0;
        var dt = args.Count > 2 ? ToNumberOrNaN(args[2]) : 1;
        var h = args.Count > 3 ? ToNumberOrNaN(args[3]) : 0;
        var min = args.Count > 4 ? ToNumberOrNaN(args[4]) : 0;
        var s = args.Count > 5 ? ToNumberOrNaN(args[5]) : 0;
        var ms = args.Count > 6 ? ToNumberOrNaN(args[6]) : 0;

        if (double.IsNaN(y) || double.IsNaN(m) || double.IsNaN(dt) ||
            double.IsNaN(h) || double.IsNaN(min) || double.IsNaN(s) || double.IsNaN(ms))
        {
            return JsValue.NaN;
        }

        var year = (int)y;
        if (year is >= 0 and <= 99)
        {
            year += 1900;
        }

        var month = (int)m + 1;
        var day = (int)dt;
        var hour = (int)h;
        var minute = (int)min;
        var second = (int)s;
        var millisecond = (int)ms;

        try
        {
            var utcDate = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
            var dto = new DateTimeOffset(utcDate);
            return new JsValue((double)dto.ToUnixTimeMilliseconds());
        }
        catch
        {
            return JsValue.NaN;
        }
    }

    [JsConstructorMethod("parse", Length = 1d)]
    public static object Parse(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetString(out var dateStr))
        {
            return JsValue.NaN;
        }

        return DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, out var parsed)
            ? new JsValue((double)parsed.ToUnixTimeMilliseconds())
            : JsValue.NaN;
    }

    private HostFunction? _constructor;

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
