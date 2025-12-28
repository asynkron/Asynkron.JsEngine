#region

using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Collections;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
public sealed class JsObject : IDictionary<string, object?>, IJsObjectLike,
    IPrivateBrandHolder,
    IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider, IAsJsValue
{
    private const string PrototypeKey = "__proto__";
    private const string GetterPrefix = "__getter__";
    private const string SetterPrefix = "__setter__";
    private JsPromise? _promiseSlot;

    private JsObjectState? _state;
    private bool _trackArrayLength;
    private double _trackedArrayLength;

    private IVirtualPropertyProvider? _virtualPropertyProvider;

    // Cached JsValue to avoid repeated struct creation
    // ReSharper disable once ReplaceWithFieldKeyword
    private readonly JsValue _cachedJsValue;

    public JsObject(IJsPropertyAccessor? prototype = null)
    {
        _cachedJsValue = new JsValue(this);
        if (prototype is not null)
        {
            SetPrototype(prototype);
        }
    }

    /// <inheritdoc />
    public ref readonly JsValue AsJsValue => ref _cachedJsValue;

    private int MutationVersion { get; set; }

    private JsObjectState State => _state ??= new JsObjectState();

    internal RealmState? RealmState { get; set; }
    internal bool IsConstructing { get; private set; }

    // Host-only metadata to help debugging prototype wiring without leaking into JS state.
    public string? Origin { get; set; }

#pragma warning disable CS0618 // IDictionary<string, object?> implementation requires ToObject() at API boundary
    // IDictionary<string, object?> implementation - wraps JsValue storage
    public ICollection<string> Keys => _state?.Storage.Keys ?? Array.Empty<string>();
    public ICollection<object?> Values => _state?.Storage.Values.Select(static v => v.ToObject()).ToList() ?? [];
    public int Count => _state?.Storage.Count ?? 0;
    public bool IsReadOnly => false;

    // External API uses object? for compatibility
    public object? this[string key]
    {
        get
        {
            if (_state is null || !_state.Storage.TryGetValue(key, out var jsValue))
            {
                throw new KeyNotFoundException();
            }

            return jsValue.ToObject();
        }
        set
        {
            MarkMutated();
            State.Storage[key] = JsValue.FromObjectUnsafe(value);
        }
    }

    public void Add(string key, object? value)
    {
        MarkMutated();
        State.Storage.Add(key, JsValue.FromObjectUnsafe(value));
    }

    public bool ContainsKey(string key)
    {
        return _state?.Storage.ContainsKey(key) ?? false;
    }

    public bool Remove(string key)
    {
        if (_state is null)
        {
            return false;
        }

        var removed = _state.Storage.Remove(key);
        if (removed)
        {
            MarkMutated();
        }

        return removed;
    }

    public bool TryGetValue(string key, out object? value)
    {
        if (_state is not null && _state.Storage.TryGetValue(key, out var jsValue))
        {
            value = jsValue.ToObject();
            return true;
        }

        value = null;
        return false;
    }

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
    {
        MarkMutated();
        State.Storage.Add(item.Key, JsValue.FromObjectUnsafe(item.Value));
    }

    public void Clear()
    {
        if (_state is null)
        {
            return;
        }

        MarkMutated();
        _state.Storage.Clear();
    }

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
    {
        return _state is not null &&
               _state.Storage.TryGetValue(item.Key, out var v) &&
               Equals(v.ToObject(), item.Value);
    }

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        if (_state is null)
        {
            return;
        }

        foreach (var kvp in _state.Storage)
        {
            array[arrayIndex++] = new KeyValuePair<string, object?>(kvp.Key, kvp.Value.ToObject());
        }
    }

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
    {
        if (_state is null ||
            !_state.Storage.TryGetValue(item.Key, out var v) ||
            !Equals(v.ToObject(), item.Value) ||
            !_state.Storage.Remove(item.Key))
        {
            return false;
        }

        MarkMutated();
        return true;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return (_state?.Storage ?? [])
            .Select(static kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value.ToObject()))
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
#pragma warning restore CS0618
    }

    public bool IsExtensible { get; private set; } = true;

    public void PreventExtensions()
    {
        MarkMutated();
        IsExtensible = false;
    }

    public bool IsFrozen { get; private set; }

    public JsObject? Prototype { get; private set; }

    public bool IsSealed { get; private set; }

    IEnumerable<string> IJsObjectLike.Keys => _state?.Storage.Keys ?? Array.Empty<string>();

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        MarkMutated();
        var previous = PrototypeAccessor ?? Prototype;
        PrototypeAccessor = candidate as IJsPropertyAccessor;
        Prototype = candidate as JsObject;

        if (!ReferenceEquals(previous, candidate))
        {
            RealmState?.Logger?.LogInformation(
                "Prototype reassigned on {ObjectId}: {OldPrototype} -> {NewPrototype}",
                RuntimeHelpers.GetHashCode(this),
                DescribePrototype(previous),
                DescribePrototype(candidate));
        }
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        DefinePropertyInternal(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name.IsPrivateSlotName())
        {
            return null;
        }

        var state = _state;
        if (state is not null && state.Descriptors.TryGetValue(name, out var descriptor))
        {
            return descriptor;
        }

        if (_virtualPropertyProvider is not null &&
            (state is null || !state.Descriptors.ContainsKey(name)) &&
            !ContainsKey(name) &&
            _virtualPropertyProvider.TryGetOwnProperty(name, out _, out var virtualDescriptor))
        {
            return virtualDescriptor;
        }

        // If no explicit descriptor but property exists, return default descriptor
        if (TryGetValue(name, out var existingValue))
        {
            return new PropertyDescriptor
            {
                Value = existingValue, Writable = true, Enumerable = true, Configurable = true
            };
        }

        return null;
    }

    // IJsPropertyAccessor interface implementation with JsValue
    public void SetProperty(string name, JsValue value)
    {
        SetPropertyJsValue(name, value, JsValue.FromJsObject(this));
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        SetPropertyJsValue(name, value, receiver);
    }

    public void Seal()
    {
        MarkMutated();
        PreventExtensions();
        IsSealed = true;

        var state = _state;
        if (state is null)
        {
            return;
        }

        // Update all existing descriptors to be non-configurable
        foreach (var key in state.Storage.Keys.ToArray())
        {
            if (key.StartsWith(GetterPrefix, StringComparison.Ordinal) ||
                key.StartsWith(SetterPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Descriptors.TryGetValue(key, out var desc))
            {
                desc.Configurable = false;
            }
            else
            {
                state.Descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key], Writable = true, Enumerable = true, Configurable = false
                };
            }
        }
    }

    // IJsPropertyAccessor interface implementation with JsValue
    public bool TryGetProperty(string name, out JsValue value)
    {
        // Super-fast path: direct storage access for simple data properties
        // This bypasses descriptors and virtual providers when they don't apply
        if (_virtualPropertyProvider is null &&
            !(_state?.Descriptors.ContainsKey(name) ?? false) &&
            !name.IsPrivateSlotName())
        {
            if (TryGetJsValue(name, out value))
            {
                return true;
            }

            // Check prototype chain if we have one
            if (Prototype is JsObject protoObj)
            {
                return protoObj.TryGetProperty(name, out value);
            }

            if (PrototypeAccessor is IJsPropertyAccessor protoAccessor)
            {
                return protoAccessor.TryGetProperty(name, out value);
            }

            value = JsValue.Undefined;
            return false;
        }

        // Slow path: full property lookup with descriptors, virtual providers, etc.
        // Use JsValue version to avoid boxing
        return TryGetPropertyInternalJsValue(name, out value);
    }

    public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
    {
        // Use JsValue version to avoid boxing
        return TryGetPropertyJsValue(name, receiver, 0, null, out value);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var key in EnumerateOwnKeysInOrder(false, true))
        {
            yield return key;
        }
    }

    public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true, bool includeNonEnumerable = true)
    {
        return EnumerateOwnKeysInOrder(includeSymbols, includeNonEnumerable);
    }

    public bool Delete(string name)
    {
        return DeleteOwnProperty(name);
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var key in EnumerateOwnKeysInOrder(false, false))
        {
            yield return key;
        }
    }

    public void AddPrivateBrand(object brand)
    {
        MarkMutated();
        State.PrivateBrands.Add(brand);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasPrivateBrand(object brand)
    {
        return _state?.PrivateBrands.Contains(brand) ?? false;
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return DefinePropertyInternal(name, descriptor);
    }

    public IJsPropertyAccessor? PrototypeAccessor { get; private set; }

    private void MarkMutated()
    {
        unchecked
        {
            MutationVersion++;
        }
    }

    internal void SetPromiseSlot(JsPromise promise)
    {
        _promiseSlot = promise;
    }

    internal bool TryGetPromiseSlot(out JsPromise? promise)
    {
        promise = _promiseSlot;
        return promise is not null;
    }

    internal void EnableArrayLengthTracking(double initialLength = 0)
    {
        _trackArrayLength = true;
        _trackedArrayLength = Math.Max(initialLength, 0);
        SyncTrackedLengthDescriptor();
    }

    internal void BeginConstruction()
    {
        IsConstructing = true;
    }

    internal void EndConstruction()
    {
        IsConstructing = false;
    }

    // Internal fast path - no boxing
    internal JsValue GetJsValue(string key)
    {
        if (_state is null || !_state.Storage.TryGetValue(key, out var jsValue))
        {
            throw new KeyNotFoundException();
        }

        return jsValue;
    }

    internal void SetJsValue(string key, JsValue value)
    {
        MarkMutated();
        State.Storage[key] = value;
    }

    internal bool TryGetJsValue(string key, out JsValue value)
    {
        if (_state is not null)
        {
            return _state.Storage.TryGetValue(key, out value);
        }

        value = default;
        return false;
    }

    internal void CloneFromSnapshot(
        JsObject original,
        Func<object?, object?> cloneValue,
        Func<PropertyDescriptor, PropertyDescriptor> cloneDescriptor,
        Func<string, bool>? shouldSkipKey,
        RealmState? newRealm)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(cloneValue);
        ArgumentNullException.ThrowIfNull(cloneDescriptor);

        if (ReferenceEquals(this, original))
        {
            return;
        }

        if (original._state is null)
        {
            _state = null;
        }
        else
        {
            _state ??= new JsObjectState();
            _state.Storage.Clear();
            _state.Descriptors.Clear();
            _state.PrivateBrands.Clear();
            _state.PrivateFields.Clear();
            _state.PropertyInsertionOrder.Clear();
            _state.PropertyInsertionNodes.Clear();

            foreach (var kv in original._state.Storage)
            {
                if (shouldSkipKey?.Invoke(kv.Key) == true)
                {
                    continue;
                }

#pragma warning disable CS0618 // cloneValue callback requires object?
                var clonedValue = cloneValue(kv.Value.ToObject());
#pragma warning restore CS0618
                _state.Storage[kv.Key] = JsValue.FromObjectUnsafe(clonedValue);
            }

            foreach (var kv in original._state.Descriptors)
            {
                if (shouldSkipKey?.Invoke(kv.Key) == true)
                {
                    continue;
                }

                _state.Descriptors[kv.Key] = cloneDescriptor(kv.Value);
            }

            foreach (var kv in original._state.PrivateFields)
            {
                _state.PrivateFields[kv.Key] = kv.Value switch
                {
                    PropertyDescriptor desc => cloneDescriptor(desc),
                    _ => cloneValue(kv.Value)
                };
            }

            foreach (var brand in original._state.PrivateBrands)
            {
                if (cloneValue(brand) is { } clonedBrand)
                {
                    _state.PrivateBrands.Add(clonedBrand);
                }
            }

            foreach (var key in original._state.PropertyInsertionOrder)
            {
                var node = _state.PropertyInsertionOrder.AddLast(key);
                _state.PropertyInsertionNodes[key] = node;
            }
        }

        _trackArrayLength = original._trackArrayLength;
        _trackedArrayLength = original._trackedArrayLength;
        IsFrozen = original.IsFrozen;
        IsSealed = original.IsSealed;
        IsExtensible = original.IsExtensible;
        IsConstructing = original.IsConstructing;
        _virtualPropertyProvider = original._virtualPropertyProvider;
        _promiseSlot = cloneValue(original._promiseSlot) as JsPromise;
        PrototypeAccessor = cloneValue(original.PrototypeAccessor) as IJsPropertyAccessor;
        Prototype = cloneValue(original.Prototype) as JsObject;
        Origin = original.Origin;
        RealmState = original.RealmState is null ? null : newRealm;
        MutationVersion = original.MutationVersion;
    }

    /// <summary>
    /// Returns true if any descriptor key is a numeric array index.
    /// Used by JsArray to detect custom property descriptors (getters/setters) on numeric indices.
    /// </summary>
    internal bool HasNumericDescriptorKeys()
    {
        var state = _state;
        if (state is null)
        {
            return false;
        }

        foreach (var key in state.Descriptors.Keys)
        {
            if (uint.TryParse(key, out _))
            {
                return true;
            }
        }

        return false;
    }

    // Fast path for JsValue that avoids boxing entirely
    private void SetPropertyJsValue(string name, JsValue value, JsValue receiver)
    {
        MarkMutated();
        // Fast path is only valid when the receiver is this object.
        // When receiver differs (e.g. function objects using an internal property bag, Reflect.set with explicit receiver),
        // we must honor full [[Set]] semantics which may invoke inherited setters instead of creating an own property.
        var receiverIsThis = receiver.IsUndefined ||
                             (receiver.Kind == JsValueKind.Object && ReferenceEquals(receiver.ObjectValue, this));

        // Fast path: no descriptors and no private fields - just set the slot.
        //
        // IMPORTANT: This fast path is only valid when our prototype chain is represented
        // by JsObject instances (Prototype != null) or there is no prototype at all.
        // When Prototype is null but _prototypeAccessor is set (e.g. HostFunction/SyncFunctionInvoker
        // prototypes like Function.prototype), we must use the full [[Set]] semantics to allow
        // inherited setters (e.g. poison-pill Function.prototype.caller/arguments) to run.
        //
        // Additionally, we must check if there's a setter on the prototype chain before taking
        // the fast path. If a setter exists, we must use the full [[Set]] semantics to invoke it.
        if (receiverIsThis &&
            !name.IsPrivateSlotName() &&
            !(_state?.Descriptors.ContainsKey(name) ?? false) &&
            !(Prototype is null && PrototypeAccessor is not null) &&
            GetSetter(name) is null)  // NEW: Check for setters on prototype chain
        {
            // Track if this is a new property before setting it
            var isNewProperty = _state is null || !_state.Storage.ContainsKey(name);
            SetJsValue(name, value);
            TrackArrayWriteJsValue(name);
            if (isNewProperty)
            {
                TrackPropertyInsertion(name);
            }

            return;
        }

        // Fall back to full implementation for complex cases
        // Pass JsValue directly (will be boxed) - better than ToObject() which loses type info
        SetPropertyInternal(name, value, receiver);
    }

    private void TrackArrayWriteJsValue(string name)
    {
        if (!_trackArrayLength)
        {
            return;
        }

        if (!uint.TryParse(name, out var idx))
        {
            return;
        }

        if (!(idx >= _trackedArrayLength))
        {
            return;
        }

        _trackedArrayLength = idx + 1;
        SyncTrackedLengthDescriptor();
    }

    // Internal implementation that uses object? for backward compatibility
    private void SetPropertyInternal(string name, object? value, object? receiver)
    {
        var state = State;
        var privateFields = state.PrivateFields;
        if (name.IsPrivateSlotName())
        {
            if (privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor
                {
                    IsAccessorDescriptor: true
                } desc)
            {
                if (desc.Set is null)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Private accessor does not have a setter",
                        realm: ResolveRealmState(receiver));
                }

                desc.Set.Invoke([JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver ?? this));

                return;
            }

            if (privateFields.TryGetValue(name, out existing) && existing is PropertyDescriptor dataDesc)
            {
                if (!dataDesc.Writable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Private field is read-only",
                        realm: ResolveRealmState(receiver));
                }

                dataDesc.JsValue = JsValue.FromObjectUnsafe(value);
                privateFields[name] = dataDesc;
                return;
            }

            // If we didn't have an accessor on this object, walk the prototype
            // chain for a private accessor before falling back to defining a slot.
            var prototype = Prototype;
            var depth = 0;
            while (prototype is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
            {
                if (prototype._state is { } protoState &&
                    protoState.PrivateFields.TryGetValue(name, out var inherited))
                {
                    if (inherited is PropertyDescriptor { IsAccessorDescriptor: true } inheritedDesc)
                    {
                        if (inheritedDesc.Set is null)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                "Private accessor does not have a setter",
                                realm: ResolveRealmState(receiver));
                        }

                        inheritedDesc.Set.Invoke([JsValue.FromObjectUnsafe(value)],
                            JsValue.FromObjectUnsafe(receiver ?? this));
                        return;
                    }

                    if (inherited is PropertyDescriptor dataDescriptor)
                    {
                        if (!dataDescriptor.Writable)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                "Private field is read-only",
                                realm: ResolveRealmState(receiver));
                        }

                        dataDescriptor.JsValue = JsValue.FromObjectUnsafe(value);
                        protoState.PrivateFields[name] = dataDescriptor;
                        return;
                    }
                }

                prototype = prototype.Prototype;
            }

            // Per ES spec PrivateFieldSet, if the entry is empty (field not found), throw TypeError.
            // Private fields should only be created via DefineProperty during class initialization,
            // not via SetProperty during assignment.
            throw StandardLibrary.ThrowTypeError(
                "Cannot set private field before it has been initialized",
                realm: ResolveRealmState(receiver));
        }

        var hasDescriptor = state.Descriptors.TryGetValue(name, out var descriptor);
        var hasDataSlot = TryGetValue(name, out _);
        var propertyExists = hasDescriptor || hasDataSlot;

        if (hasDescriptor)
        {
            if (descriptor!.IsAccessorDescriptor)
            {
                descriptor.Set?.Invoke([JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver ?? this));

                return;
            }

            if (!descriptor.Writable)
            {
                return; // Silently ignore in non-strict mode
            }

            this[name] = value;
            descriptor.JsValue = JsValue.FromObjectUnsafe(value);
            TrackArrayWrite(name, value);
            return;
        }

        if (hasDataSlot)
        {
            this[name] = value;
            TrackArrayWrite(name, value);
            return;
        }

        // First check if this object or its prototype chain has a setter
        var setter = GetSetter(name);
        if (setter != null)
        {
            setter.Invoke([JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver ?? this));
            return;
        }

        // When the prototype is a non-JsObject accessor (e.g., HostFunction, SyncFunctionInvoker),
        // recursively traverse the prototype chain looking for a setter.
        // This is needed for class inheritance where SubClass.__proto__ === BaseClass
        // and BaseClass.__proto__ === Function.prototype (which has the restricted setter).
        if (PrototypeAccessor is not null)
        {
            var foundSetter = FindSetterInPrototypeChain(PrototypeAccessor, name);
            if (foundSetter != null)
            {
                foundSetter.Invoke([JsValue.FromObjectUnsafe(value)], JsValue.FromObjectUnsafe(receiver ?? this));
                return;
            }
        }

        // Frozen objects cannot have properties modified
        if (IsFrozen)
        {
            return; // Silently ignore in non-strict mode
        }

        // Non-extensible objects cannot have new properties added
        if (!IsExtensible && !propertyExists)
        {
            return; // Silently ignore in non-strict mode
        }

        this[name] = value;
        TrackArrayWrite(name, value);
        if (!propertyExists)
        {
            TrackPropertyInsertion(name);
        }
    }

    /// <summary>
    /// JsValue version of TryGetPropertyInternal that avoids boxing.
    /// </summary>
    private bool TryGetPropertyInternalJsValue(string name, out JsValue value)
    {
        // Private slots need special handling - go through slow path
        if (name.IsPrivateSlotName())
        {
            return TryGetPropertyJsValue(name, JsValue.FromJsObject(this), 0, null, out value);
        }

        // Fast path: check own property first without allocating HashSet
        if (TryGetOwnPropertyJsValue(name, JsValue.FromJsObject(this), null, out value))
        {
            return true;
        }

        // Fast path: no prototype chain to walk
        if (Prototype is null && PrototypeAccessor is null)
        {
            value = JsValue.Undefined;
            return false;
        }

        // Slow path: need depth-limited prototype chain traversal
        return TryGetPropertyJsValue(name, JsValue.FromJsObject(this), 0, null, out value);
    }

    /// <summary>
    /// JsValue version of prototype chain traversal that avoids boxing.
    /// </summary>
    private bool TryGetPropertyJsValue(string name, JsValue receiver, int depth,
        EvaluationContext? context, out JsValue value)
    {
        if (depth >= JsEngineConstants.MaxPrototypeChainDepth)
        {
            value = JsValue.Undefined;
            return false;
        }

        if (name.IsPrivateSlotName())
        {
            if (_state is { } state && state.PrivateFields.TryGetValue(name, out var slot))
            {
                switch (slot)
                {
                    case PropertyDescriptor desc:
                        if (desc.IsAccessorDescriptor)
                        {
                            if (desc.Get != null)
                            {
                                try
                                {
                                    value = TypedAstEvaluator.InvokeCallableJsValue(
                                        desc.Get,
                                        [],
                                        receiver.IsUndefined ? JsValue.FromJsObject(this) : receiver,
                                        context,
                                        ResolveRealmState(receiver.IsObject ? receiver.ObjectValue : null)?.Engine
                                            ?.GlobalEnvironment);
                                }
                                catch (ThrowSignal signal)
                                {
                                    if (context is not null)
                                    {
                                        context.SetThrow(signal.ThrownValue);
                                        value = signal.ThrownValue;
                                        return true;
                                    }

                                    throw;
                                }

                                return true;
                            }

                            throw StandardLibrary.ThrowTypeError(
                                "Private accessor does not have a getter",
                                realm: ResolveRealmState(receiver.IsObject ? receiver.ObjectValue : null));
                        }

                        value = desc.HasValue ? desc.JsValue : JsValue.Undefined;
                        return true;
                    default:
                        value = JsValue.FromObjectUnsafe(slot);
                        return true;
                }
            }

            if (Prototype is JsObject protoWithPrivate)
            {
                return protoWithPrivate.TryGetPropertyJsValue(name, receiver, depth + 1, context, out value);
            }

            throw StandardLibrary.ThrowTypeError(
                $"Cannot read private field {name}",
                realm: ResolveRealmState(receiver.IsObject ? receiver.ObjectValue : null));
        }

        if (TryGetOwnPropertyJsValue(name, receiver, context, out value))
        {
            return true;
        }

        var prototype = ResolvePrototypeAccessor();
        if (prototype is null)
        {
            value = JsValue.Undefined;
            return false;
        }

        if (prototype is JsObject jsProto)
        {
            return jsProto.TryGetPropertyJsValue(name, receiver, depth + 1, context, out value);
        }

        if (prototype.TryGetProperty(name, receiver, out value))
        {
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    internal void SeedOwnPropertyInsertion(string name)
    {
        TrackPropertyInsertion(name);
    }

    internal void SeedIntrinsicConstructorKeys()
    {
        var state = State;
        state.PropertyInsertionOrder.Clear();
        state.PropertyInsertionNodes.Clear();
        SeedOwnPropertyInsertion("length");
        SeedOwnPropertyInsertion("name");
        SeedOwnPropertyInsertion("prototype");
        foreach (var existing in Keys)
        {
            SeedOwnPropertyInsertion(existing);
        }
    }

    /// <summary>
    /// Defines a property by taking direct ownership of the descriptor without cloning.
    /// Use this only when the descriptor is freshly created and won't be reused.
    /// This avoids allocation overhead for internal operations.
    /// </summary>
    internal bool DefinePropertyDirect(string name, PropertyDescriptor descriptor)
    {
        return DefinePropertyInternalDirect(name, descriptor);
    }

    private void TrackArrayWrite(string name, object? value)
    {
        if (!_trackArrayLength)
        {
            return;
        }

        if (string.Equals(name, "length", StringComparison.Ordinal))
        {
            TrackLengthAssignment(value);
            return;
        }

        TrackArrayIndexWriteIfNeeded(name);
    }

    private bool DefinePropertyInternal(string name, PropertyDescriptor descriptor)
    {
        MarkMutated();
        var state = State;
        var privateFields = state.PrivateFields;
        var descriptors = state.Descriptors;
        if (name.IsPrivateSlotName())
        {
            if (privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor existingDescriptor)
            {
                if (!ValidateDescriptorChange(descriptor, existingDescriptor))
                {
                    return false;
                }

                ApplyDescriptorChange(descriptor, existingDescriptor);
                privateFields[name] = existingDescriptor;
                return true;
            }

            var newDescriptor = descriptor.Clone();
            CompleteDescriptorForNewProperty(newDescriptor);
            privateFields[name] = newDescriptor;
            return true;
        }

        var hadStoredDescriptor = descriptors.TryGetValue(name, out var storedDescriptor);
        var hadDataSlot = TryGetValue(name, out var existingValue);
        var currentDescriptor = storedDescriptor;

        if (!hadStoredDescriptor && hadDataSlot)
        {
            currentDescriptor = CreateDataDescriptorFromExistingValue(existingValue);
        }

        if (currentDescriptor is null)
        {
            if (!IsExtensible)
            {
                return false;
            }

            var newDescriptor = descriptor.Clone();
            CompleteDescriptorForNewProperty(newDescriptor);
            descriptors[name] = newDescriptor;
            TrackPropertyInsertion(name);
            AssignDescriptorStorage(name, newDescriptor);
            if (_trackArrayLength)
            {
                if (string.Equals(name, "length", StringComparison.Ordinal))
                {
                    TrackLengthAssignment(newDescriptor.JsValue.ToObject());
                }
                else if (!newDescriptor.IsAccessorDescriptor)
                {
                    TrackArrayIndexWriteIfNeeded(name);
                }
            }

            return true;
        }

        if (!ValidateDescriptorChange(descriptor, currentDescriptor))
        {
            return false;
        }

        ApplyDescriptorChange(descriptor, currentDescriptor);

        if (!hadStoredDescriptor)
        {
            descriptors[name] = currentDescriptor;
            if (!hadDataSlot)
            {
                TrackPropertyInsertion(name);
            }
        }

        AssignDescriptorStorage(name, currentDescriptor);
        if (_trackArrayLength)
        {
            if (string.Equals(name, "length", StringComparison.Ordinal))
            {
                TrackLengthAssignment(currentDescriptor.JsValue.ToObject());
            }
            else if (!currentDescriptor.IsAccessorDescriptor)
            {
                TrackArrayIndexWriteIfNeeded(name);
            }
        }

        return true;
    }

    /// <summary>
    /// Internal method that takes direct ownership of the descriptor without cloning.
    /// Used for internal operations where we know the descriptor is freshly created.
    /// </summary>
    private bool DefinePropertyInternalDirect(string name, PropertyDescriptor descriptor)
    {
        MarkMutated();
        var state = State;
        var privateFields = state.PrivateFields;
        var descriptors = state.Descriptors;
        if (name.IsPrivateSlotName())
        {
            if (privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor existingDescriptor)
            {
                if (!ValidateDescriptorChange(descriptor, existingDescriptor))
                {
                    return false;
                }

                ApplyDescriptorChange(descriptor, existingDescriptor);
                privateFields[name] = existingDescriptor;
                return true;
            }

            // Take ownership directly without cloning
            CompleteDescriptorForNewProperty(descriptor);
            privateFields[name] = descriptor;
            return true;
        }

        var hadStoredDescriptor = descriptors.TryGetValue(name, out var storedDescriptor);
        var hadDataSlot = TryGetValue(name, out var existingValue);
        var currentDescriptor = storedDescriptor;

        if (!hadStoredDescriptor && hadDataSlot)
        {
            currentDescriptor = CreateDataDescriptorFromExistingValue(existingValue);
        }

        if (currentDescriptor is null)
        {
            if (!IsExtensible)
            {
                return false;
            }

            // Take ownership directly without cloning
            CompleteDescriptorForNewProperty(descriptor);
            descriptors[name] = descriptor;
            TrackPropertyInsertion(name);
            AssignDescriptorStorage(name, descriptor);
            if (_trackArrayLength)
            {
                if (string.Equals(name, "length", StringComparison.Ordinal))
                {
                    TrackLengthAssignment(descriptor.JsValue.ToObject());
                }
                else if (!descriptor.IsAccessorDescriptor)
                {
                    TrackArrayIndexWriteIfNeeded(name);
                }
            }

            return true;
        }

        if (!ValidateDescriptorChange(descriptor, currentDescriptor))
        {
            return false;
        }

        ApplyDescriptorChange(descriptor, currentDescriptor);

        if (!hadStoredDescriptor)
        {
            descriptors[name] = currentDescriptor;
            if (!hadDataSlot)
            {
                TrackPropertyInsertion(name);
            }
        }

        AssignDescriptorStorage(name, currentDescriptor);
        if (_trackArrayLength)
        {
            if (string.Equals(name, "length", StringComparison.Ordinal))
            {
                TrackLengthAssignment(currentDescriptor.JsValue.ToObject());
            }
            else if (!currentDescriptor.IsAccessorDescriptor)
            {
                TrackArrayIndexWriteIfNeeded(name);
            }
        }

        return true;
    }

    private static PropertyDescriptor CreateDataDescriptorFromExistingValue(object? value)
    {
        return new PropertyDescriptor { Value = value, Writable = true, Enumerable = true, Configurable = true };
    }

    private static void CompleteDescriptorForNewProperty(PropertyDescriptor descriptor)
    {
        if (descriptor.IsGenericDescriptor || descriptor.IsDataDescriptor)
        {
            if (!descriptor.HasValue)
            {
                descriptor.JsValue = JsValue.Undefined;
            }

            if (!descriptor.HasWritable)
            {
                descriptor.Writable = false;
            }
        }
        else
        {
            if (!descriptor.HasGet)
            {
                descriptor.Get = null;
            }

            if (!descriptor.HasSet)
            {
                descriptor.Set = null;
            }
        }

        if (!descriptor.HasEnumerable)
        {
            descriptor.Enumerable = false;
        }

        if (!descriptor.HasConfigurable)
        {
            descriptor.Configurable = false;
        }
    }

    private static bool ValidateDescriptorChange(PropertyDescriptor candidate, PropertyDescriptor current)
    {
        if (candidate.IsEmpty)
        {
            return true;
        }

        if (!current.Configurable)
        {
            if (candidate is { HasConfigurable: true, Configurable: true })
            {
                return false;
            }

            if (candidate.HasEnumerable && candidate.Enumerable != current.Enumerable)
            {
                return false;
            }
        }

        if (candidate.IsGenericDescriptor)
        {
            return true;
        }

        var currentIsData = current.IsDataDescriptor;
        var currentIsAccessor = current.IsAccessorDescriptor;
        var candidateIsAccessor = candidate.IsAccessorDescriptor;

        if (candidateIsAccessor != currentIsAccessor)
        {
            return current.Configurable;
        }

        if (currentIsData && candidate.IsDataDescriptor)
        {
            if (current is { Configurable: false, Writable: false })
            {
                if (candidate is { HasWritable: true, Writable: true })
                {
                    return false;
                }

                if (candidate.HasValue && !SameValue(candidate.JsValue.ToObject(), current.JsValue.ToObject()))
                {
                    return false;
                }
            }

            return true;
        }

        if (current.Configurable)
        {
            return true;
        }

        if (candidate.HasGet && !ReferenceEquals(candidate.Get, current.Get))
        {
            return false;
        }

        if (candidate.HasSet && !ReferenceEquals(candidate.Set, current.Set))
        {
            return false;
        }

        return true;
    }

    private void TrackArrayIndexWriteIfNeeded(string name)
    {
        if (!_trackArrayLength)
        {
            return;
        }

        if (!TryParseArrayIndex(name, out var index))
        {
            return;
        }

        var candidate = (double)index + 1;
        if (candidate > _trackedArrayLength)
        {
            _trackedArrayLength = candidate;
            SyncTrackedLengthDescriptor();
        }
    }

    private void TrackLengthAssignment(object? value)
    {
        if (!_trackArrayLength)
        {
            return;
        }

        double coerced;
        try
        {
#pragma warning disable CS0618 // Method accepts object?, ToNumber converts appropriately
            coerced = value is double d ? d : JsOps.ToNumber(JsValue.FromObjectUnsafe(value), null);
#pragma warning restore CS0618
        }
        catch (ThrowSignal)
        {
            return;
        }

        if (double.IsNaN(coerced) || double.IsInfinity(coerced) || coerced < 0)
        {
            return;
        }

        _trackedArrayLength = Math.Floor(coerced);
        SyncTrackedLengthDescriptor();
    }

    private void SyncTrackedLengthDescriptor()
    {
        if (!_trackArrayLength)
        {
            return;
        }

        var state = State;
        if (state.Descriptors.TryGetValue("length", out var descriptor))
        {
            descriptor.JsValue = _trackedArrayLength;
        }
        else
        {
            state.Storage["length"] = _trackedArrayLength;
        }
    }

    private static bool TryParseArrayIndex(string propertyName, out uint index)
    {
        index = 0;
        if (propertyName.Length == 0 || propertyName.Length > 10)
        {
            return false;
        }

        if (propertyName[0] == '0' && propertyName.Length > 1)
        {
            return false;
        }

        return uint.TryParse(propertyName, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static void ApplyDescriptorChange(PropertyDescriptor source, PropertyDescriptor target)
    {
        if (source.IsEmpty)
        {
            return;
        }

        var sourceIsGeneric = source.IsGenericDescriptor;
        var sourceIsData = source.IsDataDescriptor;
        var sourceIsAccessor = source.IsAccessorDescriptor;
        var targetIsData = target.IsDataDescriptor;

        if (!sourceIsGeneric && sourceIsAccessor && targetIsData)
        {
            target.ClearDataAttributes();
        }

        if (!sourceIsGeneric && sourceIsData && !targetIsData)
        {
            target.ClearAccessorAttributes();
        }

        if (sourceIsData || sourceIsGeneric)
        {
            if (source.HasValue)
            {
                target.JsValue = source.JsValue;
            }
            else if (target is { IsAccessorDescriptor: false, HasValue: false })
            {
                target.JsValue = JsValue.Undefined;
            }

            if (source.HasWritable)
            {
                target.Writable = source.Writable;
            }
            else if (target is { IsAccessorDescriptor: false, HasWritable: false })
            {
                target.Writable = false;
            }
        }

        if (sourceIsAccessor || sourceIsGeneric)
        {
            if (source.HasGet)
            {
                target.Get = source.Get;
            }

            if (source.HasSet)
            {
                target.Set = source.Set;
            }
        }

        if (source.HasEnumerable)
        {
            target.Enumerable = source.Enumerable;
        }

        if (source.HasConfigurable)
        {
            target.Configurable = source.Configurable;
        }
    }

    private void AssignDescriptorStorage(string name, PropertyDescriptor descriptor)
    {
        if (descriptor.IsAccessorDescriptor)
        {
            if (descriptor is { HasGet: true, Get: not null })
            {
                this[GetterPrefix + name] = descriptor.Get;
            }
            else
            {
                Remove(GetterPrefix + name);
            }

            if (descriptor is { HasSet: true, Set: not null })
            {
                this[SetterPrefix + name] = descriptor.Set;
            }
            else
            {
                Remove(SetterPrefix + name);
            }

            Remove(name);
        }
        else
        {
            this[name] = descriptor.HasValue ? descriptor.JsValue.ToObject() : Symbol.Undefined;
            Remove(GetterPrefix + name);
            Remove(SetterPrefix + name);
        }
    }

    private static bool SameValue(object? left, object? right)
    {
        switch (left)
        {
            case double ld when right is double rd:
            {
                if (double.IsNaN(ld) && double.IsNaN(rd))
                {
                    return true;
                }

                if (ld == 0.0 && rd == 0.0)
                {
                    return BitConverter.DoubleToInt64Bits(ld) == BitConverter.DoubleToInt64Bits(rd);
                }

                return ld.Equals(rd);
            }
            case float lf when right is float rf:
            {
                if (float.IsNaN(lf) && float.IsNaN(rf))
                {
                    return true;
                }

                if (lf == 0f && rf == 0f)
                {
                    return BitConverter.SingleToInt32Bits(lf) == BitConverter.SingleToInt32Bits(rf);
                }

                return lf.Equals(rf);
            }
            case JsBigInt lbi when right is JsBigInt rbi:
                return lbi == rbi;
            default:
                return Equals(left, right);
        }
    }

    public bool HasProperty(string name)
    {
        if (name.IsPrivateSlotName())
        {
            return false;
        }

        return HasPropertyCore(this, name);
    }

    /// <summary>
    /// Walks the prototype chain to check if a property exists.
    /// Uses a simple depth limit instead of HashSet allocation since prototype
    /// cycles should never exist in a correctly functioning engine.
    /// </summary>
    private static bool HasPropertyCore(JsObject? current, string name)
    {
        var depth = 0;

        while (current is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            if (current.GetOwnPropertyDescriptor(name) is not null)
            {
                return true;
            }

            var prototype = current.PrototypeAccessor;
            switch (prototype)
            {
                case null:
                    return false;
                case JsObject jsProto:
                    current = jsProto;
                    continue;
                default:
                    // Non-JsObject prototype - delegate to interface
                    return prototype.TryGetProperty(name, out _);
            }
        }

        return false;
    }

    public IJsCallable? GetSetter(string name)
    {
        var current = this;
        var depth = 0;

        while (current is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            if (current.TryGetValue(SetterPrefix + name, out var setter) &&
                setter is IJsCallable callable)
            {
                return callable;
            }

            current = current.Prototype;
        }

        return null;
    }

    /// <summary>
    /// Recursively searches for a setter in the prototype chain, handling
    /// non-JsObject prototypes like SyncFunctionInvoker. This is needed for
    /// class inheritance where the prototype chain may be:
    /// SubClass -> BaseClass -> Function.prototype
    /// </summary>
    private static IJsCallable? FindSetterInPrototypeChain(IJsPropertyAccessor? current, string name)
    {
        var depth = 0;

        while (current is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            // Check if this level has an own accessor property with a setter
            var descriptor = current.GetOwnPropertyDescriptor(name);
            if (descriptor is { IsAccessorDescriptor: true, Set: not null })
            {
                return descriptor.Set;
            }

            // Get the next prototype in the chain
            // First check if it's an IJsObjectLike with a Prototype property
            IJsPropertyAccessor? next = null;

            if (current is IJsObjectLike objectLike)
            {
                // Prototype might be either JsObject or another IJsPropertyAccessor
                next = objectLike.Prototype;
            }

            // Also check for IPrototypeAccessorProvider which can have non-JsObject prototypes
            if (next is null && current is IPrototypeAccessorProvider prototypeProvider)
            {
                next = prototypeProvider.PrototypeAccessor;
            }

            current = next;
        }

        return null;
    }

    internal bool ForceDeleteOwnProperty(string name)
    {
        var deletedDescriptor = _state?.Descriptors.Remove(name) ?? false;
        Remove(GetterPrefix + name);
        Remove(SetterPrefix + name);
        var deletedValue = Remove(name);
        if (!deletedDescriptor && !deletedValue)
        {
            return false;
        }

        MarkMutated();
        RemoveFromInsertionOrder(name);
        return true;

    }

    public bool DeleteOwnProperty(string name)
    {
        var state = _state;
        if (state is not null && state.Descriptors.TryGetValue(name, out var descriptor))
        {
            if (!descriptor.Configurable)
            {
                return false;
            }

            state.Descriptors.Remove(name);
            Remove(GetterPrefix + name);
            Remove(SetterPrefix + name);
            Remove(name);
            MarkMutated();
            RemoveFromInsertionOrder(name);
            return true;
        }

        if (Remove(name))
        {
            MarkMutated();
            RemoveFromInsertionOrder(name);
            return true;
        }

        // Property does not exist; delete is a no-op that succeeds.
        return true;
    }

    public void Freeze()
    {
        MarkMutated();
        PreventExtensions();
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed

        var state = _state;
        if (state is null)
        {
            return;
        }

        // Update all existing descriptors to be non-writable and non-configurable
        foreach (var key in state.Storage.Keys.ToArray())
        {
            if (key.StartsWith(GetterPrefix, StringComparison.Ordinal) ||
                key.StartsWith(SetterPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Descriptors.TryGetValue(key, out var desc))
            {
                desc.Writable = false;
                desc.Configurable = false;
            }
            else
            {
                state.Descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key], Writable = false, Enumerable = true, Configurable = false
                };
            }
        }
    }

#pragma warning disable CS0618 // TryGetProperty returns object? for compatibility, requires ToObject() conversions
    internal bool TryGetProperty(string name, object? receiver, EvaluationContext? context,
        out object? value)
    {
        // Private slots need special handling - go through slow path
        if (name.IsPrivateSlotName())
        {
            return TryGetProperty(name, receiver, 0, context, out value);
        }

        // Fast path: check own property first without allocating HashSet
        if (TryGetOwnProperty(name, receiver ?? this, context, out value))
        {
            return true;
        }

        // Fast path: no prototype chain to walk
        if (Prototype is null && PrototypeAccessor is null)
        {
            value = null;
            return false;
        }

        var effectiveReceiver = receiver ?? this;

        // Allocation-free prototype walk for common acyclic, shallow chains.
        const int fastPrototypeDepthLimit = 8;
        var visitedFast = ArrayPool<IJsPropertyAccessor?>.Shared.Rent(fastPrototypeDepthLimit);
        var visitedCount = 0;
        try
        {
            var prototype = ResolvePrototypeAccessor();
            while (prototype is not null && visitedCount < fastPrototypeDepthLimit)
            {
                visitedFast[visitedCount++] = prototype;

                if (prototype is JsObject jsProto)
                {
                    if (jsProto.TryGetOwnProperty(name, effectiveReceiver, context, out value))
                    {
                        return true;
                    }
                }
                else if (prototype.TryGetProperty(name, JsValue.FromObjectUnsafe(effectiveReceiver), out var jsValue))
                {
                    value = jsValue.ToObject();
                    return true;
                }

                prototype = GetNextPrototypeAccessor(prototype);
            }

            if (prototype is null)
            {
                value = null;
                return false;
            }

            // Continue traversal with depth limit (cycles shouldn't exist in prototype chains)
            return TryGetPropertyFromPrototypeChain(prototype, name, effectiveReceiver, visitedCount + 1, context,
                out value);
        }
        finally
        {
            Array.Clear(visitedFast, 0, visitedCount);
            ArrayPool<IJsPropertyAccessor?>.Shared.Return(visitedFast);
        }
    }

    private IJsPropertyAccessor? ResolvePrototypeAccessor()
    {
        var prototype = PrototypeAccessor;
        if (prototype is null && TryGetValue(PrototypeKey, out var protoCandidate) &&
            protoCandidate is IJsPropertyAccessor accessor)
        {
            prototype = accessor;
        }

        prototype ??= Prototype;
        return prototype;
    }

    private static IJsPropertyAccessor? GetNextPrototypeAccessor(IJsPropertyAccessor prototype)
    {
        if (prototype is IPrototypeAccessorProvider { PrototypeAccessor: { } nextAccessor })
        {
            return nextAccessor;
        }

        if (prototype is IJsObjectLike { Prototype: { } objProto })
        {
            return objProto;
        }

        if (prototype is JsObject { Prototype: { } jsObjProto })
        {
            return jsObjProto;
        }

        return null;
    }

    private static bool TryGetPropertyFromPrototypeChain(
        IJsPropertyAccessor? prototype,
        string name,
        object? receiver,
        int currentDepth,
        EvaluationContext? context,
        out object? value)
    {
        while (prototype is not null && currentDepth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            if (prototype is JsObject jsProto)
            {
                if (jsProto.TryGetOwnProperty(name, receiver, context, out value))
                {
                    return true;
                }
            }
            else if (prototype.TryGetProperty(name, JsValue.FromObjectUnsafe(receiver), out var jsValue))
            {
                value = jsValue.ToObject();
                return true;
            }

            prototype = GetNextPrototypeAccessor(prototype);
        }

        value = null;
        return false;
    }

    internal bool HasPrivateField(string name)
    {
        return _state?.PrivateFields.ContainsKey(name) ?? false;
    }

    private bool TryGetProperty(string name, object? receiver, int depth,
        EvaluationContext? context, out object? value)
    {
        if (depth >= JsEngineConstants.MaxPrototypeChainDepth)
        {
            value = null;
            return false;
        }

        if (name.IsPrivateSlotName())
        {
            if (_state is { } state && state.PrivateFields.TryGetValue(name, out var slot))
            {
                switch (slot)
                {
                    case PropertyDescriptor desc:
                        if (desc.IsAccessorDescriptor)
                        {
                            if (desc.Get != null)
                            {
                                try
                                {
                                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                                        desc.Get,
                                        [],
                                        JsValue.FromObjectUnsafe(receiver ?? this),
                                        context,
                                        ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
                                    value = result.IsObject ? result.ObjectValue : result.ToObject();
                                }
                                catch (ThrowSignal signal)
                                {
                                    if (context is not null)
                                    {
                                        context.SetThrow(signal.ThrownValue);
                                        value = signal.ThrownValue;
                                        return true;
                                    }

                                    throw;
                                }

                                return true;
                            }

                            throw StandardLibrary.ThrowTypeError(
                                "Private accessor does not have a getter",
                                realm: ResolveRealmState(receiver));
                        }

                        value = desc.HasValue ? desc.JsValue.ToObject() : Symbol.Undefined;
                        return true;
                    default:
                        value = slot;
                        return true;
                }
            }

            if (Prototype is not null &&
                Prototype.TryGetProperty(name, receiver ?? this, depth + 1, context, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        if (TryGetOwnProperty(name, receiver ?? this, context, out value))
        {
            return true;
        }

        var prototype = PrototypeAccessor;
        if (prototype is null && TryGetValue(PrototypeKey, out var protoCandidate) &&
            protoCandidate is IJsPropertyAccessor accessor)
        {
            prototype = accessor;
        }

        while (prototype is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            if (prototype is JsObject jsProto)
            {
                if (jsProto.TryGetProperty(name, receiver ?? this, depth, context, out value))
                {
                    return true;
                }
            }
            else if (prototype.TryGetProperty(name, JsValue.FromObjectUnsafe(receiver ?? this), out var jsValue))
            {
                value = jsValue.ToObject();
                return true;
            }

            if (prototype is IPrototypeAccessorProvider { PrototypeAccessor: { } next })
            {
                prototype = next;
                continue;
            }

            if (prototype is IJsObjectLike { Prototype: { } objProto })
            {
                prototype = objProto;
                continue;
            }

            if (prototype is JsObject { Prototype: { } jsObjProto })
            {
                prototype = jsObjProto;
                continue;
            }

            break;
        }

        value = null;
        return false;
    }

    private bool TryGetOwnProperty(string name, object? receiver, EvaluationContext? context, out object? value)
    {
        if (_virtualPropertyProvider is not null &&
            (_state is null || !_state.Descriptors.ContainsKey(name)) &&
            !ContainsKey(name) &&
            _virtualPropertyProvider.TryGetOwnProperty(name, out value, out var virtualDescriptor))
        {
            if (virtualDescriptor?.IsAccessorDescriptor != true)
            {
                return true;
            }

            if (virtualDescriptor.Get != null)
            {
                try
                {
                    var result = TypedAstEvaluator.InvokeCallableJsValue(
                        virtualDescriptor.Get,
                        [],
                        JsValue.FromObjectUnsafe(receiver ?? this),
                        context,
                        ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
                    value = result.IsObject ? result.ObjectValue : result.ToObject();
                }
                catch (ThrowSignal signal)
                {
                    if (context is not null)
                    {
                        context.SetThrow(signal.ThrownValue);
                        value = signal.ThrownValue;
                        return true;
                    }

                    throw;
                }
            }

            return true;
        }

        if (_state is not null && _state.Descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Get != null)
                {
                    try
                    {
                        var result = TypedAstEvaluator.InvokeCallableJsValue(
                            descriptor.Get,
                            [],
                            JsValue.FromObjectUnsafe(receiver ?? this),
                            context,
                            ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
                        value = result.IsObject ? result.ObjectValue : result.ToObject();
                    }
                    catch (ThrowSignal signal)
                    {
                        if (context is not null)
                        {
                            context.SetThrow(signal.ThrownValue);
                            value = signal.ThrownValue;
                            return true;
                        }

                        throw;
                    }

                    return true;
                }

                value = Symbol.Undefined;
                return true;
            }

            if (TryGetValue(name, out value))
            {
                return true;
            }

            value = descriptor.HasValue ? descriptor.JsValue.ToObject() : Symbol.Undefined;
            return true;
        }

        if (TryGetValue(name, out value))
        {
            return true;
        }

        value = null;
        return false;
    }
#pragma warning restore CS0618

    /// <summary>
    /// JsValue version of TryGetOwnProperty that avoids boxing for primitives.
    /// </summary>
    private bool TryGetOwnPropertyJsValue(string name, JsValue receiver, EvaluationContext? context, out JsValue value)
    {
        if (_virtualPropertyProvider is not null &&
            (_state is null || !_state.Descriptors.ContainsKey(name)) &&
            !ContainsKey(name) &&
            _virtualPropertyProvider.TryGetOwnProperty(name, out var virtualValue, out var virtualDescriptor))
        {
            if (virtualDescriptor?.IsAccessorDescriptor != true)
            {
                value = JsValue.FromObjectUnsafe(virtualValue);
                return true;
            }

            if (virtualDescriptor.Get != null)
            {
                try
                {
                    value = TypedAstEvaluator.InvokeCallableJsValue(
                        virtualDescriptor.Get,
                        [],
                        receiver.IsUndefined ? JsValue.FromJsObject(this) : receiver,
                        context,
                        ResolveRealmState(receiver.IsObject ? receiver.ObjectValue : null)?.Engine?.GlobalEnvironment);
                }
                catch (ThrowSignal signal)
                {
                    if (context is not null)
                    {
                        context.SetThrow(signal.ThrownValue);
                        value = signal.ThrownValue;
                        return true;
                    }

                    throw;
                }
            }
            else
            {
                value = JsValue.Undefined;
            }

            return true;
        }

        if (_state is not null && _state.Descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Get != null)
                {
                    try
                    {
                        value = TypedAstEvaluator.InvokeCallableJsValue(
                            descriptor.Get,
                            [],
                            receiver.IsUndefined ? JsValue.FromJsObject(this) : receiver,
                            context,
                            ResolveRealmState(receiver.IsObject ? receiver.ObjectValue : null)?.Engine
                                ?.GlobalEnvironment);
                    }
                    catch (ThrowSignal signal)
                    {
                        if (context is not null)
                        {
                            context.SetThrow(signal.ThrownValue);
                            value = signal.ThrownValue;
                            return true;
                        }

                        throw;
                    }

                    return true;
                }

                value = JsValue.Undefined;
                return true;
            }

            if (TryGetJsValue(name, out value))
            {
                return true;
            }

            value = descriptor.HasValue ? descriptor.JsValue : JsValue.Undefined;
            return true;
        }

        if (TryGetJsValue(name, out value))
        {
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    // Mirrors [[OwnPropertyKeys]] ordering for enumerable keys (ECMA-262 §7.3.23).
    public IEnumerable<string> GetOwnEnumerablePropertyKeysInOrder(bool includeSymbols = true)
    {
        return EnumerateOwnKeysInOrder(includeSymbols, false);
    }

    public void SetVirtualPropertyProvider(IVirtualPropertyProvider provider)
    {
        _virtualPropertyProvider = provider;
    }

    private void TrackPropertyInsertion(string name)
    {
        if (IsInternalKey(name))
        {
            return;
        }

        var state = State;
        if (!state.PropertyInsertionNodes.ContainsKey(name))
        {
            var node = state.PropertyInsertionOrder.AddLast(name);
            state.PropertyInsertionNodes[name] = node;
        }
    }

    private void RemoveFromInsertionOrder(string name)
    {
        var state = _state;
        if (state is not null && state.PropertyInsertionNodes.TryGetValue(name, out var node))
        {
            state.PropertyInsertionOrder.Remove(node);
            state.PropertyInsertionNodes.Remove(name);
        }
    }

    private IEnumerable<string> EnumerateOwnKeysInOrder(bool includeSymbols, bool includeNonEnumerable)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (_virtualPropertyProvider is not null)
        {
            foreach (var key in _virtualPropertyProvider.GetEnumerableKeys())
            {
                if (!includeSymbols && IsSymbolKey(key))
                {
                    continue;
                }

                if (seen.Add(key))
                {
                    yield return key;
                }
            }
        }

        var numericKeys = new List<uint>();
        var stringKeys = new List<string>();
        var symbolKeys = new List<string>();

        foreach (var key in _state?.PropertyInsertionOrder ?? [])
        {
            if (IsInternalKey(key))
            {
                continue;
            }

            var descriptor = GetOwnPropertyDescriptor(key);
            if (descriptor is null)
            {
                continue;
            }

            if (!includeNonEnumerable && descriptor is { HasEnumerable: true, Enumerable: false })
            {
                continue;
            }

            if (IsArrayIndexString(key, out var index))
            {
                numericKeys.Add(index);
                continue;
            }

            if (IsSymbolKey(key))
            {
                if (includeSymbols)
                {
                    symbolKeys.Add(key);
                }

                continue;
            }

            stringKeys.Add(key);
        }

        numericKeys.Sort();
        foreach (var index in numericKeys)
        {
            yield return index.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var key in stringKeys)
        {
            yield return key;
        }

        foreach (var key in symbolKeys)
        {
            if (seen.Add(key))
            {
                yield return key;
            }
        }
    }

    private static string DescribePrototype(object? candidate)
    {
        if (candidate is null)
        {
            return "null";
        }

        var typeName = candidate.GetType().Name;
        if (candidate is JsObject jsObj)
        {
            var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
            return $"{typeName}@{RuntimeHelpers.GetHashCode(candidate)} origin='{origin}'";
        }

        return $"{typeName}@{RuntimeHelpers.GetHashCode(candidate)}";
    }

    private static bool IsInternalKey(string name)
    {
        // Note: __proto__ (PrototypeKey) is NOT internal - it can be a user-defined own property
        // when defined via shorthand { __proto__ } syntax. The prototype is stored in
        // _prototypeAccessor and Prototype fields, not as a property.
        return name.StartsWith(GetterPrefix, StringComparison.Ordinal) ||
               name.StartsWith(SetterPrefix, StringComparison.Ordinal);
    }

    private static bool IsSymbolKey(string key)
    {
        return key.StartsWith("@@symbol:", StringComparison.Ordinal);
    }

    private static bool IsArrayIndexString(string key, out uint index)
    {
        var isIndex = uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                      index != uint.MaxValue &&
                      string.Equals(index.ToString(CultureInfo.InvariantCulture), key, StringComparison.Ordinal);
        return isIndex;
    }

    private RealmState? ResolveRealmState(object? receiver)
    {
        if (RealmState is { } ownRealm)
        {
            return ownRealm;
        }

        if (receiver is JsObject { RealmState: { } receiverRealm })
        {
            return receiverRealm;
        }

        var proto = Prototype;
        while (proto is not null)
        {
            if (proto.RealmState is { } realm)
            {
                return realm;
            }

            proto = proto.Prototype;
        }

        return null;
    }

    private sealed class JsObjectState
    {
        internal readonly Dictionary<string, PropertyDescriptor> Descriptors = new(StringComparer.Ordinal);
        internal readonly HashSet<object> PrivateBrands = new(ReferenceEqualityComparer<object>.Instance);
        internal readonly Dictionary<string, object?> PrivateFields = new(StringComparer.Ordinal);

        internal readonly Dictionary<string, LinkedListNode<string>> PropertyInsertionNodes =
            new(StringComparer.Ordinal);

        internal readonly LinkedList<string> PropertyInsertionOrder = [];
        internal readonly HybridDictionary<JsValue> Storage = new();
    }
}
