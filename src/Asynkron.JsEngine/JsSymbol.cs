using System.Collections.Concurrent;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Symbol value (ES6 symbol primitive type).
/// Symbols are unique and immutable primitive values that can be used as object property keys.
/// This is distinct from the internal Symbol class used for S-expression atoms.
/// </summary>
internal sealed class JsSymbol
{
    private static readonly ConcurrentDictionary<string, JsSymbol> GlobalRegistry = new(StringComparer.Ordinal);
    private static int _nextId = 0;

    private readonly int _id;
    private readonly string? _key; // null for non-global symbols, non-null for global symbols

    private JsSymbol(string? description, string? key, int id)
    {
        Description = description;
        _key = key;
        _id = id;
    }

    /// <summary>
    /// Gets the optional description of this symbol.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Creates a new unique symbol with an optional description.
    /// </summary>
    public static JsSymbol Create(string? description = null)
    {
        return new JsSymbol(description, null, Interlocked.Increment(ref _nextId));
    }

    /// <summary>
    /// Gets or creates a global symbol for the given key.
    /// Global symbols with the same key are the same object.
    /// </summary>
    public static JsSymbol For(string key)
    {
        return GlobalRegistry.GetOrAdd(key, k => new JsSymbol(k, k, Interlocked.Increment(ref _nextId)));
    }

    /// <summary>
    /// Gets the key for a global symbol, or null if the symbol is not global.
    /// </summary>
    public static string? KeyFor(JsSymbol symbol)
    {
        return symbol._key;
    }

    public override string ToString()
    {
        if (Description != null) return $"Symbol({Description})";
        return "Symbol()";
    }

    public override bool Equals(object? obj)
    {
        // Symbols are only equal to themselves (reference equality)
        return ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        return _id;
    }
}