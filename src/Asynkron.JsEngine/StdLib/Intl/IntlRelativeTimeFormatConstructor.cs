using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.RelativeTimeFormat", PrototypeType = typeof(IntlRelativeTimeFormatPrototype), Length = 0d,
    DisplayName = "RelativeTimeFormat")]
public sealed partial class IntlRelativeTimeFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(localesArg, Realm);
        var options = NormalizeOptions(optionsArg);
        var numberingSystem = ReadNumberingSystem(options);
        var numeric = ReadStringOption(options, "numeric", ["always", "auto"], "always");
        var style = ReadStringOption(options, "style", ["long", "short", "narrow"], "long");

        var instance = PrepareThisObject(thisValue);
        IntlRelativeTimeFormatPrototype.InitializeInternalSlots(instance, resolvedLocale, numberingSystem, numeric,
            style);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction(
            (_, args) => JsValue.FromObjectUnsafe(StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), Realm)),
            isConstructor: false);

        supportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true,
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocalesOf, Writable = true, Enumerable = false, Configurable = true,
            });

        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
    }

    private JsObject? NormalizeOptions(JsValue optionsArg)
    {
        if (optionsArg.IsNullOrUndefined)
        {
            return null;
        }

        if (optionsArg.IsObject && optionsArg.AsObject() is JsObject jsObject)
        {
            return jsObject;
        }

        throw StandardLibrary.ThrowTypeError("Intl.RelativeTimeFormat options must be an object", realm: Realm);
    }

    private string ReadNumberingSystem(JsObject? options)
    {
        if (options is null || !options.TryGetProperty("numberingSystem", out var value) ||
            value.IsUndefined)
        {
            return "latn";
        }

        if (!value.TryGetString(out var numberingSystem))
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.RelativeTimeFormat numberingSystem option must be a string", realm: Realm);
        }
        if (!IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Unsupported numberingSystem '{numberingSystem}'", realm: Realm);
        }

        return canonical;
    }

    private string ReadStringOption(JsObject? options, string propertyName, IReadOnlyList<string> allowed,
        string defaultValue)
    {
        if (options is null || !options.TryGetProperty(propertyName, out var rawValue) ||
            rawValue.IsUndefined)
        {
            return defaultValue;
        }

        if (!rawValue.TryGetString(out var strValue))
        {
            throw StandardLibrary.ThrowTypeError(
                $"Intl.RelativeTimeFormat {propertyName} option must be a string", realm: Realm);
        }
        if (!allowed.Contains(strValue, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowRangeError(
                $"Intl.RelativeTimeFormat {propertyName} option '{strValue}' is not supported", realm: Realm);
        }

        return strValue;
    }
}
