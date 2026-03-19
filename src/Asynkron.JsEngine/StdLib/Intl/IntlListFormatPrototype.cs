#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsPrototype("Intl.ListFormat", ToStringTag = "Intl.ListFormat")]
public sealed partial class IntlListFormatPrototype
{
    private const string BrandKey = "__listFormat__";
    private const string LocaleSlot = "__locale__";
    private const string TypeSlot = "__type__";
    private const string StyleSlot = "__style__";

    internal static void InitializeInternalSlots(JsObject instance, string locale, string type, string style)
    {
        instance.SetProperty(BrandKey, new JsValue(true));
        instance.SetProperty(LocaleSlot, new JsValue(locale));
        instance.SetProperty(TypeSlot, new JsValue(type));
        instance.SetProperty(StyleSlot, new JsValue(style));
    }

    [JsHostMethod("format", Length = 1d)]
    private JsValue Format(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var listArg = args.GetArgument(0);
        var stringList = StringListFromIterable(listArg);

        var type = GetStringSlot(instance, TypeSlot, "conjunction");
        var style = GetStringSlot(instance, StyleSlot, "long");
        var locale = GetStringSlot(instance, LocaleSlot, "en");

        return new JsValue(FormatList(stringList, type, style, locale));
    }

    [JsHostMethod("formatToParts", Length = 1d)]
    private JsValue FormatToParts(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var instance = ValidateReceiver(thisValue);
        var listArg = args.GetArgument(0);
        var stringList = StringListFromIterable(listArg);

        var type = GetStringSlot(instance, TypeSlot, "conjunction");
        var style = GetStringSlot(instance, StyleSlot, "long");
        var locale = GetStringSlot(instance, LocaleSlot, "en");

        var parts = FormatListToParts(stringList, type, style, locale);
        var result = new JsArray(Realm);
        foreach (var (partType, partValue) in parts)
        {
            var obj = new JsObject(Realm.ObjectPrototype);
            CreateDataPropertyOrThrow(obj, "type", partType, Realm,
                "Intl.ListFormat.prototype.formatToParts");
            CreateDataPropertyOrThrow(obj, "value", partValue, Realm,
                "Intl.ListFormat.prototype.formatToParts");
            result.Push(new JsValue(obj));
        }

        return JsValue.FromJsArray(result);
    }

    [JsHostMethod("resolvedOptions", Length = 0d)]
    private JsValue ResolvedOptions(JsValue thisValue, IReadOnlyList<JsValue> _)
    {
        var instance = ValidateReceiver(thisValue);
        var obj = new JsObject(Realm.ObjectPrototype);
        const string operation = "Intl.ListFormat.prototype.resolvedOptions";

        var locale = GetStringSlot(instance, LocaleSlot, "en");
        var type = GetStringSlot(instance, TypeSlot, "conjunction");
        var style = GetStringSlot(instance, StyleSlot, "long");

        CreateDataPropertyOrThrow(obj, "locale", locale, Realm, operation);
        CreateDataPropertyOrThrow(obj, "type", type, Realm, operation);
        CreateDataPropertyOrThrow(obj, "style", style, Realm, operation);

        return new JsValue(obj);
    }

    private JsObject ValidateReceiver(JsValue candidate)
    {
        return candidate.EnsureBrand(BrandKey, Realm, "Intl.ListFormat method called on incompatible receiver");
    }

    private List<string> StringListFromIterable(JsValue listArg)
    {
        if (listArg.IsUndefined)
        {
            return [];
        }

        if (listArg.IsNull || (!listArg.IsObject && !listArg.IsString))
        {
            throw ThrowTypeError("Intl.ListFormat: list must be iterable", realm: Realm);
        }

        var strings = new List<string>();

        // Use the iterator protocol
        if (listArg.TryGetObject<IJsPropertyAccessor>(out var accessor))
        {
            // Check for Symbol.iterator
            if (!accessor.TryGetProperty(Ast.SymbolKeys.Iterator, out var iteratorMethod) ||
                !iteratorMethod.TryGetObject<IJsCallable>(out var iteratorFn))
            {
                throw ThrowTypeError("Intl.ListFormat: list argument must be iterable", realm: Realm);
            }

            var iteratorResult = iteratorFn.Invoke([], listArg);
            if (!iteratorResult.TryGetObject<IJsPropertyAccessor>(out var iterator))
            {
                throw ThrowTypeError("Intl.ListFormat: iterator result is not an object", realm: Realm);
            }

            if (!iterator.TryGetProperty("next", out var nextMethod) ||
                !nextMethod.TryGetObject<IJsCallable>(out var nextFn))
            {
                throw ThrowTypeError("Intl.ListFormat: iterator has no next method", realm: Realm);
            }

            while (true)
            {
                var next = nextFn.Invoke([], iteratorResult);
                if (!next.TryGetObject<IJsPropertyAccessor>(out var nextObj))
                {
                    break;
                }

                if (nextObj.TryGetProperty("done", out var done) && JsOps.ToBoolean(done))
                {
                    break;
                }

                if (!nextObj.TryGetProperty("value", out var value))
                {
                    continue;
                }

                // Per spec: StringListFromIterable requires all values to be strings
                if (!value.IsString)
                {
                    // Close the iterator before throwing
                    IteratorClose(iterator);
                    throw ThrowTypeError(
                        $"Iterable yielded {JsValueToString(value, Realm)} which is not a string",
                        realm: Realm);
                }

                strings.Add(value.AsString());
            }
        }
        else if (listArg.IsString)
        {
            // String is iterable - iterate over characters, but each char is a string
            var str = listArg.AsString();
            foreach (var ch in str)
            {
                strings.Add(ch.ToString());
            }
        }

        return strings;
    }

