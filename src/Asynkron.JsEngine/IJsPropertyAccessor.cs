namespace Asynkron.JsEngine;

/// <summary>
/// Interface for JavaScript objects that support property access.
/// This interface allows the evaluator to access properties uniformly across different Js* types.
/// </summary>
public interface IJsPropertyAccessor
{
    /// <summary>
    /// Tries to get the value of a property by name.
    /// </summary>
    /// <param name="name">The name of the property to get.</param>
    /// <param name="value">When this method returns, contains the property value if found; otherwise, null.</param>
    /// <returns>true if the property was found; otherwise, false.</returns>
    bool TryGetProperty(string name, out object? value);
    
    /// <summary>
    /// Sets the value of a property by name.
    /// </summary>
    /// <param name="name">The name of the property to set.</param>
    /// <param name="value">The value to set for the property.</param>
    void SetProperty(string name, object? value);
}
