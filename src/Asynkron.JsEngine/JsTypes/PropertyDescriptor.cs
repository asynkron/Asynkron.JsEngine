using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript property descriptor.
/// </summary>
public sealed class PropertyDescriptor
{
    private bool _configurable = true;
    private bool _enumerable = true;
    private IJsCallable? _get;
    private IJsCallable? _set;
    private object? _value;
    private bool _writable = true;

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

    public bool IsAccessorDescriptor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasGet || HasSet;
    }

    public bool IsDataDescriptor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasValue || HasWritable;
    }

    public bool IsGenericDescriptor
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsAccessorDescriptor && !IsDataDescriptor;
    }

    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasValue && !HasWritable && !HasEnumerable && !HasConfigurable && !HasGet && !HasSet;
    }

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
