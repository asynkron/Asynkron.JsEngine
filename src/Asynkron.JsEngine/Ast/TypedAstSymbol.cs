using System.Collections.Concurrent;
using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents a JavaScript Symbol value (ES6 symbol primitive type).
///     Symbols are unique and immutable primitive values that can be used as object property keys.
///     This is distinct from the internal Symbol class used for S-expression atoms.
/// </summary>
public sealed class TypedAstSymbol : IJsPropertyAccessor
{
    private static readonly ConcurrentDictionary<string, TypedAstSymbol> GlobalRegistry = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int, TypedAstSymbol> IdRegistry = new();
    private static int NextId;
    private static readonly HostFunction SymbolToStringFunction = new((thisValue, _) =>
    {
        if (thisValue is TypedAstSymbol typed)
        {
            return typed.ToString();
        }

        return "Symbol()";
    })
    {
        IsConstructor = false
    };

    private readonly int _id;
    private readonly string? _key; // null for non-global symbols, non-null for global symbols

    private TypedAstSymbol(string? description, string? key, int id)
    {
        Description = description;
        _key = key;
        _id = id;
        IdRegistry[_id] = this;
    }

    /// <summary>
    ///     Gets the optional description of this symbol.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    ///     Creates a new unique symbol with an optional description.
    /// </summary>
    public static TypedAstSymbol Create(string? description = null)
    {
        return new TypedAstSymbol(description, null, Interlocked.Increment(ref NextId));
    }

    /// <summary>
    ///     Gets or creates a global symbol for the given key.
    ///     Global symbols with the same key are the same object.
    /// </summary>
    public static TypedAstSymbol For(string key)
    {
        return GlobalRegistry.GetOrAdd(key, k => new TypedAstSymbol(k, k, Interlocked.Increment(ref NextId)));
    }

    /// <summary>
    ///     Gets the key for a global symbol, or null if the symbol is not global.
    /// </summary>
    public static string? KeyFor(TypedAstSymbol symbol)
    {
        return symbol._key;
    }

    public override string ToString()
    {
        return Description == null ? "Symbol()" : $"Symbol({Description})";
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

    internal static bool TryGetByInternalKey(string propertyName, out TypedAstSymbol symbol)
    {
        symbol = null!;
        if (!propertyName.StartsWith("@@symbol:", StringComparison.Ordinal))
        {
            return false;
        }

        var span = propertyName.AsSpan(9);
        if (!int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        return IdRegistry.TryGetValue(id, out symbol);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (string.Equals(name, "toString", StringComparison.Ordinal))
        {
            value = SymbolToStringFunction;
            return true;
        }

        if (string.Equals(name, "valueOf", StringComparison.Ordinal))
        {
            value = new HostFunction((thisValue, _) => Unbox(thisValue)) { IsConstructor = false };
            return true;
        }

        var toPrimitiveKey = $"@@symbol:{For("Symbol.toPrimitive").GetHashCode()}";
        if (string.Equals(name, toPrimitiveKey, StringComparison.Ordinal))
        {
            value = new HostFunction((thisValue, _) => Unbox(thisValue)) { IsConstructor = false };
            return true;
        }

        var toStringTagKey = $"@@symbol:{For("Symbol.toStringTag").GetHashCode()}";
        if (string.Equals(name, toStringTagKey, StringComparison.Ordinal))
        {
            value = "Symbol";
            return true;
        }

        value = null;
        return false;

        TypedAstSymbol Unbox(object? receiver)
        {
            switch (receiver)
            {
                case TypedAstSymbol sym:
                    return sym;
                case JsObject obj when obj.TryGetProperty("__value__", out var inner) && inner is TypedAstSymbol s:
                    return s;
                default:
                    throw StandardLibrary.ThrowTypeError("Symbol.prototype valueOf called on incompatible receiver");
            }
        }
    }

    public void SetProperty(string name, object? value)
    {
        // Symbols are immutable; ignore assignments.
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return TryGetProperty(name, out var value)
            ? new PropertyDescriptor
            {
                Value = value, Writable = true, Enumerable = false, Configurable = true
            }
            : null;
    }
}
