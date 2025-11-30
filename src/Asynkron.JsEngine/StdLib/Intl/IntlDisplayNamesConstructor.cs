using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DisplayNames", PrototypeType = typeof(IntlDisplayNamesPrototype), Length = 2d,
    DisplayName = "DisplayNames")]
public sealed partial class IntlDisplayNamesConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private static readonly string[] SupportedTypes =
        ["language", "region", "script", "currency", "calendar", "dateTimeField"];

    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(localesArg, Realm);

        var options = NormalizeOptions(optionsArg);
        var localeMatcher = ReadStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var style = ReadStringOption(options, "style", ["long", "short", "narrow"], "long");
        var type = ReadStringOption(options, "type", SupportedTypes, null, requireValue: true);
        var fallback = ReadStringOption(options, "fallback", ["code", "none"], "code");
        var languageDisplay = ReadStringOption(options, "languageDisplay", ["dialect", "standard"], "dialect");

        _ = localeMatcher;

        var instance = PrepareThisObject(thisValue);
        IntlDisplayNamesPrototype.InitializeInternalSlots(
            instance,
            resolvedLocale,
            style,
            type,
            fallback,
            languageDisplay);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction((_, args) =>
        {
            return StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), Realm);
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

        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
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

    private string ReadStringOption(JsObject? options, string propertyName, IReadOnlyList<string> allowed,
        string? defaultValue, bool requireValue = false)
    {
        if (options is null ||
            !options.TryGetProperty(propertyName, out var value) ||
            ReferenceEquals(value, Symbol.Undefined))
        {
            if (requireValue)
            {
                throw StandardLibrary.ThrowTypeError(
                    $"Intl.DisplayNames requires a {propertyName} option", realm: Realm);
            }

            return defaultValue ?? string.Empty;
        }

        var str = StandardLibrary.JsValueToString(value, Realm);

        if (allowed is not null && !allowed.Contains(str, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.DisplayNames {propertyName} option '{str}' is not supported", realm: Realm);
        }

        return str;
    }
}
