using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Represents a host function that can be called from JavaScript.
/// </summary>
public sealed class HostFunction : IJsCallable, IJsObjectLike, IJsEnvironmentAwareCallable
{
    private readonly Func<object?, IReadOnlyList<object?>, object?> _handler;
    private readonly JsObject _properties = new();

    /// <summary>
    /// Optional realm/global object that owns this host function. Used for
    /// realm-aware operations (e.g. Reflect.construct default prototypes).
    /// </summary>
    public JsObject? Realm { get; set; }

    /// <summary>
    /// Optional realm state for intrinsic prototype resolution.
    /// </summary>
    public Runtime.RealmState? RealmState { get; set; }

    /// <summary>
    /// Indicates whether this host function can be used with <c>new</c>.
    /// </summary>
    public bool IsConstructor { get; set; } = true;

    /// <summary>
    /// Captures the environment that invoked this host function so nested
    /// callbacks can inherit the correct global `this` binding.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    internal JsObject Properties => _properties;

    public HostFunction(Func<IReadOnlyList<object?>, object?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = (_, args) => handler(args);
        _properties.SetProperty("prototype", new JsObject());
        EnsureFunctionPrototype();
    }

    public HostFunction(Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _properties.SetProperty("prototype", new JsObject());
        EnsureFunctionPrototype();
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        return _handler(thisValue, arguments);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        EnsureFunctionPrototype();
        if (_properties.TryGetProperty(name, out value))
        {
            return true;
        }

        // Provide minimal Function.prototype-style helpers for host functions:
        // fn.call(thisArg, ...args), fn.apply(thisArg, argsArray), fn.bind(thisArg, ...boundArgs)
        var callable = (IJsCallable)this;
        switch (name)
        {
            case "call":
                value = new HostFunction((_, args) =>
                {
                    var thisArg = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : Array.Empty<object?>();
                    return callable.Invoke(callArgs, thisArg);
                });
                return true;

            case "apply":
                value = new HostFunction((_, args) =>
                {
                    var thisArg = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var argList = new List<object?>();
                    if (args.Count > 1 && args[1] is JsArray jsArray)
                    {
                        foreach (var item in jsArray.Items)
                        {
                            argList.Add(item);
                        }
                    }

                    return callable.Invoke(argList.ToArray(), thisArg);
                });
                return true;

            case "bind":
                value = new HostFunction((_, args) =>
                {
                    var boundThis = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var boundArgs = args.Count > 1 ? args.Skip(1).ToArray() : Array.Empty<object?>();

                    return new HostFunction((innerThis, innerArgs) =>
                    {
                        var finalArgs = new object?[boundArgs.Length + innerArgs.Count];
                        boundArgs.CopyTo(finalArgs, 0);
                        for (var i = 0; i < innerArgs.Count; i++)
                        {
                            finalArgs[boundArgs.Length + i] = innerArgs[i];
                        }

                        return callable.Invoke(finalArgs, boundThis);
                    });
                });
                return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        _properties.SetProperty(name, value);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        _properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name) => _properties.GetOwnPropertyDescriptor(name);

    public IEnumerable<string> GetOwnPropertyNames() => _properties.GetOwnPropertyNames();

    public JsObject? Prototype
    {
        get
        {
            EnsureFunctionPrototype();
            return _properties.Prototype;
        }
    }

    public bool IsSealed => _properties.IsSealed;

    public IEnumerable<string> Keys => _properties.Keys;

    public void SetPrototype(object? candidate)
    {
        _properties.SetPrototype(candidate);
    }

    public bool DeleteProperty(string name)
    {
        return _properties.DeleteOwnProperty(name);
    }

    public void Seal()
    {
        _properties.Seal();
    }

    private void EnsureFunctionPrototype()
    {
        if (_properties.Prototype is null && StandardLibrary.FunctionPrototype is not null)
        {
            _properties.SetPrototype(StandardLibrary.FunctionPrototype);
        }
    }
}
