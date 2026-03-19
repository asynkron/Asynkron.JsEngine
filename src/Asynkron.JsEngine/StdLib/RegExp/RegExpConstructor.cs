#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.RegExpHelper;

#endregion

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("RegExp", PrototypeType = typeof(RegExpPrototype), Length = 2d, DisplayName = "RegExp")]
public sealed partial class RegExpConstructor(IJsObjectLike prototype, RealmState realm)
    : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("RegExp constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            var target = _constructor ?? ConstructFallback;
            return ConstructRegExp(args, target, target, constructing);
        }

        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() : null;
        return ConstructRegExp(args, targetCtor, targetCtor, providedThis);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.RegExpPrototype ??= Prototype as JsObject;
        Realm.RegExpConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisArg, _, newTarget) =>
        {
            var targetCtor = _constructor ?? constructor;
            IJsCallable effectiveNewTarget;
            if (newTarget.TryGetObject<IJsCallable>(out var callable))
            {
                effectiveNewTarget = callable;
            }
            else
            {
                effectiveNewTarget = targetCtor;
            }

            JsObject? thisObj = null;
            thisArg.TryGetObject<JsObject>(out thisObj);
            return ConstructRegExp(args, effectiveNewTarget, targetCtor, thisObj);
        });

        DefineLegacyRegExpAccessors(constructor, Realm);
    }

    [JsConstructorSymbolGetter("species")]
    public static JsValue GetSpecies(JsValue thisValue)
    {
        // Keep species defaulted to the constructor for subclassing behavior.
        return thisValue;
    }

    private JsValue ConstructRegExp(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor,
        JsObject? thisArg)
    {
        var provided = thisArg != null &&
                       (thisArg.IsConstructing || IsRegExpLikeInstance(thisArg, Realm))
            ? thisArg
            : null;
        var instance = PrepareTargetInstance(provided, newTarget, targetCtor);
        return (JsValue)InitializeRegExp(args, instance);
    }

    private JsObject PrepareTargetInstance(JsObject? provided, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var instance = provided ?? PrepareThisObject(JsValue.Undefined, false);
        if (instance.RealmState is null)
        {
            instance.RealmState = Realm;
        }

        if (instance.Prototype is not null)
        {
            return instance;
        }

        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        instance.SetPrototype(proto);

        return instance;
    }

    private JsObject InitializeRegExp(IReadOnlyList<JsValue> args, JsObject? target)
    {
        if (args.Count == 0)
        {
            return CreateRegExpLiteral("(?:)", "", Realm, target);
        }

        var patternArg = args[0];
        var flagsArg = args.Count > 1 ? args[1] : JsValue.Undefined;

        // Step 2: Let patternIsRegExp be ? IsRegExp(pattern).
        // This must happen before checking [[RegExpMatcher]] for observable behavior.
        var patternIsRegExp = IsRegExpAbrupt(patternArg);

        // Step 3: If pattern is an Object and has [[RegExpMatcher]] internal slot.
        if (patternArg.IsObject && patternArg.AsObject() is { } existingObj &&
            existingObj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex.TryGetObject<JsRegExp>(out var existing))
        {
            // Per spec: if flags argument is provided, use it; otherwise use the RegExp's original flags.
            var flags = flagsArg != JsValue.Undefined
                ? JsOps.ToJsString(flagsArg) ?? string.Empty
                : existing.Flags;
            return CreateRegExpLiteral(existing.Pattern, flags, Realm, target);
        }

        // Step 4: Else if patternIsRegExp is true (object passes IsRegExp but has no internal slot).
        if (patternIsRegExp)
        {
            string p;
            if (JsOps.TryGetPropertyValue(patternArg, "source", out var sourceVal))
                p = JsOps.ToJsString(sourceVal) ?? string.Empty;
            else
                p = string.Empty;

            string f;
            if (flagsArg.IsUndefined)
            {
                if (JsOps.TryGetPropertyValue(patternArg, "flags", out var flagsVal))
                    f = JsOps.ToJsString(flagsVal) ?? string.Empty;
                else
                    f = string.Empty;
            }
            else
            {
                f = JsOps.ToJsString(flagsArg) ?? string.Empty;
            }

            return CreateRegExpLiteral(p, f, Realm, target);
        }

        // Step 5: Else — pattern is not a RegExp.
        var pattern = patternArg.IsUndefined ? string.Empty : JsOps.ToJsString(patternArg) ?? string.Empty;
        var flagsStr = flagsArg.IsUndefined
            ? string.Empty
            : JsOps.ToJsString(flagsArg) ?? string.Empty;
        return CreateRegExpLiteral(pattern, flagsStr, Realm, target);
    }

    /// <summary>
    /// 7.2.8 IsRegExp ( argument ) with abrupt completion propagation.
    /// </summary>
    private bool IsRegExpAbrupt(JsValue argument)
    {
        if (!argument.IsObject)
            return false;

        // Step 2: Let matcher be ? Get(argument, @@match).
        var context = Realm.CreateContext();
        if (JsOps.TryGetPropertyValue(argument, SymbolKeys.Match, out var matchValue, context))
        {
            if (context.IsThrow)
                throw new ThrowSignal(context.FlowValue);

            // Step 3: If matcher is not undefined, return ! ToBoolean(matcher).
            if (!matchValue.IsUndefined)
                return matchValue.IsTruthy;
        }
        else if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        // Step 4: If argument has a [[RegExpMatcher]] internal slot, return true.
        if (argument.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("__regex__", out var regexVal) &&
            regexVal.TryGetObject<JsRegExp>(out _))
        {
            return true;
        }

        return argument.TryGetObject<JsRegExp>(out _);
    }
}
