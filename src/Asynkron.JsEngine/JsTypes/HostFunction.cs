using System;
using System.Collections.Generic;
using System.Linq;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a host function that can be called from JavaScript.
/// </summary>
public sealed class HostFunction : IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
    IPrototypeAccessorProvider,
    IJsEnvironmentAwareCallable
{
    private readonly Func<object?, IReadOnlyList<object?>, object?> _handler;
    private bool _isConstructor = true;
    private RealmState? _realmState;

    public HostFunction(Func<IReadOnlyList<object?>, object?> handler, RealmState? realmState = null,
        bool isConstructor = true)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = (_, args) => handler(args);
        RealmState = realmState;
        _isConstructor = isConstructor;
        InitializePrototype();
    }

    public HostFunction(Func<object?, IReadOnlyList<object?>, object?> handler, RealmState? realmState = null,
        bool isConstructor = true)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        RealmState = realmState;
        _isConstructor = isConstructor;
        InitializePrototype();
    }

    public HostFunction(
        Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler,
        RealmState? realmState,
        bool isConstructor = true)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        RealmState = realmState;
        _isConstructor = isConstructor;
        _handler = (thisValue, args) => handler(thisValue, args, realmState);
        InitializePrototype();
    }

    /// <summary>
    ///     Optional realm/global object that owns this host function. Used for
    ///     realm-aware operations (e.g. Reflect.construct default prototypes).
    /// </summary>
    public JsObject? Realm { get; set; }

    /// <summary>
    ///     Optional realm state for intrinsic prototype resolution.
    /// </summary>
    public RealmState? RealmState
    {
        get => _realmState;
        set
        {
            if (ReferenceEquals(_realmState, value))
            {
                return;
            }

            _realmState = value;
            if (_realmState?.FunctionPrototype is JsObject functionPrototype &&
                Properties.Prototype is null)
            {
                Properties.SetPrototype(functionPrototype);
            }
        }
    }

    /// <summary>
    ///     Indicates whether this host function can be used with <c>new</c>.
    /// </summary>
    public bool IsConstructor
    {
        get => _isConstructor;
        set
        {
            if (_isConstructor == value)
            {
                return;
            }

            _isConstructor = value;
            if (_isConstructor)
            {
                EnsurePrototypeDataProperty();
            }
            else
            {
                RemovePrototypeDataProperty();
            }
        }
    }

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
    public bool IsExtensible => Properties.IsExtensible;

    public void PreventExtensions()
    {
        Properties.PreventExtensions();
    }

    public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
    {
        return _handler(thisValue, arguments);
    }

    /// <summary>
    ///     Captures the environment that invoked this host function so nested
    ///     callbacks can inherit the correct global `this` binding.
    /// </summary>
    public JsEnvironment? CallingJsEnvironment { get; set; }

    public bool TryGetProperty(string name, object? receiver, out object? value)
    {
        if (!_isConstructor && string.Equals(name, "prototype", StringComparison.Ordinal))
        {
            value = Symbol.Undefined;
            return true;
        }

        EnsureFunctionPrototype();
        if (Properties.TryGetProperty(name, receiver ?? this, out value))
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
                    var thisArg = args.GetArgument(0);
                    var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];
                    return jsCallable.Invoke(callArgs, thisArg);
                }, isConstructor: false);
                return true;

            case "apply":
                value = new HostFunction((_, args) =>
                {
                    var thisArg = args.GetArgument(0);
                    var argList = new List<object?>();
                    if (args.Count <= 1 || args[1] is not JsArray jsArray)
                    {
                        return jsCallable.Invoke(argList.ToArray(), thisArg);
                    }

                    argList.AddRange(jsArray.Items);

                    return jsCallable.Invoke(argList.ToArray(), thisArg);
                }, isConstructor: false);
                return true;

            case "bind":
                value = new HostFunction((_, args) =>
                {
                    var boundThis = args.GetArgument(0);
                    var boundArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];

                    var targetIsConstructor = JsOps.IsConstructor(jsCallable);
                    var boundFunction = new HostFunction((_, innerArgs) =>
                    {
                        var finalArgs = new object?[boundArgs.Length + innerArgs.Count];
                        boundArgs.CopyTo(finalArgs, 0);
                        for (var i = 0; i < innerArgs.Count; i++)
                        {
                            finalArgs[boundArgs.Length + i] = innerArgs[i];
                        }

                        return jsCallable.Invoke(finalArgs, boundThis);
                    }, isConstructor: targetIsConstructor);

                    if (!targetIsConstructor)
                    {
                        boundFunction.PropertiesObject.DeleteOwnProperty("prototype");
                    }

                    if (jsCallable is HostFunction hostFunction)
                    {
                        boundFunction.Realm = hostFunction.Realm;
                        boundFunction.RealmState = hostFunction.RealmState ?? boundFunction.RealmState;
                    }
                    else if (jsCallable is ICallableMetadata metadata)
                    {
                        boundFunction.RealmState = metadata.RealmState;
                    }

                    return boundFunction;
                }, isConstructor: false);
                return true;
        }

        value = null;
        return false;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        return TryGetProperty(name, this, out value);
    }

    public void SetProperty(string name, object? value, object? receiver)
    {
        Properties.SetProperty(name, value, receiver ?? this);
    }

    public void SetProperty(string name, object? value)
    {
        SetProperty(name, value, this);
    }

    public void DefineProperty(string name, PropertyDescriptor descriptor)
    {
        Properties.DefineProperty(name, descriptor);
    }

    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (!_isConstructor && string.Equals(name, "prototype", StringComparison.Ordinal))
        {
            return null;
        }

        return Properties.GetOwnPropertyDescriptor(name);
    }

    public IEnumerable<string> GetOwnPropertyNames()
    {
        foreach (var key in Properties.GetOwnPropertyNames())
        {
            if (!_isConstructor && string.Equals(key, "prototype", StringComparison.Ordinal))
            {
                continue;
            }

            yield return key;
        }
    }

    public JsObject? Prototype
    {
        get
        {
            EnsureFunctionPrototype();
            return Properties.Prototype;
        }
    }

    public IJsPropertyAccessor? PrototypeAccessor =>
        Properties is IPrototypeAccessorProvider provider ? provider.PrototypeAccessor : null;

    public bool IsSealed => Properties.IsSealed;

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var key in Properties.Keys)
            {
                if (!_isConstructor && string.Equals(key, "prototype", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return key;
            }
        }
    }

    public void SetPrototype(object? candidate)
    {
        Properties.SetPrototype(candidate);
    }

    public void Seal()
    {
        Properties.Seal();
    }

    public bool Delete(string name)
    {
        return DeleteProperty(name);
    }

    public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
    {
        return Properties.TryDefineProperty(name, descriptor);
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

    private void InitializePrototype()
    {
        EnsureFunctionPrototype();
        if (_isConstructor)
        {
            EnsurePrototypeDataProperty();
        }
        else
        {
            RemovePrototypeDataProperty();
        }
    }

    private void EnsurePrototypeDataProperty()
    {
        if (!_isConstructor || HasPrototypeDataProperty())
        {
            return;
        }

        var prototype = new JsObject();
        prototype.SetProperty("constructor", this);
        Properties.SetProperty("prototype", prototype);
    }

    private void RemovePrototypeDataProperty()
    {
        Properties.DeleteOwnProperty("prototype");
    }

    private bool HasPrototypeDataProperty()
    {
        return Properties.GetOwnPropertyDescriptor("prototype") is not null
               || Properties.ContainsKey("prototype");
    }
}
