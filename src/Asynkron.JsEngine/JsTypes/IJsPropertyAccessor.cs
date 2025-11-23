using System.Collections.Generic;
using Asynkron.JsEngine.Runtime;

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
    ///     Sets the value of a property by name.
    /// </summary>
    /// <param name="name">The name of the property to set.</param>
    /// <param name="value">The value to set for the property.</param>
    void SetProperty(string name, object? value);



    /// <summary>
    ///     Optional hook to provide property descriptors to APIs like
    ///     Object.getOwnPropertyDescriptor without exposing JsObject directly.
    /// </summary>
    PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return null;
    }

    /// <summary>
    ///     Optional hook to enumerate own property names for accessor types that
    ///     wrap a JsObject (e.g., HostFunction).
    /// </summary>
    IEnumerable<string> GetOwnPropertyNames()
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
}