    private static void IteratorClose(IJsPropertyAccessor iterator)
    {
        try
        {
            if (iterator.TryGetProperty("return", out var returnMethod) &&
                returnMethod.TryGetObject<IJsCallable>(out var returnFn))
            {
                returnFn.Invoke([], JsValue.FromObjectUnsafe(iterator));
            }
        }
        catch
        {
            // Ignore errors during iterator close per spec
        }
    }

    private static string GetStringSlot(JsObject instance, string key, string defaultValue)
    {
        return instance.TryGetProperty(key, out var value) && value.TryGetString(out var str)
            ? str
            : defaultValue;
    }

    internal static string FormatList(List<string> list, string type, string style, string locale)
    {
        if (list.Count == 0)
        {
            return string.Empty;
        }

        if (list.Count == 1)
        {
            return list[0];
        }

        var language = ExtractLanguage(locale);
        var templates = GetListPatterns(language, type, style);

        if (list.Count == 2)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                templates.Pair, list[0], list[1]);
        }

        // Build up from end: start, middle..., end
        var result = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            templates.End, list[^2], list[^1]);

        for (var i = list.Count - 3; i > 0; i--)
        {
            result = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                templates.Middle, list[i], result);
        }

        result = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            templates.Start, list[0], result);

        return result;
    }

    internal static List<(string Type, string Value)> FormatListToParts(
        List<string> list, string type, string style, string locale)
    {
        var parts = new List<(string Type, string Value)>();

        if (list.Count == 0)
        {
            return parts;
        }

        if (list.Count == 1)
        {
            parts.Add(("element", list[0]));
            return parts;
        }

        var language = ExtractLanguage(locale);
        var templates = GetListPatterns(language, type, style);

        if (list.Count == 2)
        {
            var literal = ExtractLiteral(templates.Pair);
            parts.Add(("element", list[0]));
            if (!string.IsNullOrEmpty(literal))
            {
                parts.Add(("literal", literal));
            }

            parts.Add(("element", list[1]));
            return parts;
        }

        // For 3+ items: start, middle..., end
        parts.Add(("element", list[0]));

        var startLiteral = ExtractLiteral(templates.Start);
        parts.Add(("literal", startLiteral));

        for (var i = 1; i < list.Count - 2; i++)
        {
            parts.Add(("element", list[i]));
            var midLiteral = ExtractLiteral(templates.Middle);
            parts.Add(("literal", midLiteral));
        }

        // Second to last element
        parts.Add(("element", list[^2]));

        var endLiteral = ExtractLiteral(templates.End);
        parts.Add(("literal", endLiteral));

        // Last element
        parts.Add(("element", list[^1]));

        return parts;
    }

    /// <summary>
    /// Extracts the literal text between {0} and {1} in a pattern template.
    /// </summary>
    private static string ExtractLiteral(string template)
    {
        var idx0 = template.IndexOf("{0}", StringComparison.Ordinal);
        var idx1 = template.IndexOf("{1}", StringComparison.Ordinal);
        if (idx0 < 0 || idx1 < 0)
        {
            return template;
        }

        return template[(idx0 + 3)..idx1];
    }

    private static string ExtractLanguage(string locale)
    {
        var dashIndex = locale.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? locale[..dashIndex] : locale;
    }

    internal static ListPatterns GetListPatterns(string language, string type, string style)
    {
        // CLDR-based list patterns for common locales
        return (language, type, style) switch
        {
            // English conjunction (CLDR: en)
            ("en", "conjunction", "long") => new ListPatterns("{0} and {1}", "{0}, {1}", "{0}, {1}", "{0}, and {1}"),
            ("en", "conjunction", "short") => new ListPatterns("{0} & {1}", "{0}, {1}", "{0}, {1}", "{0}, & {1}"),
            ("en", "conjunction", "narrow") => new ListPatterns("{0}, {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),

            // English disjunction (CLDR: en)
            ("en", "disjunction", "long") => new ListPatterns("{0} or {1}", "{0}, {1}", "{0}, {1}", "{0}, or {1}"),
            ("en", "disjunction", "short") => new ListPatterns("{0} or {1}", "{0}, {1}", "{0}, {1}", "{0}, or {1}"),
            ("en", "disjunction", "narrow") => new ListPatterns("{0} or {1}", "{0}, {1}", "{0}, {1}", "{0}, or {1}"),

            // English unit (CLDR: en)
            ("en", "unit", "long") => new ListPatterns("{0}, {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),
            ("en", "unit", "short") => new ListPatterns("{0}, {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),
            ("en", "unit", "narrow") => new ListPatterns("{0} {1}", "{0} {1}", "{0} {1}", "{0} {1}"),

            // Spanish conjunction (CLDR: es)
            ("es", "conjunction", "long") => new ListPatterns("{0} y {1}", "{0}, {1}", "{0}, {1}", "{0} y {1}"),
            ("es", "conjunction", "short") => new ListPatterns("{0} y {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),
            ("es", "conjunction", "narrow") => new ListPatterns("{0}, {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),

            // Spanish disjunction (CLDR: es)
            ("es", "disjunction", "long") => new ListPatterns("{0} o {1}", "{0}, {1}", "{0}, {1}", "{0} o {1}"),
            ("es", "disjunction", "short") => new ListPatterns("{0} o {1}", "{0}, {1}", "{0}, {1}", "{0} o {1}"),
            ("es", "disjunction", "narrow") => new ListPatterns("{0} o {1}", "{0}, {1}", "{0}, {1}", "{0} o {1}"),

            // Spanish unit (CLDR: es)
            ("es", "unit", "long") => new ListPatterns("{0} y {1}", "{0}, {1}", "{0}, {1}", "{0} y {1}"),
            ("es", "unit", "short") => new ListPatterns("{0} y {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),
            ("es", "unit", "narrow") => new ListPatterns("{0} {1}", "{0} {1}", "{0} {1}", "{0} {1}"),

            // German conjunction (CLDR: de)
            ("de", "conjunction", "long") => new ListPatterns("{0} und {1}", "{0}, {1}", "{0}, {1}", "{0} und {1}"),
            ("de", "conjunction", _) => new ListPatterns("{0} und {1}", "{0}, {1}", "{0}, {1}", "{0} und {1}"),

            // French conjunction (CLDR: fr)
            ("fr", "conjunction", "long") => new ListPatterns("{0} et {1}", "{0}, {1}", "{0}, {1}", "{0} et {1}"),
            ("fr", "conjunction", _) => new ListPatterns("{0} et {1}", "{0}, {1}", "{0}, {1}", "{0} et {1}"),

            // Japanese (CLDR: ja)
            ("ja", "conjunction", "long") => new ListPatterns("{0}、{1}", "{0}、{1}", "{0}、{1}", "{0}、{1}"),
            ("ja", _, _) => new ListPatterns("{0}、{1}", "{0}、{1}", "{0}、{1}", "{0}、{1}"),

            // Chinese (CLDR: zh)
            ("zh", "conjunction", "long") => new ListPatterns("{0}和{1}", "{0}、{1}", "{0}、{1}", "{0}和{1}"),
            ("zh", _, _) => new ListPatterns("{0}、{1}", "{0}、{1}", "{0}、{1}", "{0}、{1}"),

            // Default: English-like patterns
            (_, "disjunction", _) => new ListPatterns("{0} or {1}", "{0}, {1}", "{0}, {1}", "{0}, or {1}"),
            (_, "unit", "narrow") => new ListPatterns("{0} {1}", "{0} {1}", "{0} {1}", "{0} {1}"),
            (_, "unit", _) => new ListPatterns("{0}, {1}", "{0}, {1}", "{0}, {1}", "{0}, {1}"),
            _ => new ListPatterns("{0} and {1}", "{0}, {1}", "{0}, {1}", "{0}, and {1}")
        };
    }

    internal readonly record struct ListPatterns(string Pair, string Start, string Middle, string End);
}
