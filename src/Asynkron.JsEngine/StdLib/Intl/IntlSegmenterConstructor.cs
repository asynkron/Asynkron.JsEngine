#region

using Asynkron.JsEngine.JsTypes;
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
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        return ConstructCore(thisValue, args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;

        // Override invoke handler to check NewTarget (for calls without new)
        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            GC.KeepAlive(thisValue);

            // Intl.Segmenter step 1: If NewTarget is undefined, throw a TypeError exception.
            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.Segmenter constructor requires 'new'", realm: Realm);
            }

            var target = _constructor!;
            var newTargetCallable = newTarget.IsObject ? newTarget.AsObject<IJsCallable>() : null;
            return ConstructWithNewTarget(args, newTargetCallable ?? target, target);
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

        JsObject instance;
        if (thisValue.IsObject && thisValue.AsObject() is JsObject provided)
        {
            instance = provided;
            if (instance.IsConstructing)
            {
                ApplyPrototype(instance, _constructor!);
            }
        }
        else
        {
            instance = PrepareThisObject(thisValue);
        }

        IntlSegmenterPrototype.InitializeInternalSlots(instance, resolvedLocale, granularity);
        return new JsValue(instance);
    }

    private JsValue ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = Asynkron.JsEngine.StdLib.ReflectHelper.ResolveConstructPrototype(newTarget, targetCtor, Realm) ??
                    Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, false);
        if (instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        return ConstructCore(new JsValue(instance), args);
    }
}
