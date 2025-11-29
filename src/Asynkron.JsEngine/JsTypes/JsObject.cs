using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript property descriptor.
/// </summary>
public sealed class PropertyDescriptor
{
    private object? _value;
    private bool _writable = true;
    private bool _enumerable = true;
    private bool _configurable = true;
    private IJsCallable? _get;
    private IJsCallable? _set;

    public object? Value
    {
        get => _value;
        set
        {
            _value = value;
            HasValue = true;
        }
    }

    public bool Writable
    {
        get => _writable;
        set
        {
            _writable = value;
            HasWritable = true;
        }
    }

    public bool Enumerable
    {
        get => _enumerable;
        set
        {
            _enumerable = value;
            HasEnumerable = true;
        }
    }

    public bool Configurable
    {
        get => _configurable;
        set
        {
            _configurable = value;
            HasConfigurable = true;
        }
    }

    public bool HasValue { get; set; }
    public bool HasWritable { get; set; }
    public bool HasEnumerable { get; set; }
    public bool HasConfigurable { get; set; }

    public bool HasGet { get; private set; }
    public bool HasSet { get; private set; }

    public IJsCallable? Get
    {
        get => _get;
        set
        {
            _get = value;
            HasGet = true;
        }
    }

    public IJsCallable? Set
    {
        get => _set;
        set
        {
            _set = value;
            HasSet = true;
        }
    }

    public bool IsAccessorDescriptor => HasGet || HasSet;
    public bool IsDataDescriptor => HasValue || HasWritable;
    public bool IsGenericDescriptor => !IsAccessorDescriptor && !IsDataDescriptor;
    public bool IsEmpty => !HasValue && !HasWritable && !HasEnumerable && !HasConfigurable && !HasGet && !HasSet;

    public PropertyDescriptor Clone()
    {
        var clone = new PropertyDescriptor();
        if (HasValue)
        {
            clone.Value = Value;
        }

        if (HasWritable)
        {
            clone.Writable = Writable;
        }

        if (HasEnumerable)
        {
            clone.Enumerable = Enumerable;
        }

        if (HasConfigurable)
        {
            clone.Configurable = Configurable;
        }

        if (HasGet)
        {
            clone.Get = Get;
        }

        if (HasSet)
        {
            clone.Set = Set;
        }

        return clone;
    }

    public void ClearDataAttributes()
    {
        _value = null;
        HasValue = false;
        _writable = true;
        HasWritable = false;
    }

    public void ClearAccessorAttributes()
    {
        _get = null;
        HasGet = false;
        _set = null;
        HasSet = false;
    }
}

