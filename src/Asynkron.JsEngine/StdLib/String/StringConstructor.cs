using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsConstructor("String", PrototypeType = typeof(StringPrototype), Length = 1d, DisplayName = "String")]
public sealed partial class StringConstructor(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected override object? ConstructInstance(object? thisValue, IReadOnlyList<object?> args)
    {
        if (thisValue is JsObject { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, _constructor ?? ConstructFallback);
            InitializeWrapper(constructing, args);
            return constructing;
        }

        return ResolveString(args);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;
        Realm.StringPrototype ??= Prototype as JsObject;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            if (newTarget is null)
            {
                return ResolveString(args);
            }

            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget as IJsCallable ?? target;
            return ConstructWithNewTarget(args, newTargetCallable, target);
        });

        AttachStatics(constructor);
    }

    private object ConstructWithNewTarget(IReadOnlyList<object?> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(null, assignPrototype: false);
        if (proto is not null && instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeWrapper(instance, args);
        return instance;
    }

    private void InitializeWrapper(JsObject wrapper, IReadOnlyList<object?> args)
    {
        var str = ResolveString(args);
        InitializeStringWrapper(str, wrapper, Realm);
    }

    private string ResolveString(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            return string.Empty;
        }

        var value = args[0];
        if (value is TypedAstSymbol typedSymbol)
        {
            return typedSymbol.ToString();
        }

        var context = Realm.CreateContext();
        var str = JsOps.ToJsString(value, context);
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return str;
    }

    private void AttachStatics(HostFunction constructor)
    {
        constructor.SetHostedProperty("fromCodePoint", new HostFunction(StringFromCodePoint, Realm, isConstructor: false),
            Realm);
        constructor.SetHostedProperty("fromCharCode", new HostFunction(StringFromCharCode, Realm, isConstructor: false),
            Realm);
        constructor.SetHostedProperty("raw", new HostFunction(StringRaw, Realm, isConstructor: false), Realm);
        constructor.SetHostedProperty("escape", new HostFunction(StringEscape, Realm, isConstructor: false), Realm);
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("String constructor not initialized");

    private void ApplyPrototype(JsObject instance, IJsCallable target)
    {
        if (instance.Prototype is not null)
        {
            return;
        }

        var proto = ResolveConstructPrototype(target, target, Realm) ?? Prototype;
        if (proto is not null)
        {
            instance.SetPrototype(proto);
        }
    }
}
