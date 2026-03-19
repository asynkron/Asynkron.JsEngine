#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.ListFormat", PrototypeType = typeof(IntlListFormatPrototype), Length = 0d,
    DisplayName = "ListFormat")]
public sealed partial class IntlListFormatConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructCore(thisValue, args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        // Override invoke handler to check NewTarget (for calls without new)
        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            // Intl.ListFormat step 1: If NewTarget is undefined, throw a TypeError exception.
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.ListFormat constructor requires 'new'", realm: Realm);
            }

            return ConstructCore(thisValue, args);
        });

        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
    }

    private JsValue ConstructCore(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);

        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "ListFormat");

        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "ListFormat",
            ["lookup", "best fit"], "best fit");
        var type = IntlOptionHelpers.GetStringOption(options, "type", Realm, "ListFormat",
            ["conjunction", "disjunction", "unit"], "conjunction");
        var style = IntlOptionHelpers.GetStringOption(options, "style", Realm, "ListFormat",
            ["long", "short", "narrow"], "long");

        var instance = PrepareThisObject(thisValue);
        IntlListFormatPrototype.InitializeInternalSlots(instance, resolvedLocale, type, style);
        return new JsValue(instance);
    }
}
