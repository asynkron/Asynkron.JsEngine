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

        return intl;
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
            if (args.Count <= 0 || args[0] is not JsObject source)
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
