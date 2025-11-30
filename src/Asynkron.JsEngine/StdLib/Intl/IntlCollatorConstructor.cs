using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Collator", PrototypeType = typeof(IntlCollatorPrototype), Length = 0d, DisplayName = "Collator")]
public sealed partial class IntlCollatorConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var (_, resolvedLocale) = StandardLibrary.ResolveIntlLocales(args.GetArgument(0), Realm);
        var instance = PrepareThisObject(thisValue);
        IntlCollatorPrototype.InitializeInternalSlots(instance, resolvedLocale);
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
    }

    private JsArray SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        return StandardLibrary.ResolveSupportedLocales(args.GetArgument(0), Realm);
    }
}
