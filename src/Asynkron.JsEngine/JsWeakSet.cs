using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript WeakSet collection.
/// WeakSets store unique objects where values are held weakly.
/// Unlike Set, WeakSet does not prevent garbage collection of values and does not support iteration.
/// </summary>
internal sealed class JsWeakSet
{
    // Use ConditionalWeakTable to track object membership
    // We use a dummy value since we only care about key presence
    private readonly ConditionalWeakTable<object, object?> _values = new();
    private readonly JsObject _properties = new();

    public bool TryGetProperty(string name, out object? value)
    {
        return _properties.TryGetProperty(name, out value);
    }

    public void SetProperty(string name, object? value)
    {
        _properties.SetProperty(name, value);
    }

    /// <summary>
    /// Adds a value to the WeakSet. Returns the WeakSet object to allow chaining.
    /// The value must be an object (not a primitive value).
    /// </summary>
    public JsWeakSet Add(object? value)
    {
        // WeakSet only accepts objects as values
        if (value == null || !IsObject(value)) throw new Exception("Invalid value used in weak set");

        // If already present, do nothing; otherwise add it
        if (!_values.TryGetValue(value, out _)) _values.Add(value, null);
        return this;
    }

    /// <summary>
    /// Returns true if the value exists in the WeakSet, false otherwise.
    /// </summary>
    public bool Has(object? value)
    {
        if (value == null || !IsObject(value)) return false;

        return _values.TryGetValue(value, out _);
    }

    /// <summary>
    /// Removes the specified value from the WeakSet.
    /// Returns true if the value was in the WeakSet and has been removed, false otherwise.
    /// </summary>
    public bool Delete(object? value)
    {
        if (value == null || !IsObject(value)) return false;

        return _values.Remove(value);
    }

    /// <summary>
    /// Checks if a value is considered an object for WeakSet purposes.
    /// In JavaScript, objects, arrays, functions, etc. are valid, but primitives are not.
    /// </summary>
    private static bool IsObject(object? value)
    {
        if (value == null) return false;

        // Check for undefined symbol
        if (value is Symbol sym && ReferenceEquals(sym, JsSymbols.Undefined)) return false;

        // Check if it's a reference type that can be used as a WeakSet value
        // Strings are reference types in .NET but are treated as primitives in JavaScript
        if (value is string) return false;

        // Value types (numbers, bools, etc.) are not valid WeakSet values
        if (value.GetType().IsValueType) return false;

        // Symbol is a special case - not allowed as WeakSet value
        if (value is JsSymbol) return false;

        return true;
    }
}