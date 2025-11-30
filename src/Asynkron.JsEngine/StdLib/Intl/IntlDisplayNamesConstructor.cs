using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DisplayNames", PrototypeType = typeof(IntlDisplayNamesPrototype), Length = 1d,
    DisplayName = "DisplayNames")]
public sealed partial class IntlDisplayNamesConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private static readonly string[] SupportedTypes =
        ["language", "region", "script", "currency", "calendar", "dateTimeField"];

    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var localesArg = args.Count > 0 ? args[0] : Symbol.Undefined;
        var optionsArg = args.Count > 1 ? args[1] : Symbol.Undefined;

        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg, Realm);
        var locale = IntlUtilities.ResolveRequestedLocale(requestedLocales);

        var options = NormalizeOptions(optionsArg);
        var type = ReadStringOption(options, "type", SupportedTypes, null)
                   ?? throw StandardLibrary.ThrowTypeError(
                       "Intl.DisplayNames requires a type option", realm: Realm);
        var style = ReadStringOption(options, "style", ["long", "short", "narrow"], "long")!;
        var fallback = ReadStringOption(options, "fallback", ["code", "none"], "code")!;
        var languageDisplay = ReadStringOption(options, "languageDisplay", ["dialect", "standard"], "dialect")!;

        var instance = PrepareThisObject(thisValue);
        IntlDisplayNamesPrototype.InitializeInternalSlots(instance, locale, style, type, fallback, languageDisplay);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction((_, args) =>
        {
            var localeList = IntlUtilities.CanonicalizeLocaleList(
                args.Count > 0 ? args[0] : Symbol.Undefined,
                Realm);
            var result = new JsArray(Realm);
            foreach (var locale in localeList)
            {
                result.Push(locale);
            }

            return result;
        }, isConstructor: false);

        supportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocalesOf, Writable = true, Enumerable = false, Configurable = true
            });
    }

    private JsObject? NormalizeOptions(object? optionsArg)
    {
        if (optionsArg is null)
        {
            throw StandardLibrary.ThrowTypeError("Intl.DisplayNames options must be an object", realm: Realm);
        }

        if (ReferenceEquals(optionsArg, Symbol.Undefined))
        {
            return null;
        }

        if (optionsArg is JsObject jsObject)
        {
            return jsObject;
        }

        throw StandardLibrary.ThrowTypeError("Intl.DisplayNames options must be an object", realm: Realm);
    }

    private string? ReadStringOption(JsObject? options, string propertyName, IReadOnlyList<string> allowed,
        string? defaultValue)
    {
        if (options is null ||
            !options.TryGetProperty(propertyName, out var value) ||
            value is null || ReferenceEquals(value, Symbol.Undefined))
        {
            return defaultValue;
        }

        if (value is not string str)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.DisplayNames {propertyName} option must be a string", realm: Realm);
        }

        if (!allowed.Contains(str, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.DisplayNames {propertyName} option '{str}' is not supported", realm: Realm);
        }

        return str;
    }
}
