using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a host function that can be called from JavaScript.
/// </summary>
public sealed class HostFunction : IJsObjectLike, IJsEnvironmentAwareCallable
{
    private readonly Func<object?, IReadOnlyList<object?>, object?> _handler;

    public HostFunction(Func<IReadOnlyList<object?>, object?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = (_, args) => handler(args);
        Properties.SetProperty("prototype", new JsObject());
        EnsureFunctionPrototype();
    }

    public HostFunction(Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Properties.SetProperty("prototype", new JsObject());
        EnsureFunctionPrototype();
    }

    /// <summary>
    ///     Optional realm/global object that owns this host function. Used for
    ///     realm-aware operations (e.g. Reflect.construct default prototypes).
    /// </summary>
    public JsObject? Realm { get; set; }

    /// <summary>
    ///     Optional realm state for intrinsic prototype resolution.
    /// </summary>
    public RealmState? RealmState { get; set; }

    /// <summary>
    ///     Indicates whether this host function can be used with <c>new</c>.
    /// </summary>
    public bool IsConstructor { get; set; } = true;

    /// <summary>
    ///     When true, construction is explicitly disallowed even though the function
    ///     reports itself as a constructor (e.g. BigInt).
    /// </summary>
    public bool DisallowConstruct { get; set; }

    /// <summary>
    ///     Optional error message used when construction is disallowed.
    /// </summary>
    public string? ConstructErrorMessage { get; set; }

    internal JsObject Properties { get; } = new();

    internal JsObject PropertiesObject => Properties;

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        return _handler(thisValue, arguments);
    }

    /// <summary>
    ///     Captures the environment that invoked this host function so nested
    ///     callbacks can inherit the correct global `this` binding.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public bool TryGetProperty(string name, out object? value)
    {
        EnsureFunctionPrototype();
        if (Properties.TryGetProperty(name, out value))
        {
            return true;
        }

        // Provide minimal Function.prototype-style helpers for host functions:
        // fn.call(thisArg, ...args), fn.apply(thisArg, argsArray), fn.bind(thisArg, ...boundArgs)
        IJsCallable jsCallable = this;
        switch (name)
        {
            case "call":
                value = new HostFunction((_, args) =>
                {
                    var thisArg = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];
                    return jsCallable.Invoke(callArgs, thisArg);
                });
                return true;

            case "apply":
                value = new HostFunction((_, args) =>
                {
                    var thisArg = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var argList = new List<object?>();
                    if (args.Count <= 1 || args[1] is not JsArray jsArray)
                    {
                        return jsCallable.Invoke(argList.ToArray(), thisArg);
                    }

                    argList.AddRange(jsArray.Items);

                    return jsCallable.Invoke(argList.ToArray(), thisArg);
                });
                return true;

            case "bind":
                value = new HostFunction((_, args) =>
                {
                    var boundThis = args.Count > 0 ? args[0] : Symbols.Undefined;
                    var boundArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];

                    return new HostFunction((_, innerArgs) =>
                    {
                        var finalArgs = new object?[boundArgs.Length + innerArgs.Count];
                        boundArgs.CopyTo(finalArgs, 0);
                        for (var i = 0; i < innerArgs.Count; i++)
                        {
                            finalArgs[boundArgs.Length + i] = innerArgs[i];
                        }

                        return jsCallable.Invoke(finalArgs, boundThis);
                    });
                });
                return true;
        }

        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        Properties.SetProperty(name, value);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        Properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        return Properties.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        return Properties.GetOwnPropertyNames();
    }

    public JsObject? Prototype
    {
        get
        {
            EnsureFunctionPrototype();
            return Properties.Prototype;
        }
    }

    public bool IsSealed => Properties.IsSealed;

    public IEnumerable<string> Keys => Properties.Keys;

    public void SetPrototype(object? candidate)
    {
        Properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        Properties.Seal();
    }

    public bool DeleteProperty(string name)
    {
        return Properties.DeleteOwnProperty(name);
    }

    private void EnsureFunctionPrototype()
    {
        if (Properties.Prototype is not null)
        {
            return;
        }

        if (RealmState?.FunctionPrototype is JsObject functionPrototype)
        {
            Properties.SetPrototype(functionPrototype);
        }
    }
}
