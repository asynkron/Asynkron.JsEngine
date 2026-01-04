#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DisplayNames", PrototypeType = typeof(IntlDisplayNamesPrototype), Length = 2d,
    DisplayName = "DisplayNames")]
public sealed partial class IntlDisplayNamesConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private static readonly string[] SupportedTypes =
        ["language", "region", "script", "currency", "calendar", "dateTimeField"];

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);

        var options = NormalizeOptions(optionsArg);
        var localeMatcher = ReadStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var style = ReadStringOption(options, "style", ["long", "short", "narrow"], "long");
        var type = ReadStringOption(options, "type", SupportedTypes, null, true);
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
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private IJsPropertyAccessor? NormalizeOptions(JsValue optionsArg)
    {
        return IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "DisplayNames");
    }

    private string ReadStringOption(IJsPropertyAccessor? options, string propertyName, IReadOnlyList<string>? allowed,
        string? defaultValue, bool requireValue = false)
    {
        return IntlOptionHelpers.GetStringOption(options, propertyName, Realm, "DisplayNames", allowed, defaultValue,
            requireValue);
    }
}
