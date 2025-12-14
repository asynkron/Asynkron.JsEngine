using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.RelativeTimeFormat", ToStringTag = "Intl.RelativeTimeFormat")]
public sealed partial class IntlRelativeTimeFormatPrototype
{
    private const string BrandKey = "__relativeTimeFormat__";

    internal static void InitializeInternalSlots(JsObject instance, string locale, string numberingSystem,
        string numeric, string style)
    {
        instance.SetProperty(BrandKey, true);
        instance.SetProperty("__locale__", locale);
        instance.SetProperty("__numberingSystem__", numberingSystem);
        instance.SetProperty("__numeric__", numeric);
        instance.SetProperty("__style__", style);
    }

    [JsHostMethod("format", Length = 2d)]
    private JsValue Format(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        ValidateReceiver(thisValue);
        var value = args.Count > 0 ? JsOps.ToNumber(args[0]) : double.NaN;
        if (double.IsNaN(value))
        {
            return new JsValue("NaN");
        }

        var unit = args.Count > 1 ? JsValueToString(args[1], Realm) : throw ThrowTypeError(
            "Intl.RelativeTimeFormat format requires a unit argument", realm: Realm);
        return new JsValue($"{value} {unit}");
    }

    [JsHostMethod("formatToParts", Length = 2d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var formatted = Format(thisValue, args);
        var part = new JsObject();
        part.SetProperty("type", "literal");
        part.SetProperty("value", formatted.AsString());
        var parts = new JsArray(Realm);
        parts.Push(part);
        return JsValue.FromObject(parts);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        obj.SetProperty("locale",
            instance.TryGetProperty("__locale__", out var locale) && locale.TryGetString(out var localeStr) ? localeStr : "en");
        obj.SetProperty("numberingSystem",
            instance.TryGetProperty("__numberingSystem__", out var numberingSystem) && numberingSystem.TryGetString(out var numSysStr)
                ? numSysStr
                : "latn");
        obj.SetProperty("numeric",
            instance.TryGetProperty("__numeric__", out var numeric) && numeric.TryGetString(out var numericStr) ? numericStr : "always");
        obj.SetProperty("style",
            instance.TryGetProperty("__style__", out var style) && style.TryGetString(out var styleStr) ? styleStr : "long");
        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue thisValue)
    {
        return thisValue.EnsureBrand(BrandKey, Realm,
            "Intl.RelativeTimeFormat method called on incompatible receiver");
    }
}
