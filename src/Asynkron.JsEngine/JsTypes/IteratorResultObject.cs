namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     A lightweight iterator result object with direct "value" and "done" fields.
///     Avoids the overhead of JsObject's dictionary-based property storage for the
///     common case of iterator results which are typically only read, not modified.
/// </summary>
internal sealed class IteratorResultObject : IJsObjectLike
{
    private static readonly string[] PropertyNames = ["value", "done"];

    public JsValue Value;
    public bool Done;

    public IteratorResultObject(JsValue value, bool done)
    {
        Value = value;
        Done = done;
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
                value = Value;
                return true;
            case "done":
                value = Done ? JsValue.True : JsValue.False;
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
                Value = value;
                break;
            case "done":
                Done = value.IsTruthy;
                break;
        }
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return name switch
        {
            "value" => new PropertyDescriptor
            {
                JsValue = Value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            },
            "done" => new PropertyDescriptor
            {
                JsValue = Done ? JsValue.True : JsValue.False,
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

    public void SetPrototype(object? candidate)
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
