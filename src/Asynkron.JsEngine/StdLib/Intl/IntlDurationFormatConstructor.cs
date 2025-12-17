using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.DurationFormat", PrototypeType = typeof(IntlDurationFormatPrototype), Length = 0d,
    DisplayName = "DurationFormat")]
public sealed partial class IntlDurationFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(localesArg, Realm);
        var instance = PrepareThisObject(thisValue);
        IntlDurationFormatPrototype.InitializeInternalSlots(instance, resolvedLocale);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocales = new HostFunction(SupportedLocalesOf, isConstructor: false);
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

    private JsValue SupportedLocalesOf(IReadOnlyList<JsValue> args)
    {
        var result = StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), args.GetArgument(1), Realm);
        return JsValue.FromObjectUnsafe(result);
    }
}
