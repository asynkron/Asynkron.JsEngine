using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

internal sealed class ModuleNamespace : IJsObjectLike
{
    private readonly Func<string, object?> _bindingLookup;
    private readonly ImmutableArray<string> _exportNames;
    private readonly RealmState _realmState;

    private readonly string _toStringTagKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";

    private readonly TypedAstSymbol _toStringTagSymbol = TypedAstSymbol.For("Symbol.toStringTag");
    private readonly object _uninitializedMarker;

    internal ModuleNamespace(
        IEnumerable<string> exportNames,
        Func<string, object?> bindingLookup,
        RealmState realmState,
        object uninitializedMarker)
    {
        _realmState = realmState ?? throw new ArgumentNullException(nameof(realmState));
        _bindingLookup = bindingLookup ?? throw new ArgumentNullException(nameof(bindingLookup));
        _exportNames = exportNames?.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray()
                       ?? throw new ArgumentNullException(nameof(exportNames));
        _uninitializedMarker = uninitializedMarker ?? throw new ArgumentNullException(nameof(uninitializedMarker));
    }

    internal ImmutableArray<string> ExportNames => _exportNames;

    public JsObject? Prototype => null;
    public bool IsSealed => true;

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var name in _exportNames)
            {
                yield return name;
            }

            yield return _toStringTagKey;
        }
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (string.Equals(name, _toStringTagKey, StringComparison.Ordinal))
        {
            value = "Module";
            return true;
        }

        if (_exportNames.Contains(name, StringComparer.Ordinal))
        {
            var lookedUp = _bindingLookup(name);
            EnsureInitialized(name, lookedUp);
            value = lookedUp;
            return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (string.Equals(name, _toStringTagKey, StringComparison.Ordinal))
        {
            return new PropertyDescriptor
            {
                Value = "Module", Writable = false, Enumerable = false, Configurable = false
            };
        }

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
        return _exportNames;
    }

    public IEnumerable<string> GetEnumerablePropertyNames()
    {
        return _exportNames;
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        if (string.Equals(name, _toStringTagKey, StringComparison.Ordinal))
        {
            if (descriptor.IsAccessorDescriptor)
            {
                throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
            }

            var tagValue = descriptor.HasValue ? descriptor.Value : "Module";
            var tagWritable = descriptor.HasWritable ? descriptor.Writable : false;
            var tagEnumerable = descriptor.HasEnumerable ? descriptor.Enumerable : false;
            var tagConfigurable = descriptor.HasConfigurable ? descriptor.Configurable : false;

            if (!Equals(tagValue, "Module") || tagWritable || tagEnumerable || tagConfigurable)
            {
                throw StandardLibrary.ThrowTypeError("Invalid @@toStringTag for module namespace", realm: _realmState);
            }

            return;
        }

        if (!_exportNames.Contains(name, StringComparer.Ordinal))
        {
            throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
        }

        if (descriptor.IsAccessorDescriptor)
        {
            throw StandardLibrary.ThrowTypeError("Module namespace exports are immutable", realm: _realmState);
        }

        var value = _bindingLookup(name);
        EnsureInitialized(name, value);

        const bool currentWritable = true;
        const bool currentEnumerable = true;
        const bool currentConfigurable = false;

        var writable = descriptor.HasWritable ? descriptor.Writable : currentWritable;
        var enumerable = descriptor.HasEnumerable ? descriptor.Enumerable : currentEnumerable;
        var configurable = descriptor.HasConfigurable ? descriptor.Configurable : currentConfigurable;
        var valueChange = descriptor.HasValue && !JsOps.StrictEquals(descriptor.Value, value);

        if (writable != currentWritable || enumerable != currentEnumerable || configurable != currentConfigurable ||
            valueChange)
        {
            throw StandardLibrary.ThrowTypeError("Cannot redefine module namespace export", realm: _realmState);
        }
    }

    public void SetPrototype(object? candidate)
    {
        if (candidate is null)
        {
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
        return !_exportNames.Contains(name, StringComparer.Ordinal) &&
               !string.Equals(name, _toStringTagKey, StringComparison.Ordinal);
    }

    internal bool HasExport(string name)
    {
        return _exportNames.Contains(name, StringComparer.Ordinal) ||
               string.Equals(name, _toStringTagKey, StringComparison.Ordinal);
    }

    internal IEnumerable<object?> OwnKeys()
    {
        foreach (var name in _exportNames)
        {
            yield return name;
        }

        yield return _toStringTagSymbol;
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
