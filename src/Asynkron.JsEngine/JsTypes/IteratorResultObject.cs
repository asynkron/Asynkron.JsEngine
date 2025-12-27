using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A lightweight iterator result object with direct "value" and "done" fields.
///     Avoids the overhead of JsObject's dictionary-based property storage for the
///     common case of iterator results which are typically only read, not modified.
/// </summary>
/// <remarks>
/// Implements <see cref="IJsSurfacedMutable"/> because JS code can hold references
/// to iterator results (e.g., <c>let result = iterator.next(); saved = result;</c>).
/// This means instances CANNOT be pooled.
/// </remarks>
internal sealed class IteratorResultObject : IJsObjectLike, IAsJsValue, IJsSurfacedMutable
{
    private static readonly string[] PropertyNames = ["value", "done"];

    /// <summary>
    /// Cached "done" result with value=undefined. Since this is immutable (done=true means
    /// the iterator is exhausted), it's safe to share this singleton.
    /// </summary>
    public static readonly IteratorResultObject DoneUndefined = new(JsValue.Undefined, true);

    private JsValue _value;
    private bool _done;
    private JsValue _cachedJsValue;

    public IteratorResultObject(JsValue value, bool done)
    {
        _value = value;
        _done = done;
    }

    /// <summary>
    /// Creates an iterator result. For done=true with undefined value, returns a cached singleton.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsValue Create(JsValue value, bool done)
    {
        if (done && value.IsUndefined)
        {
            return DoneUndefined.AsJsValue;
        }
        return new IteratorResultObject(value, done).AsJsValue;
    }

    public ref readonly JsValue AsJsValue
    {
        get
        {
            // Initialize the cached value's ObjectValue on first access
            // The cached value persists across pool rentals since 'this' doesn't change
            if (_cachedJsValue.ObjectValue is null)
            {
                _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);
            }
            return ref _cachedJsValue;
        }
    }
    public JsObject? Prototype => null;
    public bool IsSealed => false;
    public bool IsFrozen => false;
    public IEnumerable<string> Keys => PropertyNames;

    public bool TryGetProperty(string name, out JsValue value)
    {
        switch (name)
        {
            case "value":
                value = _value;
                return true;
            case "done":
                value = _done ? JsValue.True : JsValue.False;
                return true;
            default:
                value = JsValue.Undefined;
                return false;
        }
    }

    public void SetProperty(string name, JsValue value)
    {
        switch (name)
        {
            case "value":
                _value = value;
                break;
            case "done":
                _done = value.IsTruthy;
                break;
        }
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return name switch
        {
            "value" => new PropertyDescriptor
            {
                JsValue = _value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            },
            "done" => new PropertyDescriptor
            {
                JsValue = _done ? JsValue.True : JsValue.False,
                Writable = true,
                Enumerable = true,
                Configurable = true
            },
            _ => null
        };
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return PropertyNames;
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return PropertyNames;
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        // Iterator results don't typically support defineProperty, but handle value/done
        if (descriptor.HasValue)
        {
            SetProperty(name, descriptor.JsValue);
        }
    }

    public void SetPrototype(IJsPropertyAccessor? candidate)
    {
        // Iterator results don't support prototype modification
    }

    public void Seal()
    {
        // No-op for this lightweight object
    }

    public bool Delete(string name)
    {
        // Don't allow deletion of value/done properties
        return false;
    }
}
