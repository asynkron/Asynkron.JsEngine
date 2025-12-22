using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

internal sealed class ModuleNamespace : IJsObjectLike, IPropertyDefinitionHost
{
    private readonly Func<string, object?> _bindingLookup;
    private readonly ImmutableArray<string> _exportNames;
    private readonly RealmState _realmState;

    private static string ToStringTagKey => SymbolKeys.ToStringTag;

    private readonly TypedAstSymbol _toStringTagSymbol = Symbols.ToStringTag;
    private readonly Action? _ensureEvaluated;
    private readonly bool _isDeferred;
    private readonly object _uninitializedMarker;

    internal ModuleNamespace(
        IEnumerable<string> exportNames,
        Func<string, object?> bindingLookup,
        RealmState realmState,
        object uninitializedMarker,
        bool isDeferred,
        Action? ensureEvaluated)
    {
        _realmState = realmState ?? throw new ArgumentNullException(nameof(realmState));
        _bindingLookup = bindingLookup ?? throw new ArgumentNullException(nameof(bindingLookup));
        _exportNames = exportNames?.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray()
                       ?? throw new ArgumentNullException(nameof(exportNames));
        _uninitializedMarker = uninitializedMarker ?? throw new ArgumentNullException(nameof(uninitializedMarker));
        _isDeferred = isDeferred;
        _ensureEvaluated = ensureEvaluated;
    }

    internal ImmutableArray<string> ExportNames => _exportNames;

    public JsObject? Prototype => null;
    public bool IsSealed => true;
    public bool IsFrozen => true;

    public IEnumerable<string> Keys
    {
        get
        {
            EnsureExportsEvaluatedForList();
            foreach (var name in _exportNames)
            {
                yield return name;
            }

            yield return ToStringTagKey;
        }
    }

