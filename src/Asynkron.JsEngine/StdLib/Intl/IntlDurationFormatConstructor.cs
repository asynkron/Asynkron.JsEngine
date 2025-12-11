using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DurationFormat", PrototypeType = typeof(IntlDurationFormatPrototype), Length = 0d,
    DisplayName = "DurationFormat")]
public sealed partial class IntlDurationFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var localesArg = args.GetArgument(0);
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(localesArg, Realm);
        var instance = PrepareThisObject(thisValue);
        IntlDurationFormatPrototype.InitializeInternalSlots(instance, resolvedLocale);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocales = new HostFunction(
            args => StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), Realm),
            isConstructor: false);

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

        supportedLocales.SetPrototype(constructor.Prototype);
        supportedLocales.Delete("prototype");
    }
}
