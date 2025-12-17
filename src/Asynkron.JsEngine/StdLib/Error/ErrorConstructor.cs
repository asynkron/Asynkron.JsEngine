using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

public abstract partial class ErrorConstructorBase(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected abstract string ErrorType { get; }

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is JsObject { IsConstructing: true } constructing)
        {
            ApplyPrototype(constructing, _constructor ?? ConstructFallback);
            InitializeError(constructing, args);
            return new JsValue(constructing);
        }

        var instance = PrepareThisObject(JsValue.Undefined);
        InitializeError(instance, args);
        return new JsValue(instance);
    }

    protected override void ConfigureConstructor(HostFunction constructor)
    {
        _constructor = constructor;

        constructor.SetInvokeWithContext((args, _, _, newTarget) =>
        {
            var target = _constructor ?? constructor;
            var newTargetCallable = newTarget.TryGetObject<IJsCallable>(out var callable) ? callable : target;
            return JsValue.FromObjectUnsafe(ConstructWithNewTarget(args, newTargetCallable, target));
        });

        LinkPrototypeChain();
        InitializePrototypeDefaults();
        CacheRealmReferences(constructor);
    }

    private object ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, assignPrototype: false);
        if (proto is not null && instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeError(instance, args);
        return instance;
    }

    private void InitializeError(JsObject instance, IReadOnlyList<JsValue> args)
    {
        instance.RealmState ??= Realm;
        if (args.Count == 0 || args[0].IsUndefined)
        {
            return;
        }

        var message = args[0].IsNull ? "null" : JsOps.ToJsString(args[0]);
        instance.DefineProperty("message",
            new PropertyDescriptor
            {
                Value = message, Writable = true, Enumerable = false, Configurable = true,
            });
    }

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

    private void LinkPrototypeChain()
    {
        if (ErrorType == "Error")
        {
            if (Prototype.Prototype is null && Realm.ObjectPrototype is not null)
            {
                Prototype.SetPrototype(Realm.ObjectPrototype);
            }

            if (Prototype is JsObject jsProto)
            {
                Realm.ErrorPrototype = jsProto;
            }

            return;
        }

        if (Realm.ErrorPrototype is not null)
        {
            Prototype.SetPrototype(Realm.ErrorPrototype);
        }
    }

    private void InitializePrototypeDefaults()
    {
        Prototype.DefineProperty("name",
            new PropertyDescriptor { Value = ErrorType, Writable = true, Enumerable = false, Configurable = true });
        Prototype.DefineProperty("message",
            new PropertyDescriptor { Value = string.Empty, Writable = true, Enumerable = false, Configurable = true });
    }

    private void CacheRealmReferences(HostFunction constructor)
    {
        switch (ErrorType)
        {
            case "Error":
                break;
            case "TypeError":
                Realm.TypeErrorPrototype = Prototype as JsObject;
                Realm.TypeErrorConstructor = constructor;
                break;
            case "RangeError":
                Realm.RangeErrorConstructor = constructor;
                break;
            case "SyntaxError":
                Realm.SyntaxErrorPrototype = Prototype as JsObject;
                Realm.SyntaxErrorConstructor = constructor;
                break;
            case "ReferenceError":
                Realm.ReferenceErrorPrototype = Prototype as JsObject;
                Realm.ReferenceErrorConstructor = constructor;
                break;
        }
    }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Error constructor not initialized");
}

[JsConstructor("Error", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "Error")]
public sealed partial class ErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "Error";
}

[JsConstructor("TypeError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "TypeError")]
public sealed partial class TypeErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "TypeError";
}

[JsConstructor("RangeError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "RangeError")]
public sealed partial class RangeErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "RangeError";
}

[JsConstructor("ReferenceError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "ReferenceError")]
public sealed partial class ReferenceErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "ReferenceError";
}

[JsConstructor("SyntaxError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "SyntaxError")]
public sealed partial class SyntaxErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "SyntaxError";
}

[JsConstructor("EvalError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "EvalError")]
public sealed partial class EvalErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "EvalError";
}

[JsConstructor("URIError", PrototypeType = typeof(ErrorPrototype), Length = 1d, DisplayName = "URIError")]
public sealed partial class UriErrorConstructor(IJsObjectLike prototype, RealmState realm) : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "URIError";
}

[JsConstructor("AggregateError", PrototypeType = typeof(ErrorPrototype), Length = 2d, DisplayName = "AggregateError")]
public sealed partial class AggregateErrorConstructor(IJsObjectLike prototype, RealmState realm)
    : ErrorConstructorBase(prototype, realm)
{
    protected override string ErrorType => "AggregateError";
}
