using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class AsyncGeneratorFactory : IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
        IExtensibilityControl,
        IFunctionNameTarget, ICallableMetadata
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _isLexicallyStrict;
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private bool _isConstructorEnabled;
        private ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private PrivateNameScope? _privateNameScope;
        private IJsObjectLike? _homeObject;

        public AsyncGeneratorFactory(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool isConstructorFunction = true)
        {
            if (!function.IsGenerator || !function.IsAsync)
            {
                throw new ArgumentException("Factory can only wrap async generator functions.", nameof(function));
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            _isLexicallyStrict = isLexicallyStrict;
            _isConstructorEnabled = isConstructorFunction;
            InitializeProperties();
        }

        public bool IsExtensible => _properties.IsExtensible;

        public void PreventExtensions()
        {
            _properties.PreventExtensions();
        }

        public void EnsureHasName(string name, bool overwriteExisting = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!overwriteExisting && _function.Name is not null)
            {
                return;
            }

            var descriptor = _properties.GetOwnPropertyDescriptor("name");
            if (descriptor is { Configurable: false })
            {
                return;
            }

            if (!overwriteExisting && descriptor is not null)
            {
                if (descriptor.IsAccessorDescriptor || descriptor.Value is IJsCallable)
                {
                    return;
                }

                if (descriptor.Value is string { Length: > 0 })
                {
                    return;
                }
            }

            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = name,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var instance = new AsyncGeneratorInstance(
                _function,
                _closure,
                arguments,
                thisValue,
                this,
                _realmState,
                _isLexicallyStrict,
                _homeObject,
                _privateNameScope,
                _capturedPrivateNameScopes);
            instance.Initialize();
            return instance.CreateAsyncIteratorObject();
        }

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsFrozen => _properties.IsFrozen;

        public bool IsArrowFunction => false;
        public bool DisallowConstruct => true;
        public RealmState RealmState => _realmState;

        public IEnumerable<string> Keys => _properties.Keys;

        public void DefineProperty(string name, PropertyDescriptor descriptor)
        {
            _properties.DefineProperty(name, descriptor);
        }

        public void SetPrototype(object? candidate)
        {
            _properties.SetPrototype(candidate);
        }

        public void Seal()
        {
            _properties.Seal();
        }

        public PrivateNameScope? PrivateNameScope => _privateNameScope;

        public void SetPrivateNameScope(PrivateNameScope? scope)
        {
            _privateNameScope = scope;
        }

        public void SetCapturedPrivateNameScopes(ImmutableArray<PrivateNameScope> scopes)
        {
            _capturedPrivateNameScopes = scopes;
        }

        public void DisableConstruction()
        {
            if (!_isConstructorEnabled)
            {
                return;
            }

            _isConstructorEnabled = false;
            _properties.DeleteOwnProperty("prototype");
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
        }

        public bool Delete(string name)
        {
            return _properties.DeleteOwnProperty(name);
        }

        public bool TryGetProperty(string name, object? receiver, out object? value)
        {
            // Handle call/apply/bind specially BEFORE looking them up in prototype chain
            // This ensures async generator functions get proper constructor semantics for bound functions
            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.SliceFrom(1);
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        IReadOnlyList<object?> argList = args.Count > 1 && args[1] is JsArray jsArray
                            ? jsArray.Items
                            : ArgumentSlice.Empty;
                        return callable.Invoke(argList, thisArg);
                    });
                    return true;

                case "bind":
                    value = new HostFunction((_, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);

                        // Async generator functions are never constructors, so bound async generator functions
                        // must also have DisallowConstruct = true per ES spec.
                        return new HostFunction((_, innerArgs) =>
                        {
                            if (boundArgs.Count == 0)
                                return callable.Invoke(innerArgs, boundThis);
                            if (innerArgs.Count == 0)
                                return callable.Invoke(boundArgs, boundThis);

                            var finalArgs = new object?[boundArgs.Count + innerArgs.Count];
                            for (var i = 0; i < boundArgs.Count; i++)
                                finalArgs[i] = boundArgs[i];
                            for (var i = 0; i < innerArgs.Count; i++)
                                finalArgs[boundArgs.Count + i] = innerArgs[i];

                            return callable.Invoke(finalArgs, boundThis);
                        }, _realmState, isConstructor: false) { DisallowConstruct = true };
                    });
                    return true;
            }

            // Fall back to properties lookup for all other properties
            if (_properties.TryGetProperty(name, receiver ?? this, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetProperty(string name, out object? value)
        {
            return TryGetProperty(name, this, out value);
        }

        public void SetProperty(string name, object? value)
        {
            SetProperty(name, value, this);
        }

        public void SetProperty(string name, object? value, object? receiver)
        {
            _properties.SetProperty(name, value, receiver ?? this);
        }

        PropertyDescriptor? IJsPropertyAccessor.GetOwnPropertyDescriptor(string name)
        {
            var descriptor = _properties.GetOwnPropertyDescriptor(name);
            if (descriptor is not null && string.Equals(name, "name", StringComparison.Ordinal))
            {
                descriptor.Writable = false;
                descriptor.Enumerable = false;
                descriptor.Configurable = true;
            }

            return descriptor;
        }

        public IEnumerable<string> GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public IEnumerable<string> GetEnumerablePropertyNames()
        {
            return _properties.GetEnumerablePropertyNames();
        }

        public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
        {
            return _properties.TryDefineProperty(name, descriptor);
        }

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[AsyncGeneratorFunction: {name.Name}]"
                : "[AsyncGeneratorFunction]";
        }

        private void InitializeProperties()
        {
            if (_realmState.FunctionPrototype is { } functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            if (_isConstructorEnabled && _realmState.ObjectPrototype is not null)
            {
                var generatorPrototype = new JsObject();
                generatorPrototype.SetPrototype(_realmState.ObjectPrototype);
                generatorPrototype.DefineProperty("constructor",
                    new PropertyDescriptor
                    {
                        Value = this,
                        Writable = true,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });
                _properties.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = generatorPrototype,
                        Writable = true,
                        Enumerable = false,
                        Configurable = false,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });
            }

            var paramCount = GetExpectedParameterCount(_function.Parameters);
            _properties.DefineProperty("length",
                new PropertyDescriptor
                {
                    Value = (double)paramCount,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });

            var functionNameValue = _function.Name?.Name ?? string.Empty;
            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = functionNameValue,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }

        public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
        {
            var descriptor = _properties.GetOwnPropertyDescriptor(name);
            if (descriptor is not null && string.Equals(name, "name", StringComparison.Ordinal))
            {
                descriptor.Writable = false;
                descriptor.Enumerable = false;
                descriptor.Configurable = true;
            }

            return descriptor;
        }
    }
}
