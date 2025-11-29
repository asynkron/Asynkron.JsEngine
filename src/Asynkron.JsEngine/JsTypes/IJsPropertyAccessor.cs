namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Interface for JavaScript objects that support property access.
///     This interface allows the evaluator to access properties uniformly across different Js* types.
/// </summary>
public interface IJsPropertyAccessor
{
    /// <summary>
    ///     Tries to get the value of a property by name.
    /// </summary>
    /// <param name="name">The name of the property to get.</param>
    /// <param name="value">When this method returns, contains the property value if found; otherwise, null.</param>
    /// <returns>true if the property was found; otherwise, false.</returns>
    bool TryGetProperty(string name, out object? value);

    /// <summary>
    ///     Tries to get the value of a property by name, passing through the original receiver.
    ///     The default implementation falls back to the basic overload for implementers that
    ///     don't care about the receiver.
    /// </summary>
    /// <param name="name">The name of the property to get.</param>
    /// <param name="receiver">The receiver to use for accessors and prototype lookups.</param>
    /// <param name="value">When this method returns, contains the property value if found; otherwise, null.</param>
    /// <returns>true if the property was found; otherwise, false.</returns>
    bool TryGetProperty(string name, object? receiver, out object? value)
    {
        return TryGetProperty(name, out value);
    }

    /// <summary>
    ///     Sets the value of a property by name.
    /// </summary>
    /// <param name="name">The name of the property to set.</param>
    /// <param name="value">The value to set for the property.</param>
    void SetProperty(string name, object? value);

    /// <summary>
    ///     Sets the value of a property by name using the provided receiver for accessors.
    ///     The default implementation falls back to <see cref="SetProperty(string,object?)" />.
    /// </summary>
    /// <param name="name">The name of the property to set.</param>
    /// <param name="value">The value to set for the property.</param>
    /// <param name="receiver">The receiver to bind when invoking setters.</param>
    void SetProperty(string name, object? value, object? receiver)
    {
        SetProperty(name, value);
    }


    /// <summary>
    ///     Optional hook to provide property descriptors to APIs like
    ///     Object.getOwnPropertyDescriptor without exposing JsObject directly.
    /// </summary>
    PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return null;
    }

    IEnumerable<string> GetOwnPropertyNames()
    {
        return [];
    }

    IEnumerable<string> GetEnumerablePropertyNames()
    {
        return [];
    }
}

/// <summary>
///     Extended object-like interface for types that expose prototype and
///     descriptor operations.
/// </summary>
public interface IJsObjectLike : IJsPropertyAccessor
{
    JsObject? Prototype { get; }
    bool IsSealed { get; }
    IEnumerable<string> Keys { get; }

    void DefineProperty(string name, PropertyDescriptor descriptor);
    void SetPrototype(object? candidate);
    void Seal();
    bool Delete(string name);
}

public interface IPropertyDefinitionHost
{
    bool TryDefineProperty(string name, PropertyDescriptor descriptor);
}

public interface IExtensibilityControl
{
    bool IsExtensible { get; }
    void PreventExtensions();
}
