using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedGeneratorFactory : IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
        IExtensibilityControl,
        IFunctionNameTarget, ICallerInfo
    {
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _isLexicallyStrict;
        private readonly Dictionary<string, object?> _privateSlots = new(StringComparer.Ordinal);
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private bool _isConstructorEnabled;
        private IJsCallable? _caller;
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

            // Handle call/apply/bind specially BEFORE looking them up in prototype chain
            // This ensures generator functions get proper constructor semantics for bound functions
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

                        // Generator functions are never constructors, so bound generator functions
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
                        }) { DisallowConstruct = true };
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

                // Create the GeneratorFunction constructor if we have access to the engine
                if (_realmState.Engine is { } engine && _realmState.GeneratorFunctionConstructor is null)
                {
                    var generatorFunctionConstructor = CreateGeneratorFunctionConstructor(engine, _realmState);
                    _realmState.GeneratorFunctionConstructor = generatorFunctionConstructor;

                    // Set GeneratorFunctionPrototype.constructor = GeneratorFunction
                    genFuncProto.DefineProperty("constructor",
                        new PropertyDescriptor
                        {
                            Value = generatorFunctionConstructor,
                            Writable = false,
                            Enumerable = false,
                            Configurable = true,
                            HasValue = true,
                            HasWritable = true,
                            HasEnumerable = true,
                            HasConfigurable = true
                        });

                    // GeneratorFunction.__proto__ === Function (inherit from Function)
                    if (_realmState.FunctionPrototype is { } functionPrototype)
                    {
                        generatorFunctionConstructor.Properties.SetPrototype(functionPrototype);
                    }

                    // GeneratorFunction.prototype = GeneratorFunctionPrototype
                    generatorFunctionConstructor.SetProperty("prototype", genFuncProto);
                }
            }
        }

        private static HostFunction CreateGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction generatorFunctionConstructor = null!;

            generatorFunctionConstructor = new HostFunction((_, args) =>
                GeneratorFunctionConstructorBody(args, generatorFunctionConstructor, engine, realm))
            {
                RealmState = realm
            };

            generatorFunctionConstructor.SetInvokeWithContext((args, _, _, newTarget) =>
                GeneratorFunctionConstructorBody(args, newTarget as IJsCallable ?? generatorFunctionConstructor, engine, realm));

            StdLib.StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "length", 1d, configurable: true);
            StdLib.StandardLibrary.DefineConstantProperty(generatorFunctionConstructor, "name", "GeneratorFunction", configurable: true);

            return generatorFunctionConstructor;
        }

        private static object? GeneratorFunctionConstructorBody(
            IReadOnlyList<object?> args,
            IJsCallable newTarget,
            JsEngine engine,
            RealmState realm)
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1] : string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i], evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(",", parameters);

            // Build source as a generator function (note the *)
            var functionSource = $"(function* anonymous({paramList}\n) {{\n{bodySource}\n}})";

            ParsedProgram program;
            try
            {
                program = engine.ParseForExecution(functionSource);
            }
            catch (Parser.ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(StdLib.StandardLibrary.CreateSyntaxError(message, evalContext, realm));
            }

            var created = engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);

            // The result should now be a TypedGeneratorFactory
            if (created is IJsObjectLike objectLike)
            {
                // Resolve the prototype from the newTarget
                var proto = StdLib.StandardLibrary.ResolveConstructPrototype(
                    newTarget,
                    realm.GeneratorFunctionConstructor,
                    realm);
                if (proto is not null)
                {
                    objectLike.SetPrototype(proto);
                }
            }

            return created;
        }

        private static string ToFunctionArgumentString(object? value, EvaluationContext evalContext, RealmState realm)
        {
            var primitive = Runtime.JsOps.ToPrimitive(value, "string", evalContext);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            switch (primitive)
            {
                case null:
                    return "null";
                case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
                    return "undefined";
                case Symbol:
                case TypedAstSymbol:
                    throw StdLib.StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", evalContext, realm);
                case bool flag:
                    return flag ? "true" : "false";
                case string s:
                    return s;
                case JsBigInt bigInt:
                    return bigInt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case double d when double.IsNaN(d):
                    return "NaN";
                case double d when double.IsPositiveInfinity(d):
                    return "Infinity";
                case double d when double.IsNegativeInfinity(d):
                    return "-Infinity";
                case double d:
                    return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Convert.ToString(primitive, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
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

        // ICallerInfo implementation - generator functions are always strict mode
        IJsCallable? ICallerInfo.Caller
        {
            get => _caller;
            set => _caller = value;
        }

        // Generator functions are effectively strict mode, so accessing caller/arguments should throw
        bool ICallerInfo.IsStrictFunction => true;
    }
}
