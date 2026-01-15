#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using Asynkron.JsEngine.StdLib.Intl;

#endregion

namespace Asynkron.JsEngine.StdLib;

public static partial class IntlHelper
{
    /// <summary>
    /// Intl.getCanonicalLocales() - returns an array containing the canonical locale names.
    /// </summary>
    [JsHostFunction("getCanonicalLocales", Length = 1d, DeletePrototype = true)]
    private static JsValue GetCanonicalLocales(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var localesArg = args.GetArgument(0);
        var canonicalized = IntlUtilities.CanonicalizeLocaleList(localesArg, realm);
        return JsValue.FromJsArray(CreateLocaleArray(canonicalized, realm));
    }

    /// <summary>
    /// Intl.supportedValuesOf() - returns an array containing the supported values for a given key.
    /// </summary>
    [JsHostFunction("supportedValuesOf", Length = 1d, DeletePrototype = true)]
    private static JsValue SupportedValuesOf(IReadOnlyList<JsValue> args, RealmState realm)
    {
        var keyValue = args.GetArgument(0);
        var key = StandardLibrary.JsValueToString(keyValue, realm);
        var values = IntlUtilities.GetSupportedValues(key, realm);
        var result = new JsArray(realm);
        foreach (var value in values)
        {
            result.Push(value);
        }

        return JsValue.FromJsArray(result);
    }

