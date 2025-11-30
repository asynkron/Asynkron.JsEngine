using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DurationFormat", PrototypeType = typeof(IntlDurationFormatPrototype), Length = 0d,
    DisplayName = "DurationFormat")]
public sealed partial class IntlDurationFormatConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        return PrepareThisObject(thisValue);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocales = new HostFunction(args =>
        {
            var result = new JsArray(Realm);
            if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
            {
                return result;
            }

            var locales = args[0];
            if (locales is string localeString)
            {
                result.Push(localeString);
                return result;
            }

            if (locales is JsArray localesArray)
            {
                foreach (var item in localesArray.Items)
                {
                    if (item is string locale)
                    {
                        result.Push(locale);
                        continue;
                    }

                    throw ThrowTypeError("Invalid locale value", realm: Realm);
                }

                return result;
            }

            throw ThrowTypeError("Invalid locales argument", realm: Realm);
        }, isConstructor: false);

        supportedLocales.DefineProperty("length",
            new PropertyDescriptor { Value = 1d, Writable = false, Enumerable = false, Configurable = true });
        supportedLocales.DefineProperty("name",
            new PropertyDescriptor
            {
                Value = "supportedLocalesOf", Writable = false, Enumerable = false, Configurable = true
            });

        constructor.DefineProperty("supportedLocalesOf",
            new PropertyDescriptor
            {
                Value = supportedLocales, Writable = true, Enumerable = false, Configurable = true
            });
    }
}
