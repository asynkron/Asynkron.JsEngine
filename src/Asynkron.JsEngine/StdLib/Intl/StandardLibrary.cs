using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib.Intl;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateIntlObject(RealmState realm)
    {
        var intl = new JsObject(realm.ObjectPrototype);

        var toStringTagKey = SymbolKeys.ToStringTag;
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

        var getCanonicalLocales = new HostFunction(args => JsValue.FromObjectUnsafe(CreateCanonicalLocalesResult(args)), realm,
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
            new HostFunction(args => JsValue.FromObjectUnsafe(CreateSupportedValuesResult(args)), realm, isConstructor: false);
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

        JsArray CreateCanonicalLocalesResult(IReadOnlyList<JsValue> args)
        {
            var localesArg = args.GetArgument(0);
            var canonicalized = IntlUtilities.CanonicalizeLocaleList(localesArg.ToObject(), realm);
            return CreateLocaleArray(canonicalized, realm);
        }

        JsArray CreateSupportedValuesResult(IReadOnlyList<JsValue> args)
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
        JsValue localesArg,
        RealmState realm)
    {
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg.ToObject(), realm);
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
        var requestedLocales = IntlUtilities.CanonicalizeLocaleList(localesArg.ToObject(), realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg.ToObject(), realm, "supportedLocalesOf");
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
        }, realm) { IsConstructor = true };

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
            var result = durationCtor.Invoke([new JsValue(input)], JsValue.Undefined);
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
        }, realm, isConstructor: false);

        durationCtor.DefineProperty("from",
            new PropertyDescriptor { Value = durationFrom, Writable = true, Enumerable = false, Configurable = true });

        var durationToLocaleString = new HostFunction(args => DurationToLocaleString(JsValue.Undefined, args), realm, isConstructor: false);
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
                return formatFn.Invoke([thisValue], formatterObj);
            }

            return new JsValue("");
        }
    }
}