    public static JsObject CreateIntlObject(RealmState realm)
    {
        var intl = new JsObject(realm.ObjectPrototype);

        var toStringTagKey = SymbolKeys.ToStringTag;
        intl.DefineProperty(toStringTagKey,
            new PropertyDescriptor { Value = "Intl", Writable = false, Enumerable = false, Configurable = true });

        var localeCtor = IntlLocaleConstructor.CreateConstructor(realm);
        intl.DefineProperty("Locale",
            new PropertyDescriptor { Value = localeCtor, Writable = true, Enumerable = false, Configurable = true });

        var durationFormatCtor = IntlDurationFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("DurationFormat",
            new PropertyDescriptor
            {
                Value = durationFormatCtor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        var collatorCtor = IntlCollatorConstructor.CreateConstructor(realm);
        intl.DefineProperty("Collator",
            new PropertyDescriptor { Value = collatorCtor, Writable = true, Enumerable = false, Configurable = true });

        var dateTimeFormatCtor = IntlDateTimeFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("DateTimeFormat",
            new PropertyDescriptor
            {
                Value = dateTimeFormatCtor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        var numberFormatCtor = IntlNumberFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("NumberFormat",
            new PropertyDescriptor
            {
                Value = numberFormatCtor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        var relativeTimeFormatCtor = IntlRelativeTimeFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("RelativeTimeFormat",
            new PropertyDescriptor
            {
                Value = relativeTimeFormatCtor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        var displayNamesCtor = IntlDisplayNamesConstructor.CreateConstructor(realm);
        intl.DefineProperty("DisplayNames",
            new PropertyDescriptor
            {
                Value = displayNamesCtor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        // Register getCanonicalLocales and supportedValuesOf via source-generated registration
        RegisterHostFunctions(intl, realm);

        return intl;
    }

    internal static (IReadOnlyList<string> RequestedLocales, string ResolvedLocale) ResolveIntlLocales(
        JsValue localesArg,
        RealmState realm)
    {
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg, realm);
        var resolvedLocale = IntlUtilities.ResolveRequestedLocale(requestedLocales);
        return (requestedLocales, resolvedLocale);
    }

    internal static JsArray CreateLocaleArray(IEnumerable<string> locales, RealmState realm)
    {
        var result = new JsArray(realm);
        foreach (var locale in locales)
        {
            result.Push(locale);
        }

        return result;
    }

    internal static JsArray ResolveSupportedLocales(JsValue localesArg, JsValue optionsArg, RealmState realm)
    {
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg, realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, realm, "supportedLocalesOf");
        var _ = IntlOptionHelpers.GetStringOption(
            options,
            "localeMatcher",
            realm,
            "supportedLocalesOf",
            ["lookup", "best fit"],
            "best fit");
        var supported = IntlUtilities.FilterSupportedLocales(requestedLocales);
        return CreateLocaleArray(supported, realm);
    }

    /// <summary>
    /// Implementation for Intl.*.supportedLocalesOf() - shared across all Intl constructors.
    /// </summary>
    private static JsValue SupportedLocalesOfImpl(IReadOnlyList<JsValue> args, RealmState realm)
    {
        return JsValue.FromJsArray(
            ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), realm));
    }

    /// <summary>
    /// Configures the supportedLocalesOf static method on an Intl constructor.
    /// </summary>
    internal static void ConfigureSupportedLocalesOf(HostFunction constructor, RealmState realm)
    {
        var supportedLocalesOf = new HostFunction(
            args => SupportedLocalesOfImpl(args, realm),
            isConstructor: false);

        supportedLocalesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocalesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocalesOf,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
    }

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject();
        var durationPrototype = new JsObject(realm.ObjectPrototype);

        var durationCtor = new HostFunction((thisValue, args) =>
        {
            JsObject instance;
            if (!thisValue.TryGetObject<JsObject>(out var existingInstance))
            {
                instance = new JsObject();
            }
            else
            {
                instance = existingInstance;
            }

            instance.SetPrototype(durationPrototype);
            if (args.Count == 0 || !args[0].TryGetObject<JsObject>(out var source))
            {
                return new JsValue(instance);
            }

            foreach (var key in source.Keys)
            {
                if (source.TryGetProperty(key, out var propValue))
                {
                    instance.SetProperty(key, propValue);
                }
            }

            return new JsValue(instance);
        }, realm)
        { IsConstructor = true };

        var durationFrom = new HostFunction(args =>
        {
            JsObject input;
            if (args.Count > 0 && args[0].TryGetObject<JsObject>(out var jsObj))
            {
                input = jsObj;
            }
            else
            {
                input = new JsObject();
            }

            var result = durationCtor.Invoke(new SingleValueArgs(new JsValue(input)), JsValue.Undefined);
            JsObject instance;
            if (!result.TryGetObject<JsObject>(out var resultObj))
            {
                instance = new JsObject();
            }
            else
            {
                instance = resultObj;
            }

            instance.SetPrototype(durationPrototype);
            return new JsValue(instance);
        }, realm, false);

        durationCtor.DefineProperty("from",
            new PropertyDescriptor { Value = durationFrom, Writable = true, Enumerable = false, Configurable = true });

        var durationToLocaleString =
            new HostFunction(args => DurationToLocaleString(JsValue.Undefined, args), realm, false);
        durationPrototype.SetProperty("toLocaleString", durationToLocaleString);

        durationCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = durationPrototype,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });
        durationPrototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = durationCtor, Writable = true, Enumerable = false, Configurable = true });

        temporal.SetProperty("Duration", durationCtor);
        return temporal;

        JsValue DurationToLocaleString(JsValue thisValue, IReadOnlyList<JsValue> args)
        {
            var locale = args.GetArgument(0);
            var options = args.GetArgument(1);
            if (locale.IsUndefined && args.Count > 0)
            {
                locale = args[0];
            }

            JsValue formatterObj;
            if (CreateIntlObject(realm).TryGetProperty("DurationFormat", out var ctorVal) &&
                ctorVal.TryGetObject<IJsCallable>(out var durationFormatCtor))
            {
                formatterObj = durationFormatCtor.Invoke([locale, options], JsValue.Undefined);
            }
            else
            {
                formatterObj = new JsValue(new JsObject());
            }

            if (formatterObj.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
                accessor.TryGetProperty("format", out var formatVal) &&
                formatVal.TryGetObject<IJsCallable>(out var formatFn))
            {
                return formatFn.Invoke(new SingleValueArgs(thisValue), formatterObj);
            }

            return new JsValue("");
        }
    }
}
