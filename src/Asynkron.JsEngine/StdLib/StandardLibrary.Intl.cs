using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib.Intl;

namespace Asynkron.JsEngine.StdLib;

public static partial class StandardLibrary
{
    public static JsObject CreateIntlObject(RealmState realm)
    {
        var intl = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            intl.SetPrototype(realm.ObjectPrototype);
        }

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
        intl.SetProperty("Locale", localeCtor);

        var durationFormatCtor = IntlDurationFormatConstructor.CreateConstructor(realm);
        intl.SetProperty("DurationFormat", durationFormatCtor);

        var collatorCtor = IntlCollatorConstructor.CreateConstructor(realm);
        intl.SetProperty("Collator", collatorCtor);

        var dateTimeFormatCtor = IntlDateTimeFormatConstructor.CreateConstructor(realm);
        intl.SetProperty("DateTimeFormat", dateTimeFormatCtor);

        var numberFormatCtor = IntlNumberFormatConstructor.CreateConstructor(realm);
        intl.SetProperty("NumberFormat", numberFormatCtor);

        var getCanonicalLocales = new HostFunction(args => CreateCanonicalLocalesResult(args), realm)
        {
            IsConstructor = false
        };
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

        var supportedValuesOf = new HostFunction(args => CreateSupportedValuesResult(args), realm)
        {
            IsConstructor = false
        };
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
            var localesArg = args.Count > 0 ? args[0] : Symbol.Undefined;
            var canonicalized = IntlUtilities.CanonicalizeLocaleList(localesArg, realm);
            var result = new JsArray(realm);
            foreach (var locale in canonicalized)
            {
                result.Push(locale);
            }

            return result;
        }

        JsArray CreateSupportedValuesResult(IReadOnlyList<object?> args)
        {
            var keyValue = args.Count > 0 ? args[0] : Symbol.Undefined;
            var key = JsValueToString(keyValue);
            var values = IntlUtilities.GetSupportedValues(key, realm);
            var result = new JsArray(realm);
            foreach (var value in values)
            {
                result.Push(value);
            }

            return result;
        }
    }

    public static JsObject CreateTemporalObject(RealmState realm)
    {
        var temporal = new JsObject();
        var durationPrototype = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            durationPrototype.SetPrototype(realm.ObjectPrototype);
        }

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
        }) { IsConstructor = false };

        durationCtor.DefineProperty("from",
            new PropertyDescriptor { Value = durationFrom, Writable = true, Enumerable = false, Configurable = true });

        var durationToLocaleString = new HostFunction(DurationToLocaleString) { IsConstructor = false };
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
            var locale = args.Count > 0 ? args[0] : Symbol.Undefined;
            var options = args.Count > 1 ? args[1] : Symbol.Undefined;
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
