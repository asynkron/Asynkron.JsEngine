#region

using System.Collections;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Proxy wrapper that forwards object operations through the handler traps when available.
///     Implements all ECMAScript Proxy internal method invariant checks per the specification.
/// </summary>
public sealed class JsProxy : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl, IJsCallable,
    IPrototypeAccessorProvider, IPrivateBrandHolder, IAsJsValue
{
    private readonly JsObject _meta = new();
    private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
    private readonly JsObject _privateStorage = new();
    private readonly RealmState? _realm;

    // Cached JsValues to avoid repeated struct creation
    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly JsValue _cachedJsValue;
    private readonly JsValue _targetJsValue;
    public JsProxy(IJsObjectLike target, IJsObjectLike handler, RealmState? realm = null)
    {
        _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _targetJsValue = ToJsObjectValue(Target);
        _realm = realm;
        _privateStorage.RealmState = realm;
        if (Target is JsObject { Prototype: not null } jsObject)
        {
            _meta.SetPrototype(jsObject.Prototype);
            _privateStorage.SetPrototype(_meta.Prototype);
        }
        else if (_meta.Prototype is null && Target is IPrototypeAccessorProvider
        {
            PrototypeAccessor: { } protoAccessor
        })
        {
            _meta.SetPrototype(protoAccessor);
            _privateStorage.SetPrototype(_meta.Prototype);
        }
    }

    public IJsObjectLike Target { get; }

    private IJsObjectLike? _handler;
    public IJsObjectLike? Handler
    {
        get => _handler;
        set
        {
            _handler = value;
        }
    }

    /// <inheritdoc />
    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    private RealmState? ErrorRealm => RealmState.Current ?? _realm;
    private RealmState? CurrentOperationRealm => RealmState.Current ?? _realm;

    private static JsValue ToJsObjectValue(IJsObjectLike value)
    {
        return JsValue.FromObjectUnsafe((object)value);
    }

    private JsValue GetHandlerJsValue()
    {
        var handler = Handler ?? throw StandardLibrary.ThrowTypeError(
            "Cannot perform operation on a revoked Proxy",
            realm: ErrorRealm);
        return ToJsObjectValue(handler);
    }

    private JsValue InvokeTrap(IJsCallable trap, IReadOnlyList<JsValue> arguments)
    {
        var thisValue = GetHandlerJsValue();
        var currentContext = EvaluationContext.Current;
        var currentEnvironment = JsEnvironment.Current;
        var hadThrowBeforeInvoke = currentContext?.IsThrow == true;
        IJsEnvironmentAwareCallable? envAware = null;
        JsEnvironment? previousEnvironment = null;
        if (currentEnvironment is not null && trap is IJsEnvironmentAwareCallable environmentAware)
        {
            envAware = environmentAware;
            previousEnvironment = envAware.CallingJsEnvironment;
            envAware.CallingJsEnvironment = currentEnvironment;
        }

        IEvaluationContextAwareCallable? contextAware = null;
        if (currentContext is not null && trap is IEvaluationContextAwareCallable evaluationContextAware)
        {
            contextAware = evaluationContextAware;
            contextAware.CallingContext = currentContext;
        }

        try
        {
            var result = trap switch
            {
                global::Asynkron.JsEngine.Ast.TypedAstEvaluator.SyncFunctionInvoker typed => typed.InvokeWithContext(arguments, thisValue, currentContext),
                HostFunction host => host.InvokeWithContext(arguments, thisValue, currentContext),
                _ => trap.Invoke(arguments, thisValue)
            };

            if (!hadThrowBeforeInvoke && currentContext?.IsThrow == true)
            {
                throw new ThrowSignal(currentContext.FlowValue);
            }

            return result;
        }
        finally
        {
            if (envAware is not null)
            {
                envAware.CallingJsEnvironment = previousEnvironment;
            }

            if (contextAware is not null)
            {
                contextAware.CallingContext = null;
            }
        }
    }

    // --- [[IsExtensible]] with trap ---
    public bool IsExtensible
    {
        get
        {
            if (TryGetTrap("isExtensible", out var trap))
            {
                var trapResult = InvokeTrap(trap, new SingleValueArgs(_targetJsValue));
                var booleanTrapResult = JsOps.ToBoolean(trapResult);
                var targetIsExtensible = TargetIsExtensible();
                if (booleanTrapResult != targetIsExtensible)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'isExtensible' on proxy: trap result does not reflect extensibility of proxy target",
                        realm: ErrorRealm);
                }

                return booleanTrapResult;
            }

            return TargetIsExtensible();
        }
    }

    // --- [[PreventExtensions]] with trap ---
    public void PreventExtensions()
    {
        if (TryGetTrap("preventExtensions", out var trap))
        {
            var trapResult = InvokeTrap(trap, new SingleValueArgs(_targetJsValue));
            if (JsOps.ToBoolean(trapResult))
            {
                // Invariant: if trap returns true, target must not be extensible
                if (TargetIsExtensible())
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'preventExtensions' on proxy: trap returned truish but the proxy target is extensible",
                        realm: ErrorRealm);
                }
            }

            return;
        }

        if (Target is IExtensibilityControl extensibilityControl)
        {
            extensibilityControl.PreventExtensions();
        }
        else
        {
            Target.Seal();
        }
    }

    // --- [[Call]] with apply trap ---
    public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
    {
        _ = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: ErrorRealm);

        if (Target is not IJsCallable callableTarget)
        {
            throw StandardLibrary.ThrowTypeError("Proxy target is not callable", realm: ErrorRealm);
        }

        if (TryGetTrap("apply", out var trap))
        {
            var argArray = JsValue.FromJsArray(new JsArray(arguments, CurrentOperationRealm));
            var args = new[] { _targetJsValue, thisValue, argArray };
            return InvokeTrap(trap, args);
        }

        return callableTarget.Invoke(arguments, thisValue);
    }

    // --- [[Construct]] with construct trap ---
    internal JsValue Construct(IReadOnlyList<JsValue> arguments, IJsCallable newTarget)
    {
        _ = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: ErrorRealm);

        if (TryGetTrap("construct", out var trap))
        {
            var argArray = JsValue.FromJsArray(new JsArray(arguments, CurrentOperationRealm));
            var args = new[] { _targetJsValue, argArray, JsValue.FromObjectUnsafe(newTarget) };
            var trapResult = InvokeTrap(trap, args);

            // Invariant: trap result must be an object
            if (!trapResult.IsObject)
            {
                throw StandardLibrary.ThrowTypeError(
                    "'construct' on proxy: trap returned non-Object",
                    realm: ErrorRealm);
            }

            return trapResult;
        }

        // No trap: fall through to target [[Construct]]
        if (Target is not IJsCallable callableTarget)
        {
            throw StandardLibrary.ThrowTypeError("Proxy target is not a constructor", realm: ErrorRealm);
        }

        // Delegate to ReflectHelper.Construct for proper construction semantics
        return ReflectHelper.Construct(callableTarget, arguments, newTarget, _realm!);
    }

    public JsObject? Prototype => _meta.Prototype;

    public bool IsSealed => Target.IsSealed;

    public bool IsFrozen => Target.IsFrozen;

    public IEnumerable<string> Keys => Target.Keys;

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var key in GetOwnPropertyKeysInOrder(includeSymbols: false, includeNonEnumerable: true))
        {
            var desc = GetOwnPropertyDescriptor(key);
            if (desc is { Enumerable: true })
            {
                yield return key;
            }
        }
    }

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true)
    {
        if (!TryGetTrap("ownKeys", out var trap))
        {
            foreach (var key in Target.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable))
            {
                yield return key;
            }

            yield break;
        }

        var trapResult = InvokeTrap(trap, new SingleValueArgs(_targetJsValue));

        // Step 8: CreateListFromArrayLike(trapResultArray, <<String, Symbol>>)
        // The result must be an object
        if (!trapResult.IsObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "CreateListFromArrayLike called on non-object", realm: ErrorRealm);
        }

        // Extract keys and validate types
        var trapKeys = CreateListFromArrayLike(trapResult);

        // Step 9: If trapResult contains any duplicate entries, throw a TypeError
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in trapKeys)
        {
            if (!seen.Add(key))
            {
                throw StandardLibrary.ThrowTypeError(
                    "'ownKeys' on proxy: trap returned duplicate entries", realm: ErrorRealm);
            }
        }

        // Step 11: Get target's own keys
        var extensibleTarget = TargetIsExtensible();
        var targetKeys = Target.GetOwnPropertyKeysInOrder(true, true).ToList();

        // Step 12-13: Partition target keys into configurable and non-configurable
        var targetConfigurableKeys = new List<string>();
        var targetNonconfigurableKeys = new List<string>();
        foreach (var key in targetKeys)
        {
            var desc = Target.GetOwnPropertyDescriptor(key);
            if (desc is not null && !desc.Configurable)
            {
                targetNonconfigurableKeys.Add(key);
            }
            else
            {
                targetConfigurableKeys.Add(key);
            }
        }

        // Step 16: If extensibleTarget and nonconfigurable is empty, return trapResult
        if (extensibleTarget && targetNonconfigurableKeys.Count == 0)
        {
            // No invariant checks needed beyond duplicate/type validation
        }
        else
        {
            var uncheckedResultKeys = new HashSet<string>(trapKeys, StringComparer.Ordinal);

            // Step 17: For each non-configurable key, it must be in trap result
            foreach (var key in targetNonconfigurableKeys)
            {
                if (!uncheckedResultKeys.Remove(key))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'ownKeys' on proxy: trap result did not include '" + key + "'",
                        realm: ErrorRealm);
                }
            }

            // Step 18: If extensible, return trapResult
            if (extensibleTarget)
            {
                // All non-configurable keys are accounted for, extensible target can have extra keys
            }
            else
            {
                // Step 19: For each configurable key, it must be in trap result
                foreach (var key in targetConfigurableKeys)
                {
                    if (!uncheckedResultKeys.Remove(key))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'ownKeys' on proxy: trap result did not include '" + key + "'",
                            realm: ErrorRealm);
                    }
                }

                // Step 20: If uncheckedResultKeys is not empty, throw
                if (uncheckedResultKeys.Count > 0)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'ownKeys' on proxy: trap returned extra keys but the proxy target is not extensible",
                        realm: ErrorRealm);
                }
            }
        }

        // Yield validated keys with filtering
        foreach (var propertyName in trapKeys)
        {
            if (!includeSymbols && JsSymbol.TryGetByInternalKey(propertyName, out _))
            {
                continue;
            }

            if (!includeNonEnumerable)
            {
                var desc = GetOwnPropertyDescriptor(propertyName);
                if (desc?.Enumerable != true)
                {
                    continue;
                }
            }

            yield return propertyName;
        }
    }

    /// <summary>
    /// Implements CreateListFromArrayLike for ownKeys (ES spec 7.3.17).
    /// Each element must be a String or Symbol.
    /// </summary>
    private List<string> CreateListFromArrayLike(JsValue obj)
    {
        var result = new List<string>();

        if (obj.TryGetObject<JsArray>(out var jsArray))
        {
            foreach (var item in jsArray.Items)
            {
                var key = ValidateKeyType(item);
                result.Add(key);
            }

            return result;
        }

        // Generic array-like: read "length" and index properties
        var arrayLike = obj.AsObject();
        if (arrayLike is null)
        {
            throw StandardLibrary.ThrowTypeError(
                "CreateListFromArrayLike called on non-object", realm: ErrorRealm);
        }

        if (!arrayLike.TryGetProperty("length", out var lengthVal))
        {
            return result;
        }

        var length = (int)JsOps.ToNumber(lengthVal);
        for (var i = 0; i < length; i++)
        {
            if (arrayLike.TryGetProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture), out var element))
            {
                var key = ValidateKeyType(element);
                result.Add(key);
            }
        }

        return result;
    }

    private string ValidateKeyType(JsValue element)
    {
        // Must be String or Symbol
        if (element.IsString)
        {
            return element.AsString();
        }

        if (element.IsSymbol && element.TryUnwrap<JsSymbol>(out var primitiveSymbol))
        {
            return JsSymbol.PropertyKey(primitiveSymbol);
        }

        if (element.TryGetObject<JsSymbol>(out var symbol))
        {
            return JsSymbol.PropertyKey(symbol);
        }

        throw StandardLibrary.ThrowTypeError(
            "'ownKeys' on proxy: trap result included a non-String, non-Symbol key",
            realm: ErrorRealm);
    }

    // --- [[Get]] with invariant checks ---
    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        if (name.IsPrivateSlotName())
        {
            return _privateStorage.TryGetProperty(name,
                receiver.IsUndefined ? JsValue.FromJsProxy(this) : receiver, out value);
        }

        if (TryGetTrap("get", out var trap))
        {
            var args = new[]
            {
                _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)),
                receiver.IsUndefined ? JsValue.FromJsProxy(this) : receiver
            };
            value = InvokeTrap(trap, args);

            // Invariant checks per ES spec 10.5.8 step 13
            var targetDesc = Target.GetOwnPropertyDescriptor(name);
            if (targetDesc is not null && !targetDesc.Configurable)
            {
                if (targetDesc.IsDataDescriptor && !targetDesc.Writable)
                {
                    // 13a: non-configurable, non-writable data property must return same value
                    if (!JsOps.SameValue(value, targetDesc.JsValue))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'get' on proxy: property '" + name +
                            "' is a read-only and non-configurable data property on the proxy target but the proxy did not return its actual value",
                            realm: ErrorRealm);
                    }
                }
                else if (targetDesc.IsAccessorDescriptor && targetDesc.Get is null)
                {
                    // 13b: non-configurable accessor with undefined getter must return undefined
                    if (!value.IsUndefined)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'get' on proxy: property '" + name +
                            "' is a non-configurable accessor property on the proxy target and does not have a getter function, but the trap did not return 'undefined'",
                            realm: ErrorRealm);
                    }
                }
            }

            return true;
        }

        return Target.TryGetProperty(name, receiver.IsUndefined ? JsValue.FromJsProxy(this) : receiver, out value);
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        return TryGetProperty(name, JsValue.FromJsProxy(this), out value);
    }

    // --- [[Set]] with invariant checks ---
    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        if (!TrySetProperty(name, value, receiver))
        {
            throw StandardLibrary.ThrowTypeError("Proxy 'set' trap returned a falsy value", realm: ErrorRealm);
        }
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromJsProxy(this));
    }

    internal bool TrySetProperty(string name, JsValue value, JsValue receiver)
    {
        var effectiveReceiver = receiver.IsUndefined ? JsValue.FromJsProxy(this) : receiver;

        if (name.IsPrivateSlotName())
        {
            _privateStorage.SetProperty(name, value, effectiveReceiver);
            return true;
        }

        if (TryGetTrap("set", out var trap))
        {
            var args = new[]
            {
                _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)), value,
                effectiveReceiver
            };
            var result = InvokeTrap(trap, args);
            if (!JsOps.ToBoolean(result))
            {
                return false;
            }

            var targetDesc = Target.GetOwnPropertyDescriptor(name);
            if (targetDesc is not null && !targetDesc.Configurable)
            {
                if (targetDesc.IsDataDescriptor && !targetDesc.Writable)
                {
                    if (!JsOps.SameValue(value, targetDesc.JsValue))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'set' on proxy: trap returned truish for property '" + name +
                            "' which exists in the proxy target as a non-configurable and non-writable data property with a different value",
                            realm: ErrorRealm);
                    }
                }
                else if (targetDesc.IsAccessorDescriptor && targetDesc.Set is null)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'set' on proxy: trap returned truish for property '" + name +
                        "' which exists in the proxy target as a non-configurable and non-writable accessor property without a setter",
                        realm: ErrorRealm);
                }
            }

            return true;
        }

        return ReflectHelper.SetPropertyWithReceiver(Target, name, value, effectiveReceiver);
    }

    // --- [[DefineOwnProperty]] with invariant checks ---
    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (name.IsPrivateSlotName())
        {
            _privateStorage.DefineProperty(name, descriptor);
            return;
        }

        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor, CurrentOperationRealm);
            var args = new[]
            {
                _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)),
                (JsValue)descriptorObject
            };
            var result = InvokeTrap(trap, args);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'defineProperty' trap returned a falsy value",
                    realm: ErrorRealm);
            }

            // Invariant checks per ES spec 10.5.6
            ValidateDefinePropertyInvariant(name, descriptor);
            return;
        }

        if (!TryForwardDefineProperty(name, descriptor))
        {
            throw StandardLibrary.ThrowTypeError("Proxy 'defineProperty' trap returned a falsy value",
                realm: ErrorRealm);
        }
    }

    // --- [[GetOwnProperty]] with invariant checks ---
    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name.IsPrivateSlotName())
        {
            return null;
        }

        if (TryGetTrap("getOwnPropertyDescriptor", out var trap))
        {
            var args = new[] { _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)) };
            var result = InvokeTrap(trap, args);

            // Invariant: result must be Object or undefined
            if (!result.IsUndefined && !result.IsObject)
            {
                throw StandardLibrary.ThrowTypeError(
                    "'getOwnPropertyDescriptor' on proxy: trap returned neither Object nor undefined for property '" +
                    name + "'",
                    realm: ErrorRealm);
            }

            var resultDesc = ConvertPropertyDescriptor(result, ErrorRealm);

            // Get the target's own property descriptor for invariant validation
            var targetDesc = Target.GetOwnPropertyDescriptor(name);

            if (resultDesc is null)
            {
                // Trap returned undefined
                if (targetDesc is not null)
                {
                    if (!targetDesc.Configurable)
                    {
                        // Cannot report non-configurable property as non-existent
                        throw StandardLibrary.ThrowTypeError(
                            "'getOwnPropertyDescriptor' on proxy: trap returned undefined for property '" + name +
                            "' which is a non-configurable own property of the proxy target",
                            realm: ErrorRealm);
                    }

                    if (!TargetIsExtensible())
                    {
                        // Cannot report existing property as non-existent on non-extensible target
                        throw StandardLibrary.ThrowTypeError(
                            "'getOwnPropertyDescriptor' on proxy: trap returned undefined for property '" + name +
                            "' which exists in the non-extensible proxy target",
                            realm: ErrorRealm);
                    }
                }

                return null;
            }

            // resultDesc is not undefined/null
            // Set configurable default to true if not present
            var extensibleTarget = TargetIsExtensible();

            if (targetDesc is not null)
            {
                // Validate compatibility
                if (!IsCompatiblePropertyDescriptor(extensibleTarget, resultDesc, targetDesc))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getOwnPropertyDescriptor' on proxy: trap returned incompatible property descriptor for property '" +
                        name + "'",
                        realm: ErrorRealm);
                }

                // If result says non-configurable but target says configurable
                if (resultDesc.HasConfigurable && !resultDesc.Configurable && targetDesc.Configurable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getOwnPropertyDescriptor' on proxy: trap reported non-configurable for property '" + name +
                        "' but the property is configurable on the proxy target",
                        realm: ErrorRealm);
                }

                // If result says non-configurable non-writable but target says writable
                if (resultDesc is { HasConfigurable: true, Configurable: false, IsDataDescriptor: true } &&
                    !resultDesc.Writable &&
                    targetDesc is { IsDataDescriptor: true, Writable: true })
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getOwnPropertyDescriptor' on proxy: trap reported non-configurable and non-writable for property '" +
                        name + "' but the property is writable on the proxy target",
                        realm: ErrorRealm);
                }
            }
            else
            {
                // targetDesc is undefined
                if (!extensibleTarget)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getOwnPropertyDescriptor' on proxy: trap returned a descriptor for property '" + name +
                        "' that does not exist on the non-extensible proxy target",
                        realm: ErrorRealm);
                }

                if (resultDesc.HasConfigurable && !resultDesc.Configurable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getOwnPropertyDescriptor' on proxy: trap reported non-configurable for property '" + name +
                        "' that does not exist on the proxy target",
                        realm: ErrorRealm);
                }
            }

            return resultDesc;
        }

        return Target.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        // Route through ownKeys trap via GetOwnPropertyKeysInOrder, excluding symbols
        return GetOwnPropertyKeysInOrder(false, true);
    }

    // --- [[SetPrototypeOf]] with invariant checks ---
    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        if (TryGetTrap("setPrototypeOf", out var trap))
        {
            var args = new[] { _targetJsValue, JsValue.FromObjectUnsafe(candidate) };
            var result = InvokeTrap(trap, args);
            if (!JsOps.ToBoolean(result))
            {
                throw StandardLibrary.ThrowTypeError("Proxy 'setPrototypeOf' trap returned a falsy value",
                    realm: ErrorRealm);
            }

            // Invariant: if target is not extensible, trap result must match target prototype
            if (!TargetIsExtensible())
            {
                var targetProto = GetTargetPrototype();
                if (!ReferenceEquals(candidate, targetProto))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'setPrototypeOf' on proxy: trap returned truish for setting a new prototype on a non-extensible proxy target",
                        realm: ErrorRealm);
                }
            }

            _meta.SetPrototype(candidate);
            _privateStorage.SetPrototype(_meta.Prototype);
            return;
        }

        Target.SetPrototype(candidate);
        _meta.SetPrototype(candidate);
        _privateStorage.SetPrototype(_meta.Prototype);
    }

    public void Seal()
    {
        Target.Seal();
    }

    // --- [[Delete]] with invariant checks ---
    public bool Delete(string name)
    {
        if (TryGetTrap("deleteProperty", out var trap))
        {
            var args = new[] { _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)) };
            var result = InvokeTrap(trap, args);
            var booleanTrapResult = JsOps.ToBoolean(result);

            if (booleanTrapResult)
            {
                // Invariant checks per ES spec 10.5.10
                var targetDesc = Target.GetOwnPropertyDescriptor(name);
                if (targetDesc is not null)
                {
                    if (!targetDesc.Configurable)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'deleteProperty' on proxy: trap returned truish for property '" + name +
                            "' which is non-configurable in the proxy target",
                            realm: ErrorRealm);
                    }

                    // ES2020+: if target is not extensible, cannot delete existing property
                    if (!TargetIsExtensible())
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'deleteProperty' on proxy: trap returned truish for property '" + name +
                            "' but the proxy target is not extensible",
                            realm: ErrorRealm);
                    }
                }
            }

            return booleanTrapResult;
        }

        return Target.Delete(name);
    }

    public void AddPrivateBrand(object brand)
    {
        _privateBrands.Add(brand);
    }

    public bool HasPrivateBrand(object brand)
    {
        return _privateBrands.Contains(brand);
    }

    // --- [[DefineOwnProperty]] via TryDefineProperty with invariant checks ---
    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (name.IsPrivateSlotName())
        {
            return _privateStorage.TryDefineProperty(name, descriptor);
        }

        if (TryGetTrap("defineProperty", out var trap))
        {
            var descriptorObject = CreateDescriptorObject(descriptor, CurrentOperationRealm);
            var args = new[]
            {
                _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)),
                (JsValue)descriptorObject
            };
            var result = InvokeTrap(trap, args);
            if (!JsOps.ToBoolean(result))
            {
                return false;
            }

            ValidateDefinePropertyInvariant(name, descriptor);
            return true;
        }

        try
        {
            return TryForwardDefineProperty(name, descriptor);
        }
        catch (ThrowSignal)
        {
            return false;
        }
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        _meta is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    // --- [[HasProperty]] with invariant checks ---
    internal bool HasProperty(string name)
    {
        if (TryGetTrap("has", out var trap))
        {
            var args = new[] { _targetJsValue, JsValue.FromObjectUnsafe(DecodePropertyKey(name)) };
            var result = InvokeTrap(trap, args);
            var booleanTrapResult = JsOps.ToBoolean(result);

            if (!booleanTrapResult)
            {
                // Invariant checks per ES spec 10.5.7 step 11
                var targetDesc = Target.GetOwnPropertyDescriptor(name);
                if (targetDesc is not null)
                {
                    if (!targetDesc.Configurable)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'has' on proxy: trap returned falsish for property '" + name +
                            "' which exists in the proxy target as non-configurable",
                            realm: ErrorRealm);
                    }

                    if (!TargetIsExtensible())
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'has' on proxy: trap returned falsish for property '" + name +
                            "' but the proxy target is not extensible",
                            realm: ErrorRealm);
                    }
                }
            }

            return booleanTrapResult;
        }

        if (Target is JsProxy proxyTarget)
        {
            return proxyTarget.HasProperty(name);
        }

        if (Target is JsObject jsObject && jsObject.HasProperty(name))
        {
            return true;
        }

        if (Target.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        var prototype = Target.Prototype;
        while (prototype is not null)
        {
            if (prototype.HasProperty(name))
            {
                return true;
            }

            prototype = prototype.Prototype;
        }

        return Target.TryGetProperty(name, out _);
    }

    // --- [[GetPrototypeOf]] with invariant checks ---
    internal IJsPropertyAccessor? GetPrototypeWithTrap()
    {
        if (TryGetTrap("getPrototypeOf", out var trap))
        {
            var args = new[] { _targetJsValue };
            var result = InvokeTrap(trap, args);

            if (result.IsNull)
            {
                // Invariant: if target is not extensible, must match target prototype
                if (!TargetIsExtensible())
                {
                    var targetProto = GetTargetPrototype();
                    if (targetProto is not null)
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "'getPrototypeOf' on proxy: proxy target is non-extensible but the trap did not return its actual prototype",
                            realm: ErrorRealm);
                    }
                }

                _meta.SetPrototype(null);
                _privateStorage.SetPrototype(null);
                return null;
            }

            if (!result.TryGetObject<IJsPropertyAccessor>(out var resultObj))
            {
                throw StandardLibrary.ThrowTypeError(
                    "Proxy getPrototypeOf trap must return an object or null",
                    realm: ErrorRealm);
            }

            // Invariant: if target is not extensible, must match target prototype
            if (!TargetIsExtensible())
            {
                var targetProto = GetTargetPrototype();
                if (!ReferenceEquals(resultObj, targetProto))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'getPrototypeOf' on proxy: proxy target is non-extensible but the trap did not return its actual prototype",
                        realm: ErrorRealm);
                }
            }

            _meta.SetPrototype(resultObj);
            _privateStorage.SetPrototype(resultObj);
            return resultObj;
        }

        var proto = GetTargetPrototype();

        _meta.SetPrototype(proto);
        _privateStorage.SetPrototype(_meta.Prototype);
        return proto;
    }

    internal bool IsCallableTarget()
    {
        return Target switch
        {
            JsProxy proxyTarget => proxyTarget.IsCallableTarget(),
            IJsCallable => true,
            _ => false
        };
    }

    // --- Helper: check target extensibility ---
    private bool TargetIsExtensible()
    {
        return Target is not IExtensibilityControl extensibility || extensibility.IsExtensible;
    }

    // --- Helper: get target prototype ---
    private IJsPropertyAccessor? GetTargetPrototype()
    {
        if (Target is JsProxy proxyTarget)
        {
            return proxyTarget.GetPrototypeWithTrap();
        }

        var proto = Target.Prototype;
        if (proto is null && Target is IPrototypeAccessorProvider provider)
        {
            return provider.PrototypeAccessor;
        }

        return proto;
    }

    // --- Helper: validate defineProperty invariants ---
    private void ValidateDefinePropertyInvariant(string name, PropertyDescriptor descriptor)
    {
        var targetDesc = Target.GetOwnPropertyDescriptor(name);
        var extensibleTarget = TargetIsExtensible();
        var settingConfigFalse = descriptor.HasConfigurable && !descriptor.Configurable;

        if (targetDesc is null)
        {
            // 19a: target doesn't have the property
            if (!extensibleTarget)
            {
                throw StandardLibrary.ThrowTypeError(
                    "'defineProperty' on proxy: trap returned truish for defining property '" + name +
                    "' on a non-extensible proxy target",
                    realm: ErrorRealm);
            }

            // 19b: cannot define non-configurable property that doesn't exist on target
            if (settingConfigFalse)
            {
                throw StandardLibrary.ThrowTypeError(
                    "'defineProperty' on proxy: trap returned truish for defining non-configurable property '" + name +
                    "' which is either non-existent or configurable in the proxy target",
                    realm: ErrorRealm);
            }
        }
        else
        {
            // 20: target has the property
            // 20a: descriptors must be compatible
            if (!IsCompatiblePropertyDescriptor(extensibleTarget, descriptor, targetDesc))
            {
                throw StandardLibrary.ThrowTypeError(
                    "'defineProperty' on proxy: trap returned truish for property '" + name +
                    "' which is incompatible with the existing property on the proxy target",
                    realm: ErrorRealm);
            }

            // 20b: cannot make non-configurable if target property is configurable
            if (settingConfigFalse && targetDesc.Configurable)
            {
                throw StandardLibrary.ThrowTypeError(
                    "'defineProperty' on proxy: trap returned truish for defining non-configurable property '" + name +
                    "' which is configurable in the proxy target",
                    realm: ErrorRealm);
            }

            // 20c: cannot make non-writable if target is non-configurable and writable
            if (!targetDesc.Configurable && targetDesc.IsDataDescriptor && targetDesc.Writable)
            {
                if (descriptor.HasWritable && !descriptor.Writable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "'defineProperty' on proxy: trap returned truish for defining non-writable property '" + name +
                        "' which is writable and non-configurable in the proxy target",
                        realm: ErrorRealm);
                }
            }
        }
    }

    /// <summary>
    /// Implements the abstract operation IsCompatiblePropertyDescriptor (ES spec 10.1.6.3).
    /// </summary>
    private static bool IsCompatiblePropertyDescriptor(bool extensible, PropertyDescriptor desc,
        PropertyDescriptor current)
    {
        _ = extensible;

        // If current descriptor is the same reference, it's compatible
        if (ReferenceEquals(desc, current))
        {
            return true;
        }

        // If current is non-configurable:
        if (!current.Configurable)
        {
            // Cannot make it configurable
            if (desc.HasConfigurable && desc.Configurable)
            {
                return false;
            }

            // Cannot change enumerable
            if (desc.HasEnumerable && desc.Enumerable != current.Enumerable)
            {
                return false;
            }

            // Cannot change between data and accessor
            if (!desc.IsGenericDescriptor)
            {
                if (desc.IsAccessorDescriptor != current.IsAccessorDescriptor)
                {
                    return false;
                }

                if (current.IsDataDescriptor && desc.IsDataDescriptor)
                {
                    if (!current.Writable)
                    {
                        // Cannot change value of non-writable, non-configurable property
                        if (desc.HasWritable && desc.Writable)
                        {
                            return false;
                        }

                        if (desc.HasValue && !JsOps.SameValue(desc.JsValue, current.JsValue))
                        {
                            return false;
                        }
                    }
                }

                if (current.IsAccessorDescriptor && desc.IsAccessorDescriptor)
                {
                    if (desc.HasGet && !ReferenceEquals(desc.Get, current.Get))
                    {
                        return false;
                    }

                    if (desc.HasSet && !ReferenceEquals(desc.Set, current.Set))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool TryGetTrap(string trapName, out IJsCallable callable)
    {
        var handler = Handler ?? throw StandardLibrary.ThrowTypeError("Cannot perform operation on a revoked Proxy",
            realm: ErrorRealm);
        var handlerJsValue = GetHandlerJsValue();

        if (!handler.TryGetProperty(trapName, handlerJsValue, out var trapValueObj))
        {
            callable = null!;
            return false;
        }

        // trapValueObj is already a JsValue from TryGetProperty
        if (trapValueObj.IsUndefined || trapValueObj.IsNull)
        {
            callable = null!;
            return false;
        }

        if (!trapValueObj.IsObject || !trapValueObj.TryGetObject<IJsCallable>(out var callableTrap))
        {
            throw StandardLibrary.ThrowTypeError($"Proxy handler's '{trapName}' trap is not callable", realm: ErrorRealm);
        }

        callable = callableTrap;
        return true;
    }

    private bool TryForwardDefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (Target is IPropertyDefinitionHost definitionHost)
        {
            return definitionHost.TryDefineProperty(name, descriptor);
        }

        try
        {
            Target.DefineProperty(name, descriptor);
            return true;
        }
        catch (ThrowSignal)
        {
            return false;
        }
    }

    private static object DecodePropertyKey(string propertyName)
    {
        return JsSymbol.TryGetByInternalKey(propertyName, out var symbol)
            ? symbol!
            : propertyName;
    }

    private static PropertyDescriptor? ConvertPropertyDescriptor(JsValue candidate, RealmState? realm)
    {
        if (candidate.IsNull || candidate.IsUndefined)
        {
            return null;
        }

        if (!candidate.IsObject || candidate.AsObject() is not { } descriptorObject)
        {
            throw StandardLibrary.ThrowTypeError(
                "Proxy getOwnPropertyDescriptor trap must return an object or undefined", realm: realm);
        }

        var descriptor = new PropertyDescriptor();

        if (descriptorObject.TryGetProperty("enumerable", out var enumerableValue))
        {
            descriptor.Enumerable = JsOps.ToBoolean(enumerableValue);
        }

        if (descriptorObject.TryGetProperty("configurable", out var configurableValue))
        {
            descriptor.Configurable = JsOps.ToBoolean(configurableValue);
        }

        if (descriptorObject.TryGetProperty("value", out var valueValue))
        {
            descriptor.JsValue = valueValue;
        }

        if (descriptorObject.TryGetProperty("writable", out var writableValue))
        {
            descriptor.Writable = JsOps.ToBoolean(writableValue);
        }

        if (descriptorObject.TryGetProperty("get", out var getterValueObj))
        {
            if (!getterValueObj.IsUndefined &&
                (!getterValueObj.IsObject || !getterValueObj.TryGetObject<IJsCallable>(out _)))
            {
                throw StandardLibrary.ThrowTypeError("Getter must be a function", realm: realm);
            }

            descriptor.Get = getterValueObj.IsUndefined ? null :
                getterValueObj.TryGetObject<IJsCallable>(out var getter) ? getter : null;
        }

        if (descriptorObject.TryGetProperty("set", out var setterValueObj))
        {
            if (!setterValueObj.IsUndefined &&
                (!setterValueObj.IsObject || !setterValueObj.TryGetObject<IJsCallable>(out _)))
            {
                throw StandardLibrary.ThrowTypeError("Setter must be a function", realm: realm);
            }

            descriptor.Set = setterValueObj.IsUndefined ? null :
                setterValueObj.TryGetObject<IJsCallable>(out var setter) ? setter : null;
        }

        if (descriptor is { IsAccessorDescriptor: true, IsDataDescriptor: true })
        {
            throw StandardLibrary.ThrowTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute",
                realm: realm);
        }

        return descriptor;
    }

    private static JsObject CreateDescriptorObject(PropertyDescriptor descriptor, RealmState? realm)
    {
        var result = realm is null
            ? new JsObject()
            : new JsObject(realm.ObjectPrototype) { RealmState = realm };

        if (descriptor.IsAccessorDescriptor)
        {
            if (descriptor.HasGet)
            {
                result.SetProperty("get",
                    descriptor.Get is not null
                        ? JsValue.FromObjectUnsafe(descriptor.Get)
                        : JsValue.Undefined);
            }

            if (descriptor.HasSet)
            {
                result.SetProperty("set",
                    descriptor.Set is not null
                        ? JsValue.FromObjectUnsafe(descriptor.Set)
                        : JsValue.Undefined);
            }
        }
        else
        {
            if (descriptor.HasValue)
            {
                result.SetProperty("value", descriptor.JsValue);
            }

            if (descriptor.HasWritable)
            {
                result.SetProperty("writable", descriptor.Writable);
            }
        }

        if (descriptor.HasEnumerable)
        {
            result.SetProperty("enumerable", descriptor.Enumerable);
        }

        if (descriptor.HasConfigurable)
        {
            result.SetProperty("configurable", descriptor.Configurable);
        }

        return result;
    }
}
