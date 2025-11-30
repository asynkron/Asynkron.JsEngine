using System.Globalization;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.NumberFormat", PrototypeType = typeof(IntlNumberFormatPrototype), Length = 0d,
    DisplayName = "NumberFormat")]
public sealed partial class IntlNumberFormatConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var localesArg = args.Count > 0 ? args[0] : Symbol.Undefined;
        var optionsArg = args.Count > 1 ? args[1] : Symbol.Undefined;
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg, Realm);
        var resolvedLocale = requestedLocales.Count > 0
            ? requestedLocales[0]
            : CultureInfo.CurrentCulture.Name;
        var numberingSystem = ReadNumberingSystem(optionsArg);

        var instance = PrepareThisObject(thisValue);
        IntlNumberFormatPrototype.InitializeInternalSlots(instance, Realm);
        instance.SetProperty("__locale__", resolvedLocale);
        instance.SetProperty("__numberingSystem__", numberingSystem);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction(SupportedLocalesOf, isConstructor: false);
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

    private object? SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        var result = new JsArray(Realm);
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return result;
        }

        var locales = args[0];
        if (locales is string single)
        {
            result.Push(single);
            return result;
        }

        if (locales is not JsArray { Items.Count: > 0 } array || array.Items[0] is not string firstLocale)
        {
            return result;
        }

        result.Push(firstLocale);
        return result;

    }

    private string ReadNumberingSystem(object? optionsArg)
    {
        if (optionsArg is null)
        {
            throw StandardLibrary.ThrowTypeError("Intl.NumberFormat options must be an object", realm: Realm);
        }

        if (ReferenceEquals(optionsArg, Symbol.Undefined))
        {
            return "latn";
        }

        if (optionsArg is not JsObject options)
        {
            throw StandardLibrary.ThrowTypeError("Intl.NumberFormat options must be an object", realm: Realm);
        }

        if (!options.TryGetProperty("numberingSystem", out var rawValue) ||
            rawValue is null || ReferenceEquals(rawValue, Symbol.Undefined))
        {
            return "latn";
        }

        if (rawValue is not string numberingSystem)
        {
            throw StandardLibrary.ThrowTypeError(
                "Intl.NumberFormat numberingSystem option must be a string", realm: Realm);
        }

        return IntlUtilities.TryNormalizeNumberingSystem(numberingSystem, out var canonical)
            ? canonical
            : "latn";
    }
}
