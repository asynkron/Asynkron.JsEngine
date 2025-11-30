using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.NumberFormat", PrototypeType = typeof(IntlNumberFormatPrototype), Length = 0d,
    DisplayName = "NumberFormat")]
public sealed partial class IntlNumberFormatConstructor(JsObject prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var instance = PrepareThisObject(thisValue);
        IntlNumberFormatPrototype.InitializeInternalSlots(instance, Realm);
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
        supportedLocalesOf.SetPrototype(constructor.Prototype);
        supportedLocalesOf.Delete("prototype");
    }

    private object? SupportedLocalesOf(IReadOnlyList<object?> args)
    {
        var result = new JsArray(Realm);
        if (args.Count == 0 || args[0] is null || ReferenceEquals(args[0], Symbol.Undefined))
        {
            return result;
        }

        var locales = args[0];
        if (locales is string single)
        {
            result.Push(single);
            return result;
        }

        if (locales is JsArray array && array.Items.Count > 0 && array.Items[0] is string firstLocale)
        {
            result.Push(firstLocale);
            return result;
        }

        return result;
    }
}
