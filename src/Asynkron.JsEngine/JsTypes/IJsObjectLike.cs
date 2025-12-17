namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Extended object-like interface for types that expose prototype and
///     descriptor operations.
/// </summary>
public interface IJsObjectLike : IJsPropertyAccessor
{
    JsObject? Prototype { get; }
    bool IsSealed { get; }
    bool IsFrozen { get; }
    IEnumerable<string> Keys { get; }

    void DefineProperty(string name, PropertyDescriptor descriptor);
    void SetPrototype(object? candidate);
    void Seal();
    bool Delete(string name);
}
