#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using static Asynkron.JsEngine.StdLib.ReflectHelper;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a host function that can be called from JavaScript.
/// </summary>
public sealed class HostFunction : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider,
    IJsEnvironmentAwareCallable, IAsJsValue
{
    private bool _isConstructor = true;

    // Cached JsValue to avoid repeated struct creation
    private readonly JsValue _cachedJsValue;

    public HostFunction(JsSimpleHandler handler, RealmState? realmState = null,
        bool isConstructor = true)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        Properties = new JsObject();
        HandlerForSnapshot = (_, args) => handler(args);
        RealmState = realmState;
        _isConstructor = isConstructor;
        InitializePrototype();
    }

    public HostFunction(JsHostHandler handler, RealmState? realmState = null,
        bool isConstructor = true)
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        HandlerForSnapshot = handler ?? throw new ArgumentNullException(nameof(handler));
        Properties = new JsObject();
        RealmState = realmState;
        _isConstructor = isConstructor;
        InitializePrototype();
    }

    internal HostFunction(
        JsHostHandler handler,
        JsObject properties,
        RealmState? realmState,
        bool isConstructor,
        bool initializePrototype)
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        HandlerForSnapshot = handler ?? throw new ArgumentNullException(nameof(handler));
        Properties = properties ?? throw new ArgumentNullException(nameof(properties));
        _isConstructor = isConstructor;
        RealmState = realmState;
        if (initializePrototype)
        {
            InitializePrototype();
        }
    }

    internal bool IsBoundFunction { get; set; }
    internal IJsCallable? BoundTargetFunction { get; set; }

    /// <summary>
    ///     Optional realm/global object that owns this host function. Used for
    ///     realm-aware operations (e.g. Reflect.construct default prototypes).
    /// </summary>
    public JsObject? Realm { get; set; }

    /// <summary>
    ///     Optional realm state for intrinsic prototype resolution.
    /// </summary>
    public RealmState? RealmState
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            Properties.RealmState ??= value;
            if (field?.FunctionPrototype is { } functionPrototype &&
                Properties.Prototype is null &&
                Properties.PrototypeAccessor is null)
            {
                Properties.SetPrototype(functionPrototype);
            }
        }
    }

    /// <summary>
    ///     Indicates whether this host function can be used with <c>new</c>.
    /// </summary>
    public bool IsConstructor
    {
        get => _isConstructor;
        set
        {
            if (_isConstructor == value)
            {
                return;
            }

            _isConstructor = value;
            if (_isConstructor)
            {
                EnsurePrototypeDataProperty();
            }
            else
            {
                RemovePrototypeDataProperty();
            }
        }
    }

    /// <summary>
    ///     When true, construction is explicitly disallowed even though the function
    ///     reports itself as a constructor (e.g. BigInt).
    /// </summary>
    public bool DisallowConstruct { get; set; }

    /// <summary>
    ///     Optional error message used when construction is disallowed.
    /// </summary>
    public string? ConstructErrorMessage { get; set; }

    public bool HandlesConstructInternally { get; set; }

    internal JsObject Properties { get; }

    internal JsObject PropertiesObject => Properties;

    internal JsHostHandler HandlerForSnapshot { get; private set; }

    internal Func<IReadOnlyList<JsValue>, JsValue, EvaluationContext?, JsValue, JsValue>? InvokeWithContextForSnapshot
    {
        get;
        private set;
    }

    public bool IsExtensible => Properties.IsExtensible;

    /// <inheritdoc />
    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    public void PreventExtensions()
    {
        Properties.PreventExtensions();
    }

    public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
    {
        // Use context-aware handler if set (e.g., for error constructors that work without 'new')
        if (InvokeWithContextForSnapshot is not null)
        {
            return InvokeWithContextForSnapshot(arguments, thisValue, null, JsValue.Undefined);
        }

        return HandlerForSnapshot(thisValue, arguments);
    }

    /// <summary>
    ///     Captures the environment that invoked this host function so nested
    ///     callbacks can inherit the correct global `this` binding.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        EnsureFunctionPrototype();

        // First check if there's a user-defined property (including getters)
        if (Properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver, out value))
        {
            return true;
        }

        // Non-constructor functions should not have a "prototype" property
        if (!_isConstructor && string.Equals(name, "prototype", StringComparison.Ordinal))
        {
            value = JsValue.Undefined;
            return false;
        }

        // Provide minimal Function.prototype-style helpers for host functions:
        // fn.call(thisArg, ...args), fn.apply(thisArg, argsArray), fn.bind(thisArg, ...boundArgs)
        IJsCallable jsCallable = this;
        switch (name)
        {
            case "call":
                value = (JsValue)new HostFunction((thisValue, args) =>
                {
                    // When call is invoked through .bind(), thisValue is the bound target function.
                    // When called directly like fn.call(obj), jsCallable is the function to invoke.
                    var functionToCall = thisValue.TryGetObject<IJsCallable>(out var callable) ? callable : jsCallable;
                    var thisArg = args.GetArgument(0);
                    var callArgs = args.SliceFrom(1);
                    return functionToCall.Invoke(callArgs, thisArg);
                }, isConstructor: false);
                return true;

            case "apply":
                value = (JsValue)new HostFunction((thisValue, args) =>
                {
                    // Use the thisValue (the function being applied) when called via prototype chain
                    var target = thisValue.TryGetObject<IJsCallable>(out var thisObj) ? thisObj : jsCallable;
                    var thisArg = args.GetArgument(0);
                    var argList = CreateFunctionApplyArgumentList(args.GetArgument(1), RealmState);

                    return target.Invoke(argList, thisArg);
                }, isConstructor: false);
                return true;

            case "bind":
                value = (JsValue)new HostFunction((thisValue, args) =>
                {
                    // Use the thisValue (the function being bound) when called via prototype chain
                    var target = thisValue.TryGetObject<IJsCallable>(out var callable) ? callable : jsCallable;
                    var boundThis = args.GetArgument(0);
                    var boundArgs = args.SliceFrom(1);
                    // Check if the TARGET function is a constructor, not the bind method's receiver
                    var targetIsConstructor = JsOps.IsConstructor(thisValue);
                    var realmState = RealmState ?? (target as ICallableMetadata)?.RealmState;
                    return (JsValue)CreateBoundFunction(target, boundThis, boundArgs, targetIsConstructor, realmState);
                }, isConstructor: false);
                return true;
        }

        value = default;
        return false;
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, _cachedJsValue, out value);
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        Properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, _cachedJsValue);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        Properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (!_isConstructor && string.Equals(name, "prototype", StringComparison.Ordinal))
        {
            return null;
        }

        return Properties.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var key in Properties.GetOwnPropertyNames())
        {
            if (!_isConstructor && string.Equals(key, "prototype", StringComparison.Ordinal))
            {
                continue;
            }

            yield return key;
        }
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var key in Properties.GetEnumerablePropertyNames())
        {
            if (!_isConstructor && string.Equals(key, "prototype", StringComparison.Ordinal))
            {
                continue;
            }

            yield return key;
        }
    }

    public JsObject? Prototype
    {
        get
        {
            EnsureFunctionPrototype();
            return Properties.Prototype;
        }
    }

    public bool IsSealed => Properties.IsSealed;
    public bool IsFrozen => Properties.IsFrozen;

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var key in Properties.Keys)
            {
                if (!_isConstructor && string.Equals(key, "prototype", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return key;
            }
        }
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        Properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        Properties.Seal();
    }

    public bool Delete(string name)
    {
        return DeleteProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return Properties.TryDefineProperty(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        Properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public JsValue InvokeWithContext(
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        EvaluationContext? context,
        JsValue newTarget = default)
    {
        if (InvokeWithContextForSnapshot is null)
        {
            return HandlerForSnapshot(thisValue, arguments);
        }

        return InvokeWithContextForSnapshot(arguments, thisValue, context, newTarget);
    }

    public void SetInvokeWithContext(
        Func<IReadOnlyList<JsValue>, JsValue, EvaluationContext?, JsValue, JsValue> handler)
    {
        InvokeWithContextForSnapshot = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    internal void SetInvokeWithContextForSnapshot(
        Func<IReadOnlyList<JsValue>, JsValue, EvaluationContext?, JsValue, JsValue>? handler)
    {
        InvokeWithContextForSnapshot = handler;
    }

    internal void SetHandlerForSnapshot(JsHostHandler handler)
    {
        HandlerForSnapshot = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    internal void SetConstructorFlagForSnapshot(bool isConstructor)
    {
        _isConstructor = isConstructor;
    }

    public bool DeleteProperty(string name)
    {
        return Properties.DeleteOwnProperty(name);
    }

    internal static HostFunction CreateBoundFunction(IJsCallable target, JsValue boundThis,
        IReadOnlyList<JsValue> boundArgs, bool targetIsConstructor, RealmState? realmState)
    {
        // IMPORTANT: Copy bound args to an owned array to avoid issues with pooled argument arrays.
        // The caller's argument array may be returned to a pool and cleared after bind() returns,
        // which would corrupt the bound function's captured arguments.
        if (boundArgs.Count > 0 && boundArgs is not JsValue[])
        {
            var copy = new JsValue[boundArgs.Count];
            for (var i = 0; i < boundArgs.Count; i++)
            {
                copy[i] = boundArgs[i];
            }

            boundArgs = copy;
        }

        var boundFunction = new HostFunction((_, innerArgs) =>
        {
            var finalArgs = Combine(boundArgs, innerArgs);
            return target.Invoke(finalArgs, boundThis);
        }, realmState, false)
        { IsBoundFunction = true, IsConstructor = targetIsConstructor, BoundTargetFunction = target };

        // ES spec: Set the bound function's length and name properties
        // length = max(0, target.length - boundArgs.length)
        double targetLength = 0;
        if (target is IJsPropertyAccessor targetAccessor)
        {
            var ownLength = targetAccessor.GetOwnPropertyDescriptor("length");
            if (ownLength is { HasValue: true })
            {
                var lenVal = ownLength.JsValue;
                if (lenVal.IsNumber)
                {
                    var numericLength = lenVal.NumberValue;
                    var integerLength =
                        double.IsNaN(numericLength) || numericLength == 0
                            ? 0
                            : double.IsInfinity(numericLength)
                                ? numericLength
                                : Math.Truncate(numericLength);
                    targetLength = Math.Max(0, integerLength);
                }
            }
        }

        var boundLength = Math.Max(0, targetLength - boundArgs.Count);
        boundFunction.DefineProperty("length", new PropertyDescriptor
        {
            Value = new JsValue(boundLength),
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        // name = "bound " + target.name
        var targetName = "";
        if (target is IJsPropertyAccessor nameAccessor &&
            nameAccessor.TryGetProperty("name", out var nameVal) && nameVal.IsString)
        {
            targetName = nameVal.AsString() ?? "";
        }

        boundFunction.DefineProperty("name", new PropertyDescriptor
        {
            Value = new JsValue("bound " + targetName),
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        boundFunction.SetInvokeWithContext((invokeArgs, _, context, newTarget) =>
        {
            var finalArgs = Combine(boundArgs, invokeArgs);

            if (newTarget.IsUndefined)
            {
                return target.Invoke(finalArgs, boundThis);
            }

            if (!targetIsConstructor || !newTarget.TryGetObject<IJsCallable>(out var newTargetCtor))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Target is not a constructor",
                    context,
                    context?.RealmState ?? realmState);
                throw new ThrowSignal(error);
            }

            var realm = context?.RealmState ?? realmState;
            if (realm is null && target is ICallableMetadata metadata)
            {
                realm = metadata.RealmState;
            }

            if (realm is null)
            {
                return target.Invoke(finalArgs, boundThis);
            }

            // Per ES spec 10.4.1.2 [[Construct]] step 5:
            // If SameValue(F, newTarget) is true, set newTarget to target.
            // When the bound function is used as new.target, substitute the original target.
            var effectiveNewTarget = ReferenceEquals(newTargetCtor, boundFunction)
                ? target
                : newTargetCtor;

            return Construct(target, finalArgs, effectiveNewTarget, realm);

        });

        return boundFunction;

        static IReadOnlyList<JsValue> Combine(IReadOnlyList<JsValue> prefix, IReadOnlyList<JsValue> suffix)
        {
            if (prefix.Count == 0)
            {
                return suffix;
            }

            if (suffix.Count == 0)
            {
                return prefix;
            }

            var final = new JsValue[prefix.Count + suffix.Count];
            for (var i = 0; i < prefix.Count; i++)
            {
                final[i] = prefix[i];
            }

            for (var i = 0; i < suffix.Count; i++)
            {
                final[prefix.Count + i] = suffix[i];
            }

            return final;
        }
    }

    private void EnsureFunctionPrototype()
    {
        // Check both Prototype (for JsObject prototypes) and PrototypeAccessor (for HostFunction prototypes)
        // This is important for intrinsics like AsyncFunction whose [[Prototype]] is Function (a HostFunction),
        // not Function.prototype (a JsObject). Without this check, we would incorrectly overwrite
        // the PrototypeAccessor with Function.prototype.
        if (Properties.Prototype is not null || Properties.PrototypeAccessor is not null)
        {
            return;
        }

        if (RealmState?.FunctionPrototype is { } functionPrototype)
        {
            Properties.SetPrototype(functionPrototype);
        }
    }

    private void InitializePrototype()
    {
        EnsureFunctionPrototype();
        if (_isConstructor)
        {
            EnsurePrototypeDataProperty();
        }
        else
        {
            RemovePrototypeDataProperty();
        }
    }

    private void EnsurePrototypeDataProperty()
    {
        if (!_isConstructor || HasPrototypeDataProperty() || IsBoundFunction)
        {
            return;
        }

        var prototype = new JsObject();
        prototype.SetProperty("constructor", _cachedJsValue);
        // Per ES spec, the "prototype" property on constructor functions should be
        // writable, but NOT enumerable and NOT configurable
        Properties.DefineProperty("prototype",
            new PropertyDescriptor
            {
                Value = (JsValue)prototype,
                Writable = true,
                Enumerable = false,
                Configurable = false
            });
    }

    private void RemovePrototypeDataProperty()
    {
        if (!Properties.DeleteOwnProperty("prototype"))
        {
            Properties.ForceDeleteOwnProperty("prototype");
        }
    }

    private bool HasPrototypeDataProperty()
    {
        return Properties.GetOwnPropertyDescriptor("prototype") is not null
               || Properties.ContainsKey("prototype");
    }
}
