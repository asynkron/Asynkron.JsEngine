using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class AsyncGeneratorFactory : IJsCallable, IJsObjectLike, IPropertyDefinitionHost, IExtensibilityControl,
        IFunctionNameTarget
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly RealmState _realmState;
        private readonly JsObject _properties = new();

        public AsyncGeneratorFactory(FunctionExpression function, JsEnvironment closure, RealmState realmState)
        {
            if (!function.IsGenerator || !function.IsAsync)
            {
                throw new ArgumentException("Factory can only wrap async generator functions.", nameof(function));
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            InitializeProperties();
        }

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
        {
            var instance = new AsyncGeneratorInstance(_function, _closure, arguments, thisValue, this, _realmState);
            instance.Initialize();
            return instance.CreateAsyncIteratorObject();
        }

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[AsyncGeneratorFunction: {name.Name}]"
                : "[AsyncGeneratorFunction]";
        }

        private void InitializeProperties()
        {
            if (_realmState.FunctionPrototype is JsObject functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            if (_realmState.ObjectPrototype is not null)
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
                _properties.SetProperty("prototype", generatorPrototype);
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

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsExtensible => _properties.IsExtensible;

        public IEnumerable<string> Keys => _properties.Keys;

        public void DefineProperty(string name, PropertyDescriptor descriptor)
        {
            _properties.DefineProperty(name, descriptor);
        }

        public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
        {
            return _properties.TryDefineProperty(name, descriptor);
        }

        public void SetPrototype(object? candidate)
        {
            _properties.SetPrototype(candidate);
        }

        public void PreventExtensions()
        {
            _properties.PreventExtensions();
        }

        public void Seal()
        {
            _properties.Seal();
        }

        public bool Delete(string name)
        {
            return _properties.DeleteOwnProperty(name);
        }

        public bool TryGetProperty(string name, object? receiver, out object? value)
        {
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
                        var thisArg = args.Count > 0 ? args[0] : Symbol.Undefined;
                        var callArgs = args.Count > 1 ? args.Skip(1).ToArray() : [];
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = new HostFunction((_, args) =>
                    {
                        var thisArg = args.Count > 0 ? args[0] : Symbol.Undefined;
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
                        var boundThis = args.Count > 0 ? args[0] : Symbol.Undefined;
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

        public IEnumerable<string> GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public IEnumerable<string> GetEnumerablePropertyNames()
        {
            return _properties.GetEnumerablePropertyNames();
        }

        public void EnsureHasName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (_function.Name is not null)
            {
                return;
            }

            var descriptor = _properties.GetOwnPropertyDescriptor("name");
            if (descriptor is { Configurable: false })
            {
                return;
            }

            if (descriptor is not null)
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
    }
}