/// <summary>
///     Simple JavaScript-like object that supports prototype chaining for property lookups.
/// </summary>
public sealed class JsObject() : Dictionary<string, object?>(StringComparer.Ordinal), IJsObjectLike, IPrivateBrandHolder,
    IPropertyDefinitionHost, IExtensibilityControl
{
    private const string PrototypeKey = "__proto__";
    private const string GetterPrefix = "__getter__";
    private const string SetterPrefix = "__setter__";
    private readonly Dictionary<string, PropertyDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly List<string> _propertyInsertionOrder = [];
    private readonly HashSet<string> _propertyInsertionSet = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _privateFields = new(StringComparer.Ordinal);
    private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
    private IVirtualPropertyProvider? _virtualPropertyProvider;
    private bool _isExtensible = true;

    public bool IsFrozen { get; private set; }

    private IJsPropertyAccessor? _prototypeAccessor;

    public JsObject? Prototype { get; private set; }

    public bool IsSealed { get; private set; }
    public bool IsExtensible => _isExtensible;

    IEnumerable<string> IJsObjectLike.Keys => Keys;

    private static bool IsPrivateName(string name)
    {
        return name.Length > 0 && name[0] == '#';
    }

    public void SetPrototype(object? candidate)
    {
        _prototypeAccessor = candidate as IJsPropertyAccessor;
        Prototype = candidate as JsObject;

        if (candidate is not null)
        {
            this[PrototypeKey] = candidate;
        }
        else
        {
            Remove(PrototypeKey);
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

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        DefinePropertyInternal(name, descriptor);
    }

    private bool DefinePropertyInternal(string name, PropertyDescriptor descriptor)
    {
        if (IsPrivateName(name))
        {
            _privateFields[name] = descriptor;
            return true;
        }

        var hadStoredDescriptor = _descriptors.TryGetValue(name, out var storedDescriptor);
        var hadDataSlot = ContainsKey(name);
        var currentDescriptor = storedDescriptor;

        if (!hadStoredDescriptor && TryGetValue(name, out var existingValue))
        {
            currentDescriptor = CreateDataDescriptorFromExistingValue(existingValue);
        }

        if (currentDescriptor is null)
        {
            if (!_isExtensible)
            {
                return false;
            }

            var newDescriptor = descriptor.Clone();
            CompleteDescriptorForNewProperty(newDescriptor);
            _descriptors[name] = newDescriptor;
            TrackPropertyInsertion(name);
            AssignDescriptorStorage(name, newDescriptor);
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
        return true;
    }

    private static PropertyDescriptor CreateDataDescriptorFromExistingValue(object? value)
    {
        return new PropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };
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
                descriptor.Writable = true;
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
        if (candidate.IsAccessorDescriptor != currentIsData)
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

        if (!current.Configurable)
        {
            if (candidate.HasGet && !ReferenceEquals(candidate.Get, current.Get))
            {
                return false;
            }

            if (candidate.HasSet && !ReferenceEquals(candidate.Set, current.Set))
            {
                return false;
            }
        }

        return true;
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
        if (left is double ld && right is double rd)
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

        if (left is float lf && right is float rf)
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

        if (left is JsBigInt lbi && right is JsBigInt rbi)
        {
            return lbi == rbi;
        }

        return Equals(left, right);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (IsPrivateName(name))
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
        if (ContainsKey(name))
        {
            return new PropertyDescriptor
            {
                Value = this[name], Writable = true, Enumerable = true, Configurable = true
            };
        }

        return null;
    }

    public bool HasProperty(string name)
    {
        if (IsPrivateName(name))
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
            if (prototype is null)
            {
                return false;
            }

            if (prototype is JsObject jsObject)
            {
                current = jsObject;
                continue;
            }

            return prototype.TryGetProperty(name, out _);
        }

        return false;
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

        if (IsPrivateName(name))
        {
            if (_privateFields.TryGetValue(name, out var existing) && existing is PropertyDescriptor
                {
                    IsAccessorDescriptor: true
                } desc)
            {
                if (desc.Set != null)
                {
                    desc.Set.Invoke([value], receiver ?? this);
                }

                return;
            }

            // If we didn't have an accessor on this object, walk the prototype
            // chain for a private accessor before falling back to defining a slot.
            var prototype = Prototype;
            while (prototype is not null)
            {
                if (prototype._privateFields.TryGetValue(name, out var inherited) &&
                    inherited is PropertyDescriptor { IsAccessorDescriptor: true } inheritedDesc)
                {
                    if (inheritedDesc.Set != null)
                    {
                        inheritedDesc.Set.Invoke([value], receiver ?? this);
                    }

                    return;
                }

                prototype = prototype.Prototype;
            }

            _privateFields[name] = value;
            return;
        }

        var propertyExists = _descriptors.ContainsKey(name) || ContainsKey(name);
        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Set != null)
                {
                    descriptor.Set.Invoke([value], receiver ?? this);
                }

                return;
            }

            if (!descriptor.Writable)
            {
                return; // Silently ignore in non-strict mode
            }

            this[name] = value;
            return;
        }

        if (TryGetValue(name, out _))
        {
            this[name] = value;
            return;
        }

        // First check if this object or its prototype chain has a setter
        var setter = GetSetter(name);
        if (setter != null)
        {
            setter.Invoke([value], receiver ?? this);
            return;
        }

        // When the prototype is a non-JsObject accessor (e.g., HostFunction),
        // inspect its own descriptor and its JsObject prototype chain for a setter.
        if (_prototypeAccessor is IJsObjectLike accessorObject)
        {
            var protoDescriptor = accessorObject.GetOwnPropertyDescriptor(name);
            if (protoDescriptor is not null && protoDescriptor.IsAccessorDescriptor)
            {
                if (protoDescriptor.Set != null)
                {
                    protoDescriptor.Set.Invoke([value], receiver ?? this);
                }

                return;
            }

            if (accessorObject.Prototype is JsObject protoObj &&
                protoObj.GetSetter(name) is { } protoSetter)
            {
                protoSetter.Invoke([value], receiver ?? this);
                return;
            }
        }

        // Frozen objects cannot have properties modified
        if (IsFrozen)
        {
            return; // Silently ignore in non-strict mode
        }

        // Non-extensible objects cannot have new properties added
        if (!_isExtensible && !propertyExists)
        {
            return; // Silently ignore in non-strict mode
        }

        this[name] = value;
        if (!propertyExists)
        {
            TrackPropertyInsertion(name);
        }
    }

    public void PreventExtensions()
    {
        _isExtensible = false;
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
        return TryGetProperty(name, this, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), out value);
    }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return TryGetProperty(name, receiver, new HashSet<object>(ReferenceEqualityComparer<object>.Instance), out value);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var key in EnumerateOwnKeysInOrder(includeSymbols: false, includeNonEnumerable: true))
        {
            yield return key;
        }
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

    public bool Delete(string name)
    {
        return DeleteOwnProperty(name);
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

    private bool TryGetProperty(string name, object? receiver, HashSet<object> visited, out object? value)
    {
        if (IsPrivateName(name))
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
                                value = desc.Get.Invoke([], receiver ?? this);
                                return true;
                            }

                            value = Symbol.Undefined;
                            return true;
                        }

                        value = desc.HasValue ? desc.Value : Symbol.Undefined;
                        return true;
                    default:
                        value = slot;
                        return true;
                }
            }

            if (Prototype is not null && Prototype.TryGetProperty(name, receiver ?? this, visited, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        if (TryGetOwnProperty(name, receiver ?? this, out value))
        {
            return true;
        }

        if (!visited.Add(this))
        {
            value = null;
            return false;
        }

        var prototype = _prototypeAccessor;
        if (prototype is null)
        {
            value = null;
            return false;
        }

        if (prototype is JsObject jsObjPrototype)
        {
            return jsObjPrototype.TryGetProperty(name, receiver ?? this, visited, out value);
        }

        return prototype.TryGetProperty(name, receiver ?? this, out value);
    }

    private bool TryGetOwnProperty(string name, object? receiver, out object? value)
    {
        if (_virtualPropertyProvider is not null &&
            !_descriptors.ContainsKey(name) &&
            !ContainsKey(name) &&
            _virtualPropertyProvider.TryGetOwnProperty(name, out value, out var virtualDescriptor))
        {
            if (virtualDescriptor is not null && virtualDescriptor.IsAccessorDescriptor)
            {
                if (virtualDescriptor.Get != null)
                {
                    value = virtualDescriptor.Get.Invoke([], receiver ?? this);
                }

                return true;
            }

            return true;
        }

        if (_descriptors.TryGetValue(name, out var descriptor))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                if (descriptor.Get != null)
                {
                    value = descriptor.Get.Invoke([], receiver);
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

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        foreach (var key in EnumerateOwnKeysInOrder(includeSymbols: false, includeNonEnumerable: false))
        {
            yield return key;
        }
    }

    // Mirrors [[OwnPropertyKeys]] ordering for enumerable keys (ECMA-262 §7.3.23).
    public IEnumerable<string> GetOwnEnumerablePropertyKeysInOrder(bool includeSymbols = true)
    {
        return EnumerateOwnKeysInOrder(includeSymbols, includeNonEnumerable: false);
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
}

public interface IVirtualPropertyProvider
{
    bool TryGetOwnProperty(string name, out object? value, out PropertyDescriptor? descriptor);
    IEnumerable<string> GetEnumerableKeys();
}

public interface IPrivateBrandHolder
{
    void AddPrivateBrand(object brand);
    bool HasPrivateBrand(object brand);
}

public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    private ReferenceEqualityComparer()
    {
    }

    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(T obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