    public bool TryGetProperty(string name, out JsValue value)
    {
        if (string.Equals(name, ToStringTagKey, StringComparison.Ordinal))
        {
            value = (JsValue)"Module";
            return true;
        }

        if (IsSymbolLikeNamespaceKey(name))
        {
            if (_exportNames.Contains(name, StringComparer.Ordinal))
            {
                value = JsValue.FromObjectUnsafe(_bindingLookup(name));
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        EnsureExportsEvaluated(name);
        if (_exportNames.Contains(name, StringComparer.Ordinal))
        {
            var lookedUp = _bindingLookup(name);
            EnsureInitialized(name, lookedUp);
            value = JsValue.FromObjectUnsafe(lookedUp);
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    public void SetProperty(string name, JsValue value, JsValue receiver)
    {
        // Per ES spec [[Set]] for module namespace: always return false, never triggers evaluation
        // Even for deferred namespaces, [[Set]] does not trigger evaluation
        throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
    }

    public void SetProperty(string name, JsValue value)
    {
        SetProperty(name, value, JsValue.FromObjectUnsafe(this));
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (string.Equals(name, ToStringTagKey, StringComparison.Ordinal))
        {
            return new PropertyDescriptor
            {
                Value = "Module", Writable = false, Enumerable = false, Configurable = false
            };
        }

        if (IsSymbolLikeNamespaceKey(name))
        {
            if (_exportNames.Contains(name, StringComparer.Ordinal))
            {
                var lookedUp = _bindingLookup(name);
                return new PropertyDescriptor
                {
                    Value = lookedUp, Writable = true, Enumerable = true, Configurable = false
                };
            }

            return null;
        }

        EnsureExportsEvaluated(name);
        if (_exportNames.Contains(name, StringComparer.Ordinal))
        {
            var lookedUp = _bindingLookup(name);
            EnsureInitialized(name, lookedUp);
            return new PropertyDescriptor
            {
                Value = lookedUp, Writable = true, Enumerable = true, Configurable = false
            };
        }

        return null;
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        EnsureExportsEvaluatedForList();
        return _exportNames;
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        EnsureExportsEvaluatedForList();
        return _exportNames;
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (!TryDefineProperty(name, descriptor))
        {
            throw StandardLibrary.ThrowTypeError("Cannot define property on module namespace", realm: _realmState);
        }
    }

    /// <summary>
    /// Implements [[DefineOwnProperty]] for module namespace exotic objects.
    /// Returns true if no change is requested, false otherwise.
    /// </summary>
    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        // Check if this is Symbol.toStringTag
        // Use TryGetByInternalKey to get the actual symbol and check its description,
        // since symbol IDs can vary depending on initialization order
        // Match both the realm-specific key (e.g. "@@symbol:5") and a parsed well-known symbol
        var isToStringTag =
            string.Equals(name, ToStringTagKey, StringComparison.Ordinal) ||
            (TypedAstSymbol.TryGetByInternalKey(name, out var symbol) &&
             string.Equals(symbol.Description, "Symbol.toStringTag", StringComparison.Ordinal));

        // Handle Symbol.toStringTag property
        if (isToStringTag)
        {
            // Per ES spec, accessor descriptors are not allowed
            if (descriptor.IsAccessorDescriptor)
            {
                return false;
            }

            if (_isDeferred)
            {
                // For deferred namespaces, any valid data descriptor is accepted
                return true;
            }

            // For non-deferred namespaces, check if the descriptor requests a change
            // Current property: value="Module", writable=false, enumerable=false, configurable=false

            // If value is specified and different from "Module", return false
            if (descriptor.HasValue)
            {
                var jsVal = descriptor.JsValue;
                if (!jsVal.TryGetString(out var valueString) ||
                    !string.Equals(valueString, "Module", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // If writable is specified and true (different from current false), return false
            if (descriptor.HasWritable && descriptor.Writable)
            {
                return false;
            }

            // If enumerable is specified and true (different from current false), return false
            if (descriptor.HasEnumerable && descriptor.Enumerable)
            {
                return false;
            }

            // If configurable is specified and true (different from current false), return false
            if (descriptor.HasConfigurable && descriptor.Configurable)
            {
                return false;
            }

            // No change requested (empty descriptor or values match current)
            return true;
        }

        // Per ES spec, [[DefineOwnProperty]] calls GetModuleExportsList which triggers evaluation
        // This must happen before checking if the key is in the exports list
        if (!IsSymbolLikeNamespaceKey(name))
        {
            EnsureExportsEvaluated(name);
        }

        // Property doesn't exist - return false
        if (!_exportNames.Contains(name, StringComparer.Ordinal))
        {
            return false;
        }

        // Accessor descriptors not allowed
        if (descriptor.IsAccessorDescriptor)
        {
            return false;
        }

        var value = _bindingLookup(name);
        EnsureInitialized(name, value);

        const bool currentWritable = true;
        const bool currentEnumerable = true;
        const bool currentConfigurable = false;

        var writable = descriptor.HasWritable ? descriptor.Writable : currentWritable;
        var enumerable = descriptor.HasEnumerable ? descriptor.Enumerable : currentEnumerable;
        var configurable = descriptor.HasConfigurable ? descriptor.Configurable : currentConfigurable;
        var valueChange = descriptor.HasValue && !JsOps.StrictEquals(descriptor.JsValue, JsValue.FromObjectUnsafe(value));

        // Return true only if no change is requested
        if (writable != currentWritable || enumerable != currentEnumerable || configurable != currentConfigurable ||
            valueChange)
        {
            return false;
        }

        return true;
    }

    public void SetPrototype(object? candidate)
    {
        if (candidate is null)
        {
            if (_isDeferred)
            {
                return;
            }

            return;
        }

        throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
    }

    public void Seal()
    {
        // Module namespace objects are always non-extensible; nothing to do.
    }

    public bool Delete(string name)
    {
        if (!IsSymbolLikeNamespaceKey(name))
        {
            EnsureExportsEvaluated(name);
        }

        return !_exportNames.Contains(name, StringComparer.Ordinal) &&
               !string.Equals(name, ToStringTagKey, StringComparison.Ordinal);
    }

    internal bool HasExport(string name)
    {
        return _exportNames.Contains(name, StringComparer.Ordinal) ||
               string.Equals(name, ToStringTagKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Implements [[HasProperty]] for module namespace.
    /// Per ES spec, [[HasProperty]] for non-symbol-like keys triggers evaluation for deferred namespaces.
    /// </summary>
    internal bool HasProperty(string name)
    {
        // Symbol-like keys don't trigger evaluation
        if (!IsSymbolLikeNamespaceKey(name))
        {
            EnsureExportsEvaluated(name);
        }

        return _exportNames.Contains(name, StringComparer.Ordinal) ||
               string.Equals(name, ToStringTagKey, StringComparison.Ordinal);
    }

    internal IEnumerable<object?> OwnKeys()
    {
        EnsureExportsEvaluatedForList();
        foreach (var name in _exportNames)
        {
            yield return name;
        }

        yield return _toStringTagSymbol;
    }

    private bool IsSymbolLikeNamespaceKey(string name)
    {
        if (name.StartsWith("@@symbol:", StringComparison.Ordinal))
        {
            return true;
        }

        return _isDeferred && string.Equals(name, "then", StringComparison.Ordinal);
    }

    private void EnsureExportsEvaluated(string name)
    {
        if (_isDeferred && IsSymbolLikeNamespaceKey(name))
        {
            return;
        }

        if (_isDeferred)
        {
            _ensureEvaluated?.Invoke();
        }
    }

    private void EnsureExportsEvaluatedForList()
    {
        if (_isDeferred)
        {
            _ensureEvaluated?.Invoke();
        }
    }

    private void EnsureInitialized(string name, object? value)
    {
        if (ReferenceEquals(value, _uninitializedMarker))
        {
            throw StandardLibrary.ThrowReferenceError($"Cannot access '{name}' before initialization",
                realm: _realmState);
        }
    }
}
