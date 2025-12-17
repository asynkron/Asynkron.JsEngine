using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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
        private readonly bool _hasFunctionNameEnvironment;
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
            bool hasFunctionNameEnvironment = false,
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
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
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

        public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            var instance = new AsyncGeneratorInstance(
                _function,
                _closure,
                arguments,
                thisValue,
                this,
                _realmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                _privateNameScope,
                _capturedPrivateNameScopes);
            instance.Initialize();
            return (JsValue)instance.CreateAsyncIteratorObject();
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

        public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
        {
            // Handle call/apply/bind specially BEFORE looking them up in prototype chain
            // This ensures async generator functions get proper constructor semantics for bound functions
            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.SliceFrom(1);
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        IReadOnlyList<JsValue> argList = ArgumentSlice.Empty;
                        if (args.Count > 1 && args[1].TryUnwrap(out JsArray? jsArray))
                        {
                            var items = jsArray.Items;
                            var converted = new JsValue[items.Count];
                            for (var i = 0; i < items.Count; i++)
                            {
                                // items[i] is already JsValue from JsArray.Items
                                converted[i] = items[i];
                            }
                            argList = converted;
                        }
                        return callable.Invoke(argList, thisArg);
                    });
                    return true;

                case "bind":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);

                        // Async generator functions are never constructors, so bound async generator functions
                        // must also have DisallowConstruct = true per ES spec.
                        return (JsValue)new HostFunction((_, innerArgs) =>
                        {
                            if (boundArgs.Count == 0)
                                return callable.Invoke(innerArgs, boundThis);
                            if (innerArgs.Count == 0)
                                return callable.Invoke(boundArgs, boundThis);

                            var finalArgs = new JsValue[boundArgs.Count + innerArgs.Count];
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
            var receiverValue = receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver;
            if (_properties.TryGetProperty(name, receiverValue, out var objValue))
            {
                value = objValue;
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        public bool TryGetProperty(string name, out JsValue value)
        {
            return TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value);
        }


        public void SetProperty(string name, JsValue value)
        {
            SetProperty(name, value, JsValue.FromObjectUnsafe(this));
        }


        public void SetProperty(string name, JsValue value, JsValue receiver)
        {
            var receiverValue = receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver;
            _properties.SetProperty(name, value, receiverValue);
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

        private void EnsureAsyncGeneratorIntrinsics()
        {
            var engine = _realmState.Engine ?? throw new InvalidOperationException("Engine reference is missing.");

            // %AsyncIteratorPrototype% (inherits from %Object.prototype%)
            if (_realmState.AsyncIteratorPrototype is null)
            {
                var asyncIteratorProto = new JsObject();
                if (_realmState.ObjectPrototype is not null)
                {
                    asyncIteratorProto.SetPrototype(_realmState.ObjectPrototype);
                }

                _realmState.AsyncIteratorPrototype = asyncIteratorProto;
            }

            // %AsyncGeneratorPrototype% (inherits from %AsyncIteratorPrototype%)
            if (_realmState.AsyncGeneratorPrototype is null)
            {
                var asyncGenProto = new JsObject();
                asyncGenProto.SetPrototype(_realmState.AsyncIteratorPrototype ?? _realmState.ObjectPrototype);
                _realmState.AsyncGeneratorPrototype = asyncGenProto;
            }

            // %AsyncGeneratorFunction.prototype%
            if (_realmState.AsyncGeneratorFunctionPrototype is null && _realmState.FunctionPrototype is not null)
            {
                var asyncGenFuncProto = new JsObject();
                asyncGenFuncProto.SetPrototype(_realmState.FunctionPrototype);

                asyncGenFuncProto.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = _realmState.AsyncGeneratorPrototype,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });

                if (_realmState.AsyncGeneratorFunctionConstructor is null)
                {
                    _realmState.AsyncGeneratorFunctionConstructor =
                        CreateAsyncGeneratorFunctionConstructor(engine, _realmState);
                }

                if (_realmState.AsyncGeneratorFunctionConstructor is { } asyncGenCtor)
                {
                    asyncGenCtor.SetProperty("prototype", (JsValue)asyncGenFuncProto);
                }

                asyncGenFuncProto.DefineProperty("constructor",
                    new PropertyDescriptor
                    {
                        Value = _realmState.AsyncGeneratorFunctionConstructor,
                        Writable = false,
                        Enumerable = false,
                        Configurable = true,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });

                _realmState.AsyncGeneratorFunctionPrototype = asyncGenFuncProto;
            }
        }

        private static HostFunction CreateAsyncGeneratorFunctionConstructor(JsEngine engine, RealmState realm)
        {
            HostFunction constructor = null!;

            constructor = new HostFunction((_, args) =>
                    AsyncGeneratorFunctionConstructorBody(args, constructor, engine, realm))
            {
                RealmState = realm
            };

            constructor.SetInvokeWithContext((args, _, _, newTarget) =>
            {
                IJsCallable targetCallable = constructor;
                if (newTarget.TryUnwrap(out IJsCallable? callable))
                {
                    targetCallable = callable;
                }
                return AsyncGeneratorFunctionConstructorBody(args, targetCallable, engine, realm);
            });

            StandardLibrary.DefineConstantProperty(constructor, "length", 1d, configurable: true);
            StandardLibrary.DefineConstantProperty(constructor, "name", "AsyncGeneratorFunction", configurable: true);

            if (realm.FunctionPrototype is { } functionPrototype)
            {
                constructor.Properties.SetPrototype(functionPrototype);
            }

            return constructor;
        }

        private static JsValue AsyncGeneratorFunctionConstructorBody(
            IReadOnlyList<JsValue> args,
            IJsCallable newTarget,
            JsEngine engine,
            RealmState realm)
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1] : (JsValue)string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i], evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(",", parameters);
            var functionSource = $"(async function* anonymous({paramList}\n) {{\n{bodySource}\n}})";

            var scriptGoalOptions = new JsEngineOptions
            {
                AllowImportMeta = false
            };

            ParsedProgram program;
            try
            {
                program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
            }
            catch (Parser.ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(JsValue.FromObjectUnsafe(StandardLibrary.CreateSyntaxError(message, evalContext, realm)));
            }

            var createdObj = engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);

            var created = JsValue.FromObjectUnsafe(createdObj);

            if (created.TryUnwrap(out IJsObjectLike? objectLike))
            {
                var proto = StandardLibrary.ResolveConstructPrototype(
                    newTarget,
                    realm.AsyncGeneratorFunctionConstructor!,
                    realm);
                if (proto is not null)
                {
                    objectLike.SetPrototype(proto);
                }
            }

            return created;
        }

        private static string ToFunctionArgumentString(JsValue value, EvaluationContext evalContext, RealmState realm)
        {
            var primitiveObj = JsOps.ToPrimitive(value.ToObject(), ToPrimitiveHint.String, evalContext);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            var primitive = JsValue.FromObjectUnsafe(primitiveObj);

            if (primitive.IsNull)
            {
                return "null";
            }

            if (primitive.IsUndefined)
            {
                return "undefined";
            }

            if (primitive.TryUnwrap(out Symbol? _) || primitive.TryUnwrap(out TypedAstSymbol? _))
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", evalContext, realm);
            }

            if (primitive.TryGetBoolean(out var flag))
            {
                return flag ? "true" : "false";
            }

            if (primitive.TryGetString(out var s))
            {
                return s ?? string.Empty;
            }

            if (primitive.TryUnwrap(out JsBigInt? bigInt))
            {
                return bigInt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (primitive.TryGetDouble(out var d))
            {
                if (double.IsNaN(d))
                    return "NaN";
                if (double.IsPositiveInfinity(d))
                    return "Infinity";
                if (double.IsNegativeInfinity(d))
                    return "-Infinity";
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var obj = primitive.ToObject();
            return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private void InitializeProperties()
        {
            EnsureAsyncGeneratorIntrinsics();

            if (_realmState.AsyncGeneratorFunctionPrototype is { } asyncGenFuncProto)
            {
                _properties.SetPrototype(asyncGenFuncProto);
            }
            else if (_realmState.FunctionPrototype is { } functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            if (_isConstructorEnabled && _realmState.ObjectPrototype is not null)
            {
                var generatorPrototype = new JsObject();
                generatorPrototype.SetPrototype(_realmState.AsyncGeneratorPrototype ?? _realmState.ObjectPrototype);
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
