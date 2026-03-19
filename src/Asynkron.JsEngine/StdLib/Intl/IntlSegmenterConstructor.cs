#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.Segmenter", PrototypeType = typeof(IntlSegmenterPrototype), Length = 0d,
    DisplayName = "Segmenter")]
public sealed partial class IntlSegmenterConstructor(IJsObjectLike prototype, RealmState realm)
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
            // Intl.Segmenter step 1: If NewTarget is undefined, throw a TypeError exception.
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.Segmenter constructor requires 'new'", realm: Realm);
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
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "Segmenter");

        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "Segmenter",
            ["lookup", "best fit"], "best fit");
        var granularity = IntlOptionHelpers.GetStringOption(options, "granularity", Realm, "Segmenter",
            ["grapheme", "word", "sentence"], "grapheme");

        var instance = PrepareThisObject(thisValue);
        IntlSegmenterPrototype.InitializeInternalSlots(instance, resolvedLocale, granularity);
        return new JsValue(instance);
    }
}
