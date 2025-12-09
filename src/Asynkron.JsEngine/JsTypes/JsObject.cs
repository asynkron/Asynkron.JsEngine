using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
    public sealed class JsObject : Dictionary<string, object?>, IJsObjectLike,
        IPrivateBrandHolder,
        IPropertyDefinitionHost, IExtensibilityControl, IPrototypeAccessorProvider
    {
    private const string PrototypeKey = "__proto__";
    private const string GetterPrefix = "__getter__";
    private const string SetterPrefix = "__setter__";
    private readonly Dictionary<string, PropertyDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
    private readonly Dictionary<string, object?> _privateFields = new(StringComparer.Ordinal);
    private readonly List<string> _propertyInsertionOrder = [];
    private readonly HashSet<string> _propertyInsertionSet = new(StringComparer.Ordinal);
    private bool _trackArrayLength;
    private double _trackedArrayLength;

    private IJsPropertyAccessor? _prototypeAccessor;
    private IVirtualPropertyProvider? _virtualPropertyProvider;

    internal RealmState? RealmState { get; set; }

    public bool IsFrozen { get; private set; }
    public bool IsExtensible { get; private set; } = true;
    internal bool IsConstructing { get; private set; }

    public JsObject(object? prototype = null) : base(StringComparer.Ordinal)
    {
        if (prototype is not null)
        {
            SetPrototype(prototype);
        }
    }

    // Host-only metadata to help debugging prototype wiring without leaking into JS state.
    public string? Origin { get; set; }

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

    public void PreventExtensions()
    {
        IsExtensible = false;
    }

    public JsObject? Prototype { get; private set; }
    public IJsPropertyAccessor? PrototypeAccessor => _prototypeAccessor;

    public bool IsSealed { get; private set; }

    IEnumerable<string> IJsObjectLike.Keys => Keys;

    public void SetPrototype(object? candidate)
    {
        var previous = _prototypeAccessor ?? Prototype;

        // Per ES spec 9.1.2.1: If the object is not extensible and the new prototype
        // is different from the current prototype, the operation should fail.
        // We silently return without changing (caller can check strict mode for throwing).
        if (!IsExtensible && !ReferenceEquals(previous, candidate) &&
            !ReferenceEquals(previous, candidate as IJsPropertyAccessor) &&
            !ReferenceEquals(previous, candidate as JsObject))
        {
            return;
        }

        _prototypeAccessor = candidate as IJsPropertyAccessor;
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

        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            return descriptor;
        }

        if (_virtualPropertyProvider is not null &&
            !_descriptors.ContainsKey(name) &&
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

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        if (string.Equals(name, PrototypeKey, StringComparison.Ordinal))
        {
            SetPrototype(value);
            return;
        }

        if (name.IsPrivateSlotName())
        {
            if (_privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor
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

                desc.Set.Invoke([value], receiver ?? this);

                return;
            }

            if (_privateFields.TryGetValue(name, out existing) && existing is PropertyDescriptor dataDesc)
            {
                if (!dataDesc.Writable)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Private field is read-only",
                        realm: ResolveRealmState(receiver));
                }

                dataDesc.Value = value;
                _privateFields[name] = dataDesc;
                return;
            }

            // If we didn't have an accessor on this object, walk the prototype
            // chain for a private accessor before falling back to defining a slot.
            var prototype = Prototype;
            while (prototype is not null)
            {
                if (prototype._privateFields.TryGetValue(name, out var inherited))
                {
                    if (inherited is PropertyDescriptor { IsAccessorDescriptor: true } inheritedDesc)
                    {
                        if (inheritedDesc.Set is null)
                        {
                            throw StandardLibrary.ThrowTypeError(
                                "Private accessor does not have a setter",
                                realm: ResolveRealmState(receiver));
                        }

                        inheritedDesc.Set.Invoke([value], receiver ?? this);
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

                        dataDescriptor.Value = value;
                        prototype._privateFields[name] = dataDescriptor;
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

        var hasDescriptor = _descriptors.TryGetValue(name, out var descriptor);
        var hasDataSlot = TryGetValue(name, out _);
        var propertyExists = hasDescriptor || hasDataSlot;

        if (hasDescriptor)
        {
            if (descriptor!.IsAccessorDescriptor)
            {
                descriptor.Set?.Invoke([value], receiver ?? this);

                return;
            }

            if (!descriptor.Writable)
            {
                return; // Silently ignore in non-strict mode
            }

            this[name] = value;
            descriptor.Value = value;
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
            setter.Invoke([value], receiver ?? this);
            return;
        }

        // When the prototype is a non-JsObject accessor (e.g., HostFunction, TypedFunction),
        // recursively traverse the prototype chain looking for a setter.
        // This is needed for class inheritance where SubClass.__proto__ === BaseClass
        // and BaseClass.__proto__ === Function.prototype (which has the restricted setter).
        if (_prototypeAccessor is not null)
        {
            var foundSetter = FindSetterInPrototypeChain(_prototypeAccessor, name);
            if (foundSetter != null)
            {
                foundSetter.Invoke([value], receiver ?? this);
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

    public void Seal()
    {
        PreventExtensions();
        IsSealed = true;

        // Update all existing descriptors to be non-configurable
        foreach (var key in Keys.ToArray())
        {
            if (key == PrototypeKey || key.StartsWith(GetterPrefix) || key.StartsWith(SetterPrefix))
            {
                continue;
            }

            if (_descriptors.TryGetValue(key, out var desc))
            {
                desc.Configurable = false;
            }
            else
            {
                _descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key], Writable = true, Enumerable = true, Configurable = false
                };
            }
        }
    }

    public bool TryGetProperty(string name, out object? value)
    {
        // Private slots need special handling - go through slow path
        if (name.IsPrivateSlotName())
        {
            return TryGetProperty(name, this, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), null,
                out value);
        }

        // Fast path: check own property first without allocating HashSet
        if (TryGetOwnProperty(name, this, null, out value))
            return true;

        // Fast path: no prototype chain to walk
        if (Prototype is null && _prototypeAccessor is null)
        {
            value = null;
            return false;
        }

        // Slow path: need cycle detection for prototype chain traversal
        return TryGetProperty(name, this, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), null,
            out value);
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        // Private slots need special handling - go through slow path
        if (name.IsPrivateSlotName())
        {
            return TryGetProperty(name, receiver, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), null,
                out value);
        }

        // Fast path: check own property first without allocating HashSet
        if (TryGetOwnProperty(name, receiver ?? this, null, out value))
            return true;

        // Fast path: no prototype chain to walk
        if (Prototype is null && _prototypeAccessor is null)
        {
            value = null;
            return false;
        }

        // Slow path: need cycle detection for prototype chain traversal
        return TryGetProperty(name, receiver, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), null,
            out value);
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

    internal void SeedOwnPropertyInsertion(string name)
    {
        TrackPropertyInsertion(name);
    }

    internal void SeedIntrinsicConstructorKeys()
    {
        _propertyInsertionOrder.Clear();
        _propertyInsertionSet.Clear();
        SeedOwnPropertyInsertion("length");
        SeedOwnPropertyInsertion("name");
        SeedOwnPropertyInsertion("prototype");
        foreach (var existing in Keys)
        {
            SeedOwnPropertyInsertion(existing);
        }
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
        _privateBrands.Add(brand);
    }

    public bool HasPrivateBrand(object brand)
    {
        return _privateBrands.Contains(brand);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return DefinePropertyInternal(name, descriptor);
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
        if (name.IsPrivateSlotName())
        {
            if (_privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor existingDescriptor)
            {
                if (!ValidateDescriptorChange(descriptor, existingDescriptor))
                {
                    return false;
                }

                ApplyDescriptorChange(descriptor, existingDescriptor);
                _privateFields[name] = existingDescriptor;
                return true;
            }

            var newDescriptor = descriptor.Clone();
            CompleteDescriptorForNewProperty(newDescriptor);
            _privateFields[name] = newDescriptor;
            return true;
        }

        var hadStoredDescriptor = _descriptors.TryGetValue(name, out var storedDescriptor);
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
            _descriptors[name] = newDescriptor;
            TrackPropertyInsertion(name);
            AssignDescriptorStorage(name, newDescriptor);
            if (_trackArrayLength)
            {
                if (string.Equals(name, "length", StringComparison.Ordinal))
                {
                    TrackLengthAssignment(newDescriptor.Value);
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
            _descriptors[name] = currentDescriptor;
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
                TrackLengthAssignment(currentDescriptor.Value);
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
                descriptor.Value = Symbol.Undefined;
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

                if (candidate.HasValue && !SameValue(candidate.Value, current.Value))
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
            coerced = value is double d ? d : JsOps.ToNumber(value);
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

        if (_descriptors.TryGetValue("length", out var descriptor))
        {
            descriptor.Value = _trackedArrayLength;
        }
        else
        {
            base["length"] = _trackedArrayLength;
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
                target.Value = source.Value;
            }
            else if (target is { IsAccessorDescriptor: false, HasValue: false })
            {
                target.Value = Symbol.Undefined;
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
            this[name] = descriptor.HasValue ? descriptor.Value : Symbol.Undefined;
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

        return HasPropertyCore(this, name, new HashSet<JsObject>(ReferenceEqualityComparer<JsObject>.Instance));
    }

    private static bool HasPropertyCore(JsObject current, string name, HashSet<JsObject> visited)
    {
        while (current is not null && visited.Add(current))
        {
            if (current.GetOwnPropertyDescriptor(name) is not null)
            {
                return true;
            }

            var prototype = current._prototypeAccessor;
            switch (prototype)
            {
                case null:
                    return false;
                case JsObject jsObject:
                    current = jsObject;
                    continue;
                default:
                    return prototype.TryGetProperty(name, out _);
            }
        }

        return false;
    }

    public void SetGetter(string name, IJsCallable getter)
    {
        this[GetterPrefix + name] = getter;
    }

    public void SetSetter(string name, IJsCallable setter)
    {
        this[SetterPrefix + name] = setter;
    }

    public bool HasGetter(string name)
    {
        return TryGetValue(GetterPrefix + name, out _);
    }

    public bool HasSetter(string name)
    {
        return TryGetValue(SetterPrefix + name, out _);
    }

    public IJsCallable? GetGetter(string name)
    {
        var current = this;
        var visited = ReferenceEqualityComparer.Instance;
        var seen = new HashSet<JsObject>(visited);

        while (current is not null && seen.Add(current))
        {
            if (current.TryGetValue(GetterPrefix + name, out var getter) &&
                getter is IJsCallable callable)
            {
                return callable;
            }

            current = current.Prototype;
        }

        return null;
    }

    public IJsCallable? GetSetter(string name)
    {
        var current = this;
        var visited = ReferenceEqualityComparer.Instance;
        var seen = new HashSet<JsObject>(visited);

        while (current is not null && seen.Add(current))
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
    /// non-JsObject prototypes like TypedFunction. This is needed for
    /// class inheritance where the prototype chain may be:
    /// SubClass -> BaseClass -> Function.prototype
    /// </summary>
    private static IJsCallable? FindSetterInPrototypeChain(IJsPropertyAccessor? current, string name)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        while (current is not null && seen.Add(current))
        {
            // Check if this level has an own accessor property with a setter
            var descriptor = current.GetOwnPropertyDescriptor(name);
            if (descriptor?.IsAccessorDescriptor == true && descriptor.Set is not null)
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

    public bool DeleteOwnProperty(string name)
    {
        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            if (!descriptor.Configurable)
            {
                return false;
            }

            _descriptors.Remove(name);
            Remove(GetterPrefix + name);
            Remove(SetterPrefix + name);
            Remove(name);
            RemoveFromInsertionOrder(name);
            return true;
        }

        if (Remove(name))
        {
            RemoveFromInsertionOrder(name);
            return true;
        }

        // Property does not exist; delete is a no-op that succeeds.
        return true;
    }

    public void Freeze()
    {
        PreventExtensions();
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed

        // Update all existing descriptors to be non-writable and non-configurable
        foreach (var key in Keys.ToArray())
        {
            if (key == PrototypeKey || key.StartsWith(GetterPrefix) || key.StartsWith(SetterPrefix))
            {
                continue;
            }

            if (_descriptors.TryGetValue(key, out var desc))
            {
                desc.Writable = false;
                desc.Configurable = false;
            }
            else
            {
                _descriptors[key] = new PropertyDescriptor
                {
                    Value = this[key], Writable = false, Enumerable = true, Configurable = false
                };
            }
        }
    }

    internal bool TryGetProperty(string name, object? receiver, EvaluationContext? context,
        out object? value)
    {
        // Private slots need special handling - go through slow path
        if (name.IsPrivateSlotName())
        {
            return TryGetProperty(name, receiver, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), context,
                out value);
        }

        // Fast path: check own property first without allocating HashSet
        if (TryGetOwnProperty(name, receiver ?? this, context, out value))
            return true;

        // Fast path: no prototype chain to walk
        if (Prototype is null && _prototypeAccessor is null)
        {
            value = null;
            return false;
        }

        // Slow path: need cycle detection for prototype chain traversal
        return TryGetProperty(name, receiver, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), context,
            out value);
    }

    internal bool HasPrivateField(string name)
    {
        return _privateFields.ContainsKey(name);
    }

    private bool TryGetProperty(string name, object? receiver, HashSet<object> visited,
        EvaluationContext? context, out object? value)
    {
        if (name.IsPrivateSlotName())
        {
            if (_privateFields.TryGetValue(name, out var slot))
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
                                value = TypedAstEvaluator.InvokeCallable(
                                    desc.Get,
                                    Array.Empty<object?>(),
                                    receiver ?? this,
                                    context,
                                    ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
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

                        value = desc.HasValue ? desc.Value : Symbol.Undefined;
                        return true;
                    default:
                        value = slot;
                        return true;
                }
            }

            if (Prototype is not null &&
                Prototype.TryGetProperty(name, receiver ?? this, visited, context, out value))
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

        if (!visited.Add(this))
        {
            value = null;
            return false;
        }

        var prototype = _prototypeAccessor;
        if (prototype is null && TryGetValue(PrototypeKey, out var protoCandidate) &&
            protoCandidate is IJsPropertyAccessor accessor)
        {
            prototype = accessor;
        }
        while (prototype is not null)
        {
            if (prototype is JsObject jsProto)
            {
                if (jsProto.TryGetProperty(name, receiver ?? this, visited, context, out value))
                {
                    return true;
                }
            }
            else if (prototype.TryGetProperty(name, receiver ?? this, out value))
            {
                return true;
            }

            if (prototype is IPrototypeAccessorProvider provider && provider.PrototypeAccessor is { } next)
            {
                prototype = next;
                continue;
            }

            if (prototype is IJsObjectLike objLike && objLike.Prototype is { } objProto)
            {
        prototype = objProto;
        continue;
    }

            if (prototype is JsObject jsObj && jsObj.Prototype is { } jsObjProto)
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
            !_descriptors.ContainsKey(name) &&
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
                    value = TypedAstEvaluator.InvokeCallable(
                        virtualDescriptor.Get,
                        Array.Empty<object?>(),
                        receiver ?? this,
                        context,
                        ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
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

        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Get != null)
                {
                    try
                    {
                        value = TypedAstEvaluator.InvokeCallable(
                            descriptor.Get,
                            Array.Empty<object?>(),
                            receiver ?? this,
                            context,
                            ResolveRealmState(receiver)?.Engine?.GlobalEnvironment);
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

            value = descriptor.HasValue ? descriptor.Value : Symbol.Undefined;
            return true;
        }

        if (TryGetValue(name, out value))
        {
            return true;
        }

        value = null;
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

        if (_propertyInsertionSet.Add(name))
        {
            _propertyInsertionOrder.Add(name);
        }
    }

    private void RemoveFromInsertionOrder(string name)
    {
        if (!_propertyInsertionSet.Remove(name))
        {
            return;
        }

        var index = _propertyInsertionOrder.IndexOf(name);
        if (index >= 0)
        {
            _propertyInsertionOrder.RemoveAt(index);
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

        foreach (var key in _propertyInsertionOrder)
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
        return name == PrototypeKey ||
               name.StartsWith(GetterPrefix, StringComparison.Ordinal) ||
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

        if (receiver is JsObject receiverObj && receiverObj.RealmState is { } receiverRealm)
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
}
