using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedGeneratorFactory : IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
        IExtensibilityControl,
        IFunctionNameTarget
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _isLexicallyStrict;
        private readonly Dictionary<string, object?> _privateSlots = new(StringComparer.Ordinal);
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private bool _isConstructorEnabled;
        private ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private PrivateNameScope? _privateNameScope;
        private IJsObjectLike? _homeObject;

        public TypedGeneratorFactory(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool isConstructorFunction = true)
        {
            if (!function.IsGenerator)
            {
                throw new ArgumentException("Factory can only wrap generator functions.", nameof(function));
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
            var instance = new TypedGeneratorInstance(
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
            return instance.CreateGeneratorObject();
        }

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsFrozen => _properties.IsFrozen;

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
            if (name.IsPrivateSlotName())
            {
                if (_privateSlots.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            if (_properties.TryGetProperty(name, receiver ?? this, out value))
            {
                return true;
            }

            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
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
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];

                        return new HostFunction((_, innerArgs) =>
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
            if (name.IsPrivateSlotName())
            {
                _privateSlots[name] = value;
                return;
            }

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
                ? $"[GeneratorFunction: {name.Name}]"
                : "[GeneratorFunction]";
        }

        private void EnsureGeneratorIntrinsics()
        {
            // Lazily initialize the GeneratorFunction.prototype and Generator.prototype intrinsics
            // if they haven't been created yet.

            // First create %GeneratorPrototype% if needed
            if (_realmState.GeneratorPrototype is null)
            {
                var generatorProto = new JsObject();
                // %GeneratorPrototype% inherits from %IteratorPrototype%, which inherits from %Object.prototype%
                // For now, we'll just inherit from Object.prototype directly
                if (_realmState.ObjectPrototype is not null)
                {
                    generatorProto.SetPrototype(_realmState.ObjectPrototype);
                }

                _realmState.GeneratorPrototype = generatorProto;
            }

            // Then create %GeneratorFunction.prototype% and link it to %GeneratorPrototype%
            if (_realmState.GeneratorFunctionPrototype is null && _realmState.FunctionPrototype is not null)
            {
                var genFuncProto = new JsObject();
                genFuncProto.SetPrototype(_realmState.FunctionPrototype);

                // %GeneratorFunction.prototype% should have a .prototype property pointing to %GeneratorPrototype%
                genFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = _realmState.GeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });

                _realmState.GeneratorFunctionPrototype = genFuncProto;
            }
        }

        private void InitializeProperties()
        {
            // Set up the generator function's [[Prototype]] chain.
            // According to the spec, generator functions should inherit from %GeneratorFunction.prototype%,
            // which in turn inherits from %Function.prototype%.
            EnsureGeneratorIntrinsics();

            if (_realmState.GeneratorFunctionPrototype is JsObject genFuncProto)
            {
                _properties.SetPrototype(genFuncProto);
            }
            else if (_realmState.FunctionPrototype is { } functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            if (_isConstructorEnabled && _realmState.GeneratorPrototype is not null)
            {
                // Set up the generator function's .prototype property.
                // Each generator function instance gets its own .prototype object that inherits
                // from %GeneratorPrototype%.
                // Per spec 25.2.4.2: The prototype property is created as a plain object with
                // no own properties (the constructor property is inherited from %GeneratorPrototype%).
                var generatorPrototype = new JsObject();
                generatorPrototype.SetPrototype(_realmState.GeneratorPrototype);
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
