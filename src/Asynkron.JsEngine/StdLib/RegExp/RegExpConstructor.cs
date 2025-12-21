using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.RegExpHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("RegExp", PrototypeType = typeof(RegExpPrototype), Length = 2d, DisplayName = "RegExp")]
public sealed partial class RegExpConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
        {
            var target = _constructor ?? ConstructFallback;
            return ConstructRegExp(args, target, target, constructing);
        }

        var targetCtor = _constructor ?? ConstructFallback;
        var providedThis = thisValue.IsObject ? thisValue.AsObject() as JsObject : null;
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
        var instance = provided ?? PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        if (instance.RealmState is null)
        {
            instance.RealmState = Realm;
        }

        if (instance.Prototype is null)
        {
            var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
            if (proto is not null)
            {
                instance.SetPrototype(proto);
            }
        }

        return instance;
    }

    private JsObject InitializeRegExp(IReadOnlyList<JsValue> args, JsObject? target)
    {
        if (args.Count == 0)
        {
            return CreateRegExpLiteral("(?:)", "", Realm, target);
        }

        if (args.Count == 1 && args[0].IsObject && args[0].AsObject() is { } existingObj &&
            existingObj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex.TryGetObject<JsRegExp>(out var existing))
        {
            return CreateRegExpLiteral(existing.Pattern, existing.Flags, Realm, target);
        }

        var pattern = JsOps.ToJsString(args[0]) ?? string.Empty;
        var flags = args.Count > 1 ? JsOps.ToJsString(args[1]) ?? string.Empty : string.Empty;
        return CreateRegExpLiteral(pattern, flags, Realm, target);
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("RegExp constructor not initialized");
}
