using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Locale", PrototypeType = typeof(IntlLocalePrototype), Length = 1d, DisplayName = "Locale")]
public sealed partial class IntlLocaleConstructor : JsConstructor
{
    public IntlLocaleConstructor(JsObject prototype, RealmState realm) : base(prototype, realm)
    {
    }

    protected override JsObject ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        var instance = PrepareThisObject(thisValue);

        var tagValue = args.GetArgument(0);
        var tag = StandardLibrary.JsValueToString(tagValue, Realm);
        var canonicalTag = IntlUtilities.CanonicalizeLocale(tag, Realm);
        instance.SetProperty(IntlLocalePrototype.TagSlot, canonicalTag);
        instance.SetProperty(IntlLocalePrototype.BrandKey, true);

        var optionsArg = args.GetArgument(1);
        if (!StandardLibrary.TryGetObject(optionsArg, Realm, out var optionsAccessor) ||
            optionsAccessor is not IJsPropertyAccessor options)
        {
            return instance;
        }

        if (options.TryGetProperty("calendar", out var calendar))
        {
            instance.SetProperty(IntlLocalePrototype.CalendarSlot, calendar);
        }

        if (options.TryGetProperty("numberingSystem", out var numberingSystem))
        {
            instance.SetProperty(IntlLocalePrototype.NumberingSystemSlot, numberingSystem);
        }

        return instance;
    }
}
