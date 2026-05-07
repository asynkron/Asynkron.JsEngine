#region

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Base class for generator-like function callables (function* and async function*).
    /// Contains all shared infrastructure: property management, call/apply/bind, private slots, etc.
    /// Subclasses only need to implement Invoke() and intrinsic setup.
    /// </summary>
    private abstract class GeneratorFunctionBase(
        FunctionExpression function,
        JsEnvironment closure,
        RealmState realmState,
        bool isLexicallyStrict,
        bool hasFunctionNameEnvironment,
        bool isConstructorFunction,
        FunctionExecutionPlanSeed planSeed)
        : IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
            IExtensibilityControl,
            IFunctionNameTarget, ICallableMetadata
    {
        private protected readonly JsEnvironment _closure = closure;
        private protected readonly FunctionExpression _function = function;
        private protected readonly bool _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
        private protected readonly bool _isLexicallyStrict = isLexicallyStrict;
        private protected readonly FunctionExecutionPlanSeed _planSeed = planSeed;
        private readonly Dictionary<string, JsValue> _privateSlots = new(StringComparer.Ordinal);
        private protected readonly JsObject _properties = new();

        private protected ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes =
            ImmutableArray<PrivateNameScope>.Empty;

        private protected IJsObjectLike? _homeObject;
        private protected bool _isConstructorEnabled = isConstructorFunction;

        public PrivateNameScope? PrivateNameScope { get; private set; }

        /// <summary>
        /// Returns the function description string for ToString().
        /// </summary>
        protected abstract string FunctionTypeName { get; }

        public bool IsArrowFunction => false;
        public bool DisallowConstruct => true;
        public RealmState RealmState { get; } = realmState;
        public SourceReference? SourceReference => _function.Source;

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
                if (descriptor.IsAccessorDescriptor || descriptor.JsValue.TryGetObject<IJsCallable>(out _))
                {
                    return;
                }

                if (descriptor.JsValue.TryGetString(out var existingName) && existingName.Length > 0)
                {
                    return;
                }
            }

            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    JsValue = new JsValue(name),
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
        }

        /// <summary>
        /// Invokes this generator function with the given arguments.
        /// Subclasses implement this to return the appropriate iterator/promise.
        /// </summary>
        public abstract JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue);

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsFrozen => _properties.IsFrozen;

        public IEnumerable<string> Keys => _properties.Keys;

        public void DefineProperty(string name, PropertyDescriptor descriptor)
        {
            _properties.DefineProperty(name, descriptor);
        }

        public void SetPrototype(IJsPropertyAccessor? candidate)
        {
            _properties.SetPrototype(candidate);
        }

        public void Seal()
        {
            _properties.Seal();
        }

        public bool Delete(string name)
        {
            return _properties.DeleteOwnProperty(name);
        }

        public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
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
            IJsCallable callable = this;
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
                        var argList = ReflectHelper.CreateFunctionApplyArgumentList(args.GetArgument(1), RealmState);

                        return callable.Invoke(argList, thisArg);
                    });
                    return true;

                case "bind":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);

                        // Generator functions are never constructors, so bound generator functions
                        // must also have DisallowConstruct = true per ES spec.
                        return (JsValue)new HostFunction((_, innerArgs) =>
                        {
                            if (boundArgs.Count == 0)
                            {
                                return callable.Invoke(innerArgs, boundThis);
                            }

                            if (innerArgs.Count == 0)
                            {
                                return callable.Invoke(boundArgs, boundThis);
                            }

                            var finalArgs = new JsValue[boundArgs.Count + innerArgs.Count];
                            for (var i = 0; i < boundArgs.Count; i++)
                            {
                                finalArgs[i] = boundArgs[i];
                            }

                            for (var i = 0; i < innerArgs.Count; i++)
                            {
                                finalArgs[boundArgs.Count + i] = innerArgs[i];
                            }

                            return callable.Invoke(finalArgs, boundThis);
                        }, RealmState, false)
                        { DisallowConstruct = true };
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
            if (name.IsPrivateSlotName())
            {
                _privateSlots[name] = value;
                return;
            }

            var receiverValue = receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver;
            _properties.SetProperty(name, value, receiverValue);
        }

        PropertyDescriptor? IJsPropertyAccessor.GetOwnPropertyDescriptor(string name)
        {
            return _properties.GetOwnPropertyDescriptor(name);
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

        public void SetPrivateNameScope(PrivateNameScope? scope)
        {
            PrivateNameScope = scope;
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

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[{FunctionTypeName}: {name.Name}]"
                : $"[{FunctionTypeName}]";
        }

        /// <summary>
        /// Ensures the required intrinsics (prototypes, constructors) are initialized.
        /// Called during InitializeProperties.
        /// </summary>
        protected abstract void EnsureIntrinsics();

        /// <summary>
        /// Gets the function prototype to use for this callable's [[Prototype]].
        /// </summary>
        protected abstract IJsPropertyAccessor? GetFunctionPrototype();

        /// <summary>
        /// Gets the generator prototype to use for the .prototype property's [[Prototype]].
        /// </summary>
        protected abstract JsObject? GetGeneratorPrototype();

        /// <summary>
        /// Called after the prototype object is created, allowing subclasses to add additional properties.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual void CustomizePrototypeObject(JsObject prototypeObject)
        {
            // Default: no additional customization
        }

        /// <summary>
        /// Validates the function type for this generator invoker.
        /// Throws ArgumentException if the function type is invalid.
        /// </summary>
        protected abstract void ValidateFunctionType();

        /// <summary>
        /// Validates the function type and initializes properties.
        /// Call this from derived constructors.
        /// </summary>
        protected void Initialize()
        {
            ValidateFunctionType();
            InitializeProperties();
        }

        /// <summary>
        /// Initializes the standard properties (prototype, length, name).
        /// </summary>
        private void InitializeProperties()
        {
            EnsureIntrinsics();

            var funcProto = GetFunctionPrototype();
            if (funcProto is not null)
            {
                _properties.SetPrototype(funcProto);
            }
            else if (RealmState.FunctionPrototype is { } functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            // Per ES spec: Generator functions ALWAYS have a prototype property
            var genProto = GetGeneratorPrototype();
            if (genProto is not null)
            {
                var generatorPrototype = new JsObject();
                generatorPrototype.SetPrototype(genProto);
                CustomizePrototypeObject(generatorPrototype);
                _properties.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = generatorPrototype,
                        Writable = true,
                        Enumerable = false,
                        Configurable = false
                    });
            }

            var paramCount = _function.Parameters.GetExpectedParameterCount();
            _properties.DefineProperty("length",
                new PropertyDescriptor
                {
                    Value = (double)paramCount,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });

            var functionNameValue = _function.Name?.Name ?? string.Empty;
            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = functionNameValue,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
        }

        /// <summary>
        /// Helper for converting a value to a function argument string (used by dynamic Function constructors).
        /// </summary>
        protected static string ToFunctionArgumentString(JsValue value, EvaluationContext evalContext, RealmState realm)
        {
            var primitiveObj = JsOps.ToPrimitive(value, ToPrimitiveHint.String, evalContext);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            var primitive = primitiveObj;

            if (primitive.IsNull)
            {
                return "null";
            }

            if (primitive.IsUndefined)
            {
                return "undefined";
            }

            if (primitive.TryUnwrap(out Symbol? _) || primitive.TryUnwrap(out JsSymbol? _))
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
                return bigInt.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (primitive.TryGetDouble(out var d))
            {
                if (double.IsNaN(d))
                {
                    return "NaN";
                }

                if (double.IsPositiveInfinity(d))
                {
                    return "Infinity";
                }

                if (double.IsNegativeInfinity(d))
                {
                    return "-Infinity";
                }

                return d.ToString(CultureInfo.InvariantCulture);
            }

            // At this point, all primitive types have been handled above;
            // remaining cases are objects, so use ObjectValue directly.
            return Convert.ToString(primitive.ObjectValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>
        /// Common implementation for generator function constructor body (GeneratorFunction, AsyncGeneratorFunction).
        /// Parses dynamic function source and returns the created function with proper prototype.
        /// </summary>
        /// <param name="args">Constructor arguments (parameters..., body).</param>
        /// <param name="newTarget">The new.target for prototype resolution.</param>
        /// <param name="engine">The JS engine instance.</param>
        /// <param name="realm">The current realm.</param>
        /// <param name="functionPrefix">The function declaration prefix (e.g., "function*" or "async function*").</param>
        /// <param name="defaultConstructor">The default constructor for prototype resolution.</param>
        protected static JsValue CreateDynamicGeneratorFunction(
            IReadOnlyList<JsValue> args,
            IJsCallable newTarget,
            JsEngine engine,
            RealmState realm,
            string functionPrefix,
            IJsCallable defaultConstructor)
        {
            var evalContext = realm.CreateContext();
            var argCount = args.Count;
            var bodyValue = argCount > 0 ? args[argCount - 1] : (JsValue)string.Empty;
            var parameterCount = Math.Max(argCount - 1, 0);
            var checkAwaitInParameters = functionPrefix.StartsWith("async", StringComparison.Ordinal);

            var parameters = new string[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                var paramText = ToFunctionArgumentString(args[i], evalContext, realm);
                parameters[i] = paramText;
            }

            var bodySource = ToFunctionArgumentString(bodyValue, evalContext, realm);
            var paramList = string.Join(',', parameters);
            var functionSource = $"({functionPrefix} anonymous({paramList}\n) {{\n{bodySource}\n}})";

            var scriptGoalOptions = new JsEngineOptions { AllowImportMeta = false };

            ProgramNode program;
            try
            {
                program = engine.ParseProgram(functionSource, options: scriptGoalOptions);
            }
            catch (ParseException parseException)
            {
                var message = parseException.Message ?? "SyntaxError";
                throw new ThrowSignal(StandardLibrary.CreateSyntaxError(message, evalContext, realm));
            }

            if (TryGetDynamicGeneratorFunctionExpression(program, out var parsedFunction) &&
                HasIllegalYieldOrAwaitInParameters(parsedFunction, checkAwaitInParameters))
            {
                throw StandardLibrary.ThrowSyntaxError("Invalid function parameter list", evalContext, realm);
            }

            var createdObj = engine.ExecuteProgram(
                program,
                engine.GlobalEnvironment,
                CancellationToken.None);

            var created = JsValue.FromObjectUnsafe(createdObj);

            if (created.TryUnwrap(out IJsObjectLike? objectLike))
            {
                var proto = ReflectHelper.ResolveConstructPrototype(newTarget, defaultConstructor, realm);
                if (proto is not null)
                {
                    objectLike.SetPrototype(proto);
                }
            }

            return created;
        }

        private static bool TryGetDynamicGeneratorFunctionExpression(ProgramNode program,
            [NotNullWhen(true)] out FunctionExpression? function)
        {
            function = null;

            if (program.Body.Length != 1)
            {
                return false;
            }

            if (program.Body[0] is not ExpressionStatement { Expression: FunctionExpression parsed })
            {
                return false;
            }

            function = parsed;
            return true;
        }

        private static bool HasIllegalYieldOrAwaitInParameters(FunctionExpression function, bool checkAwait)
        {
            foreach (var parameter in function.Parameters)
            {
                if (parameter.DefaultValue is not null)
                {
                    if (AstShapeAnalyzer.ContainsYield(parameter.DefaultValue))
                    {
                        return true;
                    }

                    if (checkAwait && AstShapeAnalyzer.ContainsAwait(parameter.DefaultValue))
                    {
                        return true;
                    }
                }

                if (parameter.Pattern is not null)
                {
                    if (AstShapeAnalyzer.BindingTargetContainsYieldInDefaultValue(parameter.Pattern))
                    {
                        return true;
                    }

                    if (checkAwait && AstShapeAnalyzer.BindingTargetContainsAwaitInDefaultValue(parameter.Pattern))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates an ExecutionPlanRunner for this generator function.
        /// </summary>
        protected ExecutionPlanRunner CreateRunner(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            var context = CreateInvocationContext(arguments, thisValue);
            return context.CreateRunner();
        }

        protected GeneratorInvocationContext CreateInvocationContext(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            return new GeneratorInvocationContext(
                _function,
                _closure,
                arguments,
                thisValue,
                this,
                RealmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                PrivateNameScope,
                _capturedPrivateNameScopes,
                _planSeed);
        }
    }

    private readonly struct GeneratorInvocationContext
    {
        private readonly FunctionExpression _function;
        private readonly JsEnvironment _closure;
        private readonly IReadOnlyList<JsValue> _arguments;
        private readonly JsValue _thisValue;
        private readonly IJsCallable _callable;
        private readonly RealmState _realmState;
        private readonly bool _isLexicallyStrict;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly IJsObjectLike? _homeObject;
        private readonly PrivateNameScope? _privateNameScope;
        private readonly ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes;
        private readonly FunctionExecutionPlanSeed _planSeed;

        public GeneratorInvocationContext(
            FunctionExpression function,
            JsEnvironment closure,
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            IJsCallable callable,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment,
            IJsObjectLike? homeObject,
            PrivateNameScope? privateNameScope,
            ImmutableArray<PrivateNameScope> capturedPrivateNameScopes,
            FunctionExecutionPlanSeed planSeed)
        {
            _function = function;
            _closure = closure;
            _arguments = arguments;
            _thisValue = thisValue;
            _callable = callable;
            _realmState = realmState;
            _isLexicallyStrict = isLexicallyStrict;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _homeObject = homeObject;
            _privateNameScope = privateNameScope;
            _capturedPrivateNameScopes = capturedPrivateNameScopes;
            _planSeed = planSeed;
        }

        public ExecutionPlanRunner CreateRunner()
        {
            return new ExecutionPlanRunner(
                _function,
                _closure,
                _arguments,
                _thisValue,
                _callable,
                _realmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                _privateNameScope,
                _capturedPrivateNameScopes,
                planOverride: _planSeed.Plan,
                planFailureOverride: _planSeed.Failure);
        }

        public AsyncGeneratorInvoker CreateAsyncGeneratorInvoker()
        {
            return new AsyncGeneratorInvoker(
                _function,
                _closure,
                _arguments,
                _thisValue,
                _callable,
                _realmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                _privateNameScope,
                _capturedPrivateNameScopes,
                _planSeed);
        }
    }
}
