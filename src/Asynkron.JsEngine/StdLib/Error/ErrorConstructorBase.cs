#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.ReflectHelper;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

#endregion

namespace Asynkron.JsEngine.StdLib;

public abstract class ErrorConstructorBase(IJsObjectLike prototype, RealmState realm) : JsConstructor(prototype, realm)
{
    private HostFunction? _constructor;

    protected abstract string ErrorType { get; }

    private HostFunction ConstructFallback =>
        _constructor ?? throw new InvalidOperationException("Error constructor not initialized");

    protected override JsValue ConstructInstance(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        if (thisValue.IsObject && thisValue.AsObject() is { IsConstructing: true } constructing)
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

        // Ensure the prototype points back to this constructor so that thrown errors
        // expose a usable .constructor property (used by assert.throws in Test262).
        if (Prototype.GetOwnPropertyDescriptor("constructor") is null)
        {
            var descriptor = new PropertyDescriptor
            {
                Value = constructor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            };

            if (Prototype is IPropertyDefinitionHost definable)
            {
                definable.TryDefineProperty("constructor", descriptor);
            }
            else if (Prototype is JsObject protoObj)
            {
                protoObj.SetProperty("constructor", constructor);
            }
        }

        LinkPrototypeChain();
        InitializePrototypeDefaults();
        CacheRealmReferences(constructor);
        LinkConstructorPrototypeChain(constructor);
    }

    private object ConstructWithNewTarget(IReadOnlyList<JsValue> args, IJsCallable newTarget, IJsCallable targetCtor)
    {
        var proto = ResolveConstructPrototype(newTarget, targetCtor, Realm) ?? Prototype;
        var instance = PrepareThisObject(JsValue.Undefined, false);
        if (instance.Prototype is null)
        {
            instance.SetPrototype(proto);
        }

        InitializeError(instance, args);
        return instance;
    }

    private void InitializeError(JsObject instance, IReadOnlyList<JsValue> args)
    {
        instance.RealmState ??= Realm;

        // ES spec: Set [[ErrorData]] internal slot - we use a non-enumerable internal property
        instance.DefineProperty("_errorData",
            new PropertyDescriptor { Value = JsValue.True, Writable = false, Enumerable = false, Configurable = false });

        // Set message if provided and not undefined
        if (args.Count >= 1 && !args[0].IsUndefined)
        {
            var message = args[0].IsNull ? "null" : JsOps.ToJsString(args[0]);
            instance.DefineProperty("message",
                new PropertyDescriptor { Value = message, Writable = true, Enumerable = false, Configurable = true });
        }

        // ES2022: InstallErrorCause - handle options.cause
        if (args.Count >= 2 && args[1].IsObject)
        {
            var options = args[1];
            if (options.TryGetObject<IJsPropertyAccessor>(out var optionsAccessor) &&
                HasProperty(optionsAccessor, "cause"))
            {
                JsOps.TryGetPropertyValue(options, "cause", out var cause);
                instance.DefineProperty("cause",
                    new PropertyDescriptor { Value = cause, Writable = true, Enumerable = false, Configurable = true });
            }
        }
    }

    private void LinkPrototypeChain()
    {
        if (string.Equals(ErrorType, "Error", StringComparison.Ordinal))
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

    /// <summary>
    ///     Per ES spec, the [[Prototype]] of each NativeError constructor is the intrinsic %Error% constructor.
    ///     For the Error constructor itself, the [[Prototype]] remains %Function.prototype% (the default).
    /// </summary>
    private void LinkConstructorPrototypeChain(HostFunction constructor)
    {
        if (string.Equals(ErrorType, "Error", StringComparison.Ordinal))
        {
            return;
        }

        if (Realm.ErrorConstructor is { } errorConstructor)
        {
            constructor.SetPrototype(errorConstructor);
        }
    }

    private void CacheRealmReferences(HostFunction constructor)
    {
        switch (ErrorType)
        {
            case "Error":
                Realm.ErrorConstructor = constructor;
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
            case "URIError":
                Realm.URIErrorConstructor = constructor;
                break;
        }
    }
}
