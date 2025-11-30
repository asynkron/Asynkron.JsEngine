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
        var instance = PrepareThisObject(thisValue);
        IntlCollatorPrototype.InitializeInternalSlots(instance);
        return instance;
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        var supportedLocalesOf = new HostFunction(SupportedLocalesOf) { IsConstructor = false };
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

    private object? SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        var result = new JsArray(Realm);
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return result;
        }

        var locales = args[0];
        switch (locales)
        {
            case string single:
                result.Push(single);
                return result;
            case JsArray { Items.Count: > 0 } array when array.Items[0] is string firstLocale:
                result.Push(firstLocale);
                return result;
            default:
                return result;
        }
    }
}
