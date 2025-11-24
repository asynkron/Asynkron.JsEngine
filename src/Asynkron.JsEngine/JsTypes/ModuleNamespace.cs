using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

internal sealed class ModuleNamespace : IJsObjectLike
{
    private readonly RealmState _realmState;
    private readonly ImmutableArray<string> _exportNames;
    private readonly Func<string, object?> _bindingLookup;
    private readonly string _toStringTagKey =
        $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";

    internal ModuleNamespace(
        IEnumerable<string> exportNames,
        Func<string, object?> bindingLookup,
        RealmState realmState)
    {
        _realmState = realmState ?? throw new ArgumentNullException(nameof(realmState));
        _bindingLookup = bindingLookup ?? throw new ArgumentNullException(nameof(bindingLookup));
        _exportNames = exportNames?.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray()
                       ?? throw new ArgumentNullException(nameof(exportNames));
    }

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
            value = _bindingLookup(name);
            return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (string.Equals(name, _toStringTagKey, StringComparison.Ordinal))
        {
            return new PropertyDescriptor
            {
                Value = "Module",
                Writable = false,
                Enumerable = false,
                Configurable = false
            };
        }

        if (_exportNames.Contains(name, StringComparer.Ordinal))
        {
            return new PropertyDescriptor
            {
                Value = _bindingLookup(name),
                Writable = false,
                Enumerable = true,
                Configurable = false
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
        throw StandardLibrary.ThrowTypeError("Module namespace objects are immutable", realm: _realmState);
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

    internal bool Delete(string name)
    {
        if (_exportNames.Contains(name, StringComparer.Ordinal) ||
            string.Equals(name, _toStringTagKey, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
