#region

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

        // Check if the first argument is a RegExp object (has __regex__ internal slot).
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

        // Per spec: check IsRegExp(pattern). This may trigger Symbol.match getter.
        if (patternArg.IsObject && IsRegExpLike(patternArg))
        {
            // Pattern is a regexp-like object without __regex__. Extract source and flags.
            JsOps.TryGetPropertyValue(patternArg, "source", out var sourceValue);
            var pattern = JsOps.ToJsString(sourceValue) ?? string.Empty;
            string flags;
            if (flagsArg != JsValue.Undefined)
            {
                flags = JsOps.ToJsString(flagsArg) ?? string.Empty;
            }
            else
            {
                JsOps.TryGetPropertyValue(patternArg, "flags", out var flagsValue);
                flags = JsOps.ToJsString(flagsValue) ?? string.Empty;
            }

            return CreateRegExpLiteral(pattern, flags, Realm, target);
        }

        var patternStr = patternArg == JsValue.Undefined
            ? "(?:)"
            : JsOps.ToJsString(patternArg) ?? string.Empty;
        var flagsStr = flagsArg != JsValue.Undefined
            ? JsOps.ToJsString(flagsArg) ?? string.Empty
            : string.Empty;
        return CreateRegExpLiteral(patternStr, flagsStr, Realm, target);
    }

    /// <summary>
    /// Checks IsRegExp(argument) per ES spec 7.2.8.
    /// Accesses Symbol.match property and returns true if it's truthy or if it's a RegExp.
    /// </summary>
    private static bool IsRegExpLike(JsValue argument)
    {
        if (!argument.IsObject)
        {
            return false;
        }

        if (JsOps.TryGetPropertyValue(argument, Ast.SymbolKeys.Match, out var matchValue) &&
            !matchValue.IsUndefined)
        {
            return matchValue.IsTruthy;
        }

        return argument.TryGetObject<JsRegExp>(out _);
    }
}
