#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript property descriptor.
/// </summary>
public sealed class PropertyDescriptor
{
    private IJsCallable? _get;
    private JsValue _jsValue;
    private IJsCallable? _set;
    private bool _writable = true;

    /// <summary>
    /// Gets or sets the value as JsValue (preferred, avoids boxing).
    /// </summary>
    public JsValue JsValue
    {
        get => _jsValue;
        set
        {
            _jsValue = value;
            HasValue = true;
        }
    }

    /// <summary>
    /// Gets or sets the value as object (for backward compatibility, causes boxing).
    /// </summary>
    public object? Value
    {


        set => JsValue = JsValue.FromObjectUnsafe(value);
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
        get;
        set
        {
            field = value;
            HasEnumerable = true;
        }
    } = true;

    public bool Configurable
    {
        get;
        set
        {
            field = value;
            HasConfigurable = true;
        }
    } = true;

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
        [MethodImpl(JsEngineConstants.Inlining)]
        get => HasGet || HasSet;
    }

    public bool IsDataDescriptor
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => HasValue || HasWritable;
    }

    public bool IsGenericDescriptor
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => !IsAccessorDescriptor && !IsDataDescriptor;
    }

    public bool IsEmpty
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        get => !HasValue && !HasWritable && !HasEnumerable && !HasConfigurable && !HasGet && !HasSet;
    }

    public PropertyDescriptor Clone()
    {
        var clone = new PropertyDescriptor();
        if (HasValue)
        {
            clone.JsValue = JsValue;
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
        _jsValue = default;
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
