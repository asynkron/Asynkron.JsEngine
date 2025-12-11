using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("RegExp", PrototypeType = typeof(RegExpPrototype), Length = 2d, DisplayName = "RegExp")]
public sealed partial class RegExpConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is JsObject { IsConstructing: true } constructing)
        {
            var target = _constructor ?? ConstructFallback;
            return ConstructRegExp(args, target, target, constructing);
        }

        var targetCtor = _constructor ?? ConstructFallback;
        return ConstructRegExp(args, targetCtor, targetCtor, thisValue as JsObject);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.RegExpPrototype ??= Prototype as JsObject;
        Realm.RegExpConstructor ??= constructor;

        constructor.SetInvokeWithContext((args, thisArg, _, newTarget) =>
        {
            var targetCtor = _constructor ?? constructor;
            var effectiveNewTarget = newTarget as IJsCallable ?? targetCtor;
            return ConstructRegExp(args, effectiveNewTarget, targetCtor, thisArg as JsObject);
        });

        DefineLegacyRegExpAccessors(constructor, Realm);
    }

    private object ConstructRegExp(IReadOnlyList<object?> args, IJsCallable newTarget, IJsCallable targetCtor,
        JsObject? thisArg)
    {
        var provided = thisArg is JsObject jsObj &&
                       (jsObj.IsConstructing || IsRegExpLikeInstance(jsObj, Realm))
            ? jsObj
            : null;
        var instance = PrepareTargetInstance(provided, newTarget, targetCtor);
        return InitializeRegExp(args, instance);
    }

    private JsObject PrepareTargetInstance(JsObject? provided, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var instance = provided ?? PrepareThisObject(null, assignPrototype: false);
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

    private JsObject InitializeRegExp(IReadOnlyList<object?> args, JsObject? target)
    {
        if (args.Count == 0)
        {
            return CreateRegExpLiteral("(?:)", "", Realm, target);
        }

        if (args is [JsObject { } existingObj] &&
            existingObj.TryGetProperty("__regex__", out var internalRegex) &&
            internalRegex is JsRegExp existing)
        {
            return CreateRegExpLiteral(existing.Pattern, existing.Flags, Realm, target);
        }

        var pattern = args[0]?.ToString() ?? string.Empty;
        var flags = args.Count > 1 ? args[1]?.ToString() ?? string.Empty : string.Empty;
        return CreateRegExpLiteral(pattern, flags, Realm, target);
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("RegExp constructor not initialized");
}
