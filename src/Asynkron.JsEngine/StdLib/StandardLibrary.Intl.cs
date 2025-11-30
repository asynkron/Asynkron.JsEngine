using System.Collections.Generic;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib.Intl;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateIntlObject(RealmState realm)
    {
        var intl = new JsObject(realm.ObjectPrototype);

        var toStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        intl.DefineProperty(toStringTagKey,
            new PropertyDescriptor
            {
                Value = "Intl",
                Writable = false,
                Enumerable = false,
                Configurable = true
            });

        var localeCtor = IntlLocaleConstructor.CreateConstructor(realm);
        intl.DefineProperty("Locale",
            new PropertyDescriptor
            {
                Value = localeCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var durationFormatCtor = IntlDurationFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("DurationFormat",
            new PropertyDescriptor
            {
                Value = durationFormatCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var collatorCtor = IntlCollatorConstructor.CreateConstructor(realm);
        intl.DefineProperty("Collator",
            new PropertyDescriptor { Value = collatorCtor, Writable = true, Enumerable = false, Configurable = true });

        var dateTimeFormatCtor = IntlDateTimeFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("DateTimeFormat",
            new PropertyDescriptor
            {
                Value = dateTimeFormatCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var numberFormatCtor = IntlNumberFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("NumberFormat",
            new PropertyDescriptor
            {
                Value = numberFormatCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var relativeTimeFormatCtor = IntlRelativeTimeFormatConstructor.CreateConstructor(realm);
        intl.DefineProperty("RelativeTimeFormat",
            new PropertyDescriptor
            {
                Value = relativeTimeFormatCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var displayNamesCtor = IntlDisplayNamesConstructor.CreateConstructor(realm);
        intl.DefineProperty("DisplayNames",
            new PropertyDescriptor
            {
                Value = displayNamesCtor, Writable = true, Enumerable = false, Configurable = true
            });

        var getCanonicalLocales = new HostFunction(args => CreateCanonicalLocalesResult(args), realm,
            isConstructor: false);
        getCanonicalLocales.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        getCanonicalLocales.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "getCanonicalLocales", Writable = false, Enumerable = false, Configurable = true
            });
        getCanonicalLocales.Delete("prototype");

        intl.DefineProperty("getCanonicalLocales",
            new PropertyDescriptor
            {
                Value = getCanonicalLocales, Writable = true, Enumerable = false, Configurable = true
            });

        var supportedValuesOf =
            new HostFunction(args => CreateSupportedValuesResult(args), realm, isConstructor: false);
        supportedValuesOf.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedValuesOf.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedValuesOf", Writable = false, Enumerable = false, Configurable = true
            });
        supportedValuesOf.Delete("prototype");

        intl.DefineProperty("supportedValuesOf",
            new PropertyDescriptor
            {
                Value = supportedValuesOf, Writable = true, Enumerable = false, Configurable = true
            });

        return intl;

        JsArray CreateCanonicalLocalesResult(IReadOnlyList<object?> args)
        {
            var localesArg = args.GetArgument(0);
            var canonicalized = IntlUtilities.CanonicalizeLocaleList(localesArg, realm);
            return CreateLocaleArray(canonicalized, realm);
        }

        JsArray CreateSupportedValuesResult(IReadOnlyList<object?> args)
        {
            var keyValue = args.GetArgument(0);
            var key = JsValueToString(keyValue, realm);
            var values = IntlUtilities.GetSupportedValues(key, realm);
            var result = new JsArray(realm);
            foreach (var value in values)
            {
                result.Push(value);
            }
            return result;
        }
    }

    internal static (IReadOnlyList<string> RequestedLocales, string ResolvedLocale) ResolveIntlLocales(
        object? localesArg,
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

    internal static JsArray ResolveSupportedLocales(object? localesArg, object? optionsArg, RealmState realm)
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

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject();
        var durationPrototype = new JsObject(realm.ObjectPrototype);

        var durationCtor = new HostFunction((thisValue, args) =>
        {
            var instance = thisValue as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            if (args.Count == 0 || args[0] is not JsObject source)
            {
                return instance;
            }

            foreach (var key in source.Keys)
            {
                instance.SetProperty(key, source[key]);
            }

            return instance;
        }) { IsConstructor = true };

        var durationFrom = new HostFunction(args =>
        {
            var input = args.Count > 0 && args[0] is JsObject jsObj ? jsObj : new JsObject();
            var instance = durationCtor.Invoke([input], null) as JsObject ?? new JsObject();
            instance.SetPrototype(durationPrototype);
            return instance;
        }, isConstructor: false);

        durationCtor.DefineProperty("from",
            new PropertyDescriptor { Value = durationFrom, Writable = true, Enumerable = false, Configurable = true });

        var durationToLocaleString = new HostFunction(DurationToLocaleString, isConstructor: false);
        durationPrototype.SetProperty("toLocaleString", durationToLocaleString);

        durationCtor.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = durationPrototype, Writable = false, Enumerable = false, Configurable = false
            });
        durationPrototype.DefineProperty("constructor",
            new PropertyDescriptor { Value = durationCtor, Writable = true, Enumerable = false, Configurable = true });

        temporal.SetProperty("Duration", durationCtor);
        return temporal;

        object? DurationToLocaleString(object? thisValue, IReadOnlyList<object?> args)
        {
            var locale = args.GetArgument(0);
            var options = args.GetArgument(1);
            if (Symbol.Undefined.Equals(locale) && args.Count > 0)
            {
                locale = args[0];
            }

            var formatterObj = CreateIntlObject(realm).TryGetProperty("DurationFormat", out var ctorVal) &&
                               ctorVal is IJsCallable durationFormatCtor
                ? durationFormatCtor.Invoke([locale, options], null)
                : new JsObject();

            if (formatterObj is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("format", out var formatVal) &&
                formatVal is IJsCallable formatFn)
            {
                return formatFn.Invoke([thisValue], formatterObj);
            }

            return "";
        }
    }
}
