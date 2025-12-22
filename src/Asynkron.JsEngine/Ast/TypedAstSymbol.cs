#region

using System.Collections.Concurrent;
using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

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
    private static readonly ConcurrentDictionary<int, string> PropertyKeyCache = new();
    private static int NextId;

    private static readonly HostFunction SymbolToStringFunction = new((thisValue, _) =>
    {
        // Use TryUnwrap instead of TryGetObject because Symbol JsValues have Kind=Symbol, not Object
        if (thisValue.TryUnwrap<TypedAstSymbol>(out var typed))
        {
            return new JsValue(typed.ToString());
        }

        return new JsValue("Symbol()");
    }, isConstructor: false);

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

    public bool TryGetProperty(string name, out JsValue value)
    {
        if (string.Equals(name, "toString", StringComparison.Ordinal))
        {
            value = (JsValue)SymbolToStringFunction;
            return true;
        }

        if (string.Equals(name, "valueOf", StringComparison.Ordinal))
        {
            value = (JsValue)new HostFunction((thisValue, _) => (JsValue)Unbox(thisValue), isConstructor: false);
            return true;
        }

        var toPrimitiveKey = PropertyKey(Symbols.ToPrimitive);
        if (string.Equals(name, toPrimitiveKey, StringComparison.Ordinal))
        {
            value = (JsValue)new HostFunction((thisValue, _) => (JsValue)Unbox(thisValue), isConstructor: false);
            return true;
        }

        var toStringTagKey = PropertyKey(Symbols.ToStringTag);
        if (string.Equals(name, toStringTagKey, StringComparison.Ordinal))
        {
            value = (JsValue)"Symbol";
            return true;
        }

        value = JsValue.Undefined;
        return false;

        TypedAstSymbol Unbox(JsValue receiver)
        {
            // For primitive symbols: Kind=Symbol, ObjectValue=TypedAstSymbol
            if (receiver.IsSymbol && receiver.TryUnwrap<TypedAstSymbol>(out var sym))
            {
                return sym;
            }

            // For boxed symbols: Kind=Object, ObjectValue=JsObject with __value__
            if (receiver.TryGetObject<JsObject>(out var obj) &&
                obj.TryGetProperty("__value__", out var inner) &&
                inner.IsSymbol && inner.TryUnwrap<TypedAstSymbol>(out var innerSym))
            {
                return innerSym;
            }

            throw StandardLibrary.ThrowTypeError("Symbol.prototype valueOf called on incompatible receiver");
        }
    }

    public void SetProperty(string name, JsValue value)
    {
        // Symbols are immutable; ignore assignments.
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return TryGetProperty(name, out var value)
            ? new PropertyDescriptor { JsValue = value, Writable = true, Enumerable = false, Configurable = true }
            : null;
    }

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

    /// <summary>
    ///     Returns a stable property key string for a symbol (e.g. "@@symbol:1234"), cached per process.
    /// </summary>
    public static string PropertyKey(TypedAstSymbol symbol)
    {
        var hash = symbol.GetHashCode();
        return PropertyKeyCache.GetOrAdd(hash, h => $"@@symbol:{h}");
    }

    public static string PropertyKey(string wellKnownName, RealmState? realm = null)
    {
        _ = realm;
        return PropertyKey(For(wellKnownName));
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
}
