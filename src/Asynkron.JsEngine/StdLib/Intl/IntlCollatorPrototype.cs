using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.Collator", ToStringTag = "Intl.Collator")]
public sealed partial class IntlCollatorPrototype
{
    private const string CollatorBrand = "__collator__";

    internal static void InitializeInternalSlots(JsObject instance)
    {
        instance.SetProperty(CollatorBrand, true);
        instance.SetProperty("__locale__", "en");
        instance.SetProperty("__usage__", "sort");
        instance.SetProperty("__sensitivity__", "variant");
        instance.SetProperty("__ignorePunctuation__", false);
    }

    [JsHostMethod("compare", Length = 2d)]
    private double Compare(object? thisValue, IReadOnlyList<object?> args)
    {
        ValidateCollatorReceiver(thisValue);
        var first = args.Count > 0 ? JsValueToString(args[0]) : string.Empty;
        var second = args.Count > 1 ? JsValueToString(args[1]) : string.Empty;
        return string.CompareOrdinal(first, second) switch
        {
            < 0 => -1d,
            > 0 => 1d,
            _ => 0d
        };
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsObject ResolvedOptions(object? thisValue, IReadOnlyList<object?> _)
    {
        var collator = ValidateCollatorReceiver(thisValue);
        var options = new JsObject(Realm.ObjectPrototype);
        options.SetProperty("locale", collator.TryGetProperty("__locale__", out var locale) ? locale ?? "en" : "en");
        options.SetProperty("usage", collator.TryGetProperty("__usage__", out var usage) ? usage ?? "sort" : "sort");
        options.SetProperty("sensitivity",
            collator.TryGetProperty("__sensitivity__", out var sensitivity) ? sensitivity ?? "variant" : "variant");
        options.SetProperty("ignorePunctuation",
            collator.TryGetProperty("__ignorePunctuation__", out var ignore) && ignore is bool ignoreBool &&
            ignoreBool);
        return options;
    }

    private JsObject ValidateCollatorReceiver(object? thisValue)
    {
        return thisValue.EnsureBrand(CollatorBrand, Realm,
            "Intl.Collator method called on incompatible receiver");
    }

}
