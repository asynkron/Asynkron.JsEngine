#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.IntlHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib.Intl;

[JsConstructor("Intl.PluralRules", PrototypeType = typeof(IntlPluralRulesPrototype), Length = 0d,
    DisplayName = "PluralRules")]
public sealed partial class IntlPluralRulesConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        var localesArg = args.GetArgument(0);
        var optionsArg = args.GetArgument(1);
        var (_, resolvedLocale) = ResolveIntlLocales(localesArg, Realm);
        var options = IntlOptionHelpers.GetOptionsObject(optionsArg, Realm, "PluralRules");

        _ = IntlOptionHelpers.GetStringOption(options, "localeMatcher", Realm, "PluralRules",
            ["lookup", "best fit"], "best fit");
        var type = IntlOptionHelpers.GetStringOption(options, "type", Realm, "PluralRules",
            ["cardinal", "ordinal"], "cardinal");
        var notation = IntlOptionHelpers.GetStringOption(options, "notation", Realm, "PluralRules",
            ["standard", "scientific", "engineering", "compact"], "standard");

        var minimumIntegerDigits = GetDigitOption(options, "minimumIntegerDigits", 1, 21, 1);
        var hasMinFrac = TryGetDigitOption(options, "minimumFractionDigits", 0, 20, out var minimumFractionDigits);
        var hasMaxFrac = TryGetDigitOption(options, "maximumFractionDigits", 0, 20, out var maximumFractionDigits);
        var hasMinSig = TryGetDigitOption(options, "minimumSignificantDigits", 1, 21, out var minimumSignificantDigits);
        var hasMaxSig = TryGetDigitOption(options, "maximumSignificantDigits", 1, 21, out var maximumSignificantDigits);

        var roundingIncrement = GetDigitOption(options, "roundingIncrement", 1, 5000, 1);
        var roundingMode = IntlOptionHelpers.GetStringOption(options, "roundingMode", Realm, "PluralRules",
            ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"],
            "halfExpand");
        var roundingPriority = IntlOptionHelpers.GetStringOption(options, "roundingPriority", Realm, "PluralRules",
            ["auto", "morePrecision", "lessPrecision"], "auto");
        var trailingZeroDisplay = IntlOptionHelpers.GetStringOption(options, "trailingZeroDisplay", Realm,
            "PluralRules", ["auto", "stripIfInteger"], "auto");

        // Resolve digit options per spec
        int resolvedMinFrac, resolvedMaxFrac;
        int? resolvedMinSig = null, resolvedMaxSig = null;
        string roundingType;

        if (hasMinSig || hasMaxSig)
        {
            var mnsd = minimumSignificantDigits ?? 1;
            var mxsd = maximumSignificantDigits ?? 21;
            if (mnsd > mxsd)
            {
                throw ThrowRangeError(
                    "minimumSignificantDigits must be less than or equal to maximumSignificantDigits", realm: Realm);
            }

            resolvedMinSig = mnsd;
            resolvedMaxSig = mxsd;

            if (hasMinFrac || hasMaxFrac)
            {
                resolvedMinFrac = minimumFractionDigits ?? 0;
                resolvedMaxFrac = maximumFractionDigits ?? Math.Max(resolvedMinFrac, 0);
                if (resolvedMinFrac > resolvedMaxFrac)
                {
                    throw ThrowRangeError(
                        "minimumFractionDigits must be less than or equal to maximumFractionDigits", realm: Realm);
                }

                roundingType = string.Equals(roundingPriority, "auto", StringComparison.Ordinal)
                    ? "morePrecision"
                    : roundingPriority;
            }
            else
            {
                resolvedMinFrac = 0;
                resolvedMaxFrac = 0;
                roundingType = string.Equals(roundingPriority, "auto", StringComparison.Ordinal)
                    ? "significantDigits"
                    : roundingPriority;
            }
        }
        else if (hasMinFrac || hasMaxFrac)
        {
            resolvedMinFrac = minimumFractionDigits ?? 0;
            resolvedMaxFrac = maximumFractionDigits ?? Math.Max(resolvedMinFrac, 0);
            if (resolvedMinFrac > resolvedMaxFrac)
            {
                throw ThrowRangeError(
                    "minimumFractionDigits must be less than or equal to maximumFractionDigits", realm: Realm);
            }

            roundingType = "fractionDigits";
        }
        else
        {
            // Defaults for PluralRules per spec: fractionDigits 0..3
            resolvedMinFrac = 0;
            resolvedMaxFrac = 3;
            roundingType = "fractionDigits";
        }

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

        IntlPluralRulesPrototype.InitializeInternalSlots(instance, resolvedLocale, type, notation,
            minimumIntegerDigits, resolvedMinFrac, resolvedMaxFrac,
            resolvedMinSig, resolvedMaxSig, roundingMode, roundingType,
            roundingIncrement, trailingZeroDisplay);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;

        constructor.SetInvokeWithContext((args, thisValue, _, newTarget) =>
        {
            GC.KeepAlive(thisValue);

            if (newTarget.IsUndefined)
            {
                throw ThrowTypeError("Intl.PluralRules constructor requires 'new'", realm: Realm);
            }

            var target = _constructor!;
            var newTargetCallable = newTarget.IsObject ? newTarget.AsObject<IJsCallable>() : null;
            return ConstructWithNewTarget(args, newTargetCallable ?? target, target);
        });

        IntlHelper.ConfigureSupportedLocalesOf(constructor, Realm);
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

        return ConstructInstance(new JsValue(instance), args);
    }

    private int GetDigitOption(IJsPropertyAccessor? options, string property, int minimum, int maximum, int fallback)
    {
        if (options is null ||
            !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return fallback;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < minimum || number > maximum)
        {
            throw ThrowRangeError(
                $"Intl.PluralRules {property} option must be between {minimum} and {maximum}", realm: Realm);
        }

        return (int)Math.Floor(number);
    }

    private bool TryGetDigitOption(
        IJsPropertyAccessor? options,
        string property,
        int minimum,
        int maximum,
        out int? result)
    {
        result = null;
        if (options is null ||
            !options.TryGetProperty(property, out var value) ||
            value.IsUndefined)
        {
            return false;
        }

        var number = JsOps.ToNumber(value);
        if (double.IsNaN(number) || number < minimum || number > maximum)
        {
            throw ThrowRangeError(
                $"Intl.PluralRules {property} option must be between {minimum} and {maximum}", realm: Realm);
        }

        result = (int)Math.Floor(number);
        return true;
    }
}
