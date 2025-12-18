using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    public sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor, IJsObjectLike,
        ICallableMetadata, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost,
        IExtensibilityControl, IPrototypeAccessorProvider
    {
        private readonly Symbol[] _bodyLexicalNames;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _hasHoistableDeclarations;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly JsValue _lexicalThis;
        private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private readonly bool _isStrict;
        private readonly bool _wasAsyncFunction;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly bool _hasParameterExpressions;
        private readonly bool _allowIdentifierCache;
        private readonly bool _isSimpleFunction;
        private readonly bool _usesArguments;
        private readonly bool _argumentsObjectNeeded;
        private readonly bool _canPoolInvocationEnvironment;
        private readonly string _functionDescription;
        private readonly ImmutableArray<Symbol> _parameterNames;
        private readonly ImmutableArray<Symbol> _lexicalTemplate;
        private readonly ImmutableArray<Symbol> _catchParameterTemplate;
        private readonly ImmutableArray<Symbol> _simpleCatchParameterTemplate;
        private readonly ImmutableArray<Symbol> _bodyLexicalTemplate;
        private static readonly System.Collections.Concurrent.ConcurrentBag<HashSet<Symbol>> SymbolSetPool = [];
        private bool _isConstructorEnabled;
        private ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private JsObject? _prototypeObject;
        private IJsObjectLike? _homeObject;
        private ImmutableArray<ClassField> _instanceFields = ImmutableArray<ClassField>.Empty;
        private bool _isClassConstructor;
        private bool _isDerivedClassConstructor;
        private IJsEnvironmentAwareCallable? _superConstructor;
        private IJsPropertyAccessor? _superPrototype;
        // Precomputed fast path eligibility - combines all conditions except newTarget.IsUndefined
        // Updated when setters are called that could invalidate fast path
        private bool _canUseFastPathBase;

        public TypedFunction(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true)
        {
            if (function.IsGenerator)
            {
                throw new NotSupportedException(
                    "Generator functions should be created via the generator factory.");
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            _properties.RealmState = _realmState;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            IsAsyncFunction = function.IsAsync;
            _wasAsyncFunction = function.WasAsync;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            IsArrowFunction = function.IsArrow;
            _isConstructorEnabled = isConstructorFunction;
            _bodyLexicalNames = CollectLexicalNames(function.Body).ToArray();
            _hasHoistableDeclarations = HasHoistableDeclarations(function.Body);
            _hasParameterExpressions = HasParameterExpressions(_function);
            // Allow identifier caching only if the function body has no with/eval AND
            // the closure chain has no with environments (functions defined inside with blocks
            // need to check with bindings at runtime)
            _allowIdentifierCache = AllowsIdentifierCaching(_function) && !closure.HasWithObjectInChain();
            _usesArguments = !IsArrowFunction && UsesArgumentsIdentifier(_function);

            // Detect simple functions for fast-path invocation
            // A simple function has: no async, no defaults, no destructuring, no body lexicals, no hoisting needed
            // Note: _hasFunctionNameEnvironment being true is fine - it just means the function name binding is
            // in an intermediate scope (for named function expressions), not in the invocation environment.
            // For non-strict mode: can use fast path if the function doesn't use 'arguments' identifier,
            // since mapped arguments object (which links argument values to parameter bindings) is not needed.
            var hasSimpleParams = HasOnlySimpleIdentifierParameters(function);
            var canUseFastPathForStrictness = _isStrict || !_usesArguments;
            _isSimpleFunction = canUseFastPathForStrictness &&
                               !function.IsAsync &&
                               !_wasAsyncFunction &&
                               !_hasParameterExpressions &&
                               _bodyLexicalNames.Length == 0 &&
                               !_hasHoistableDeclarations &&
                               _allowIdentifierCache &&
                               hasSimpleParams;

            // Can pool invocation environment if simple function AND no inner functions that would capture it
            _canPoolInvocationEnvironment = _isSimpleFunction &&
                                            !ContainsInnerFunctionExpression(function);

            // Cache the function description to avoid string allocation per call
            _functionDescription = function.Name is { } funcName ? $"function {funcName.Name}" : "anonymous function";

            var parameterNames = new List<Symbol>();
            CollectParameterNamesFromFunction(_function, parameterNames);
            _parameterNames = [..parameterNames];
            _lexicalTemplate = [.._bodyLexicalNames];
            var catchParams = CollectCatchParameterNames(_function.Body);
            _catchParameterTemplate = [..catchParams];
            var simpleCatchParams = CollectSimpleCatchParameterNames(_function.Body);
            _simpleCatchParameterTemplate = [..simpleCatchParams];
            var bodyLexicalSet = new HashSet<Symbol>(_bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalSet.ExceptWith(simpleCatchParams);
            _bodyLexicalTemplate = [..bodyLexicalSet];

            // ES2024 9.2.12 FunctionDeclarationInstantiation steps 17-20:
            // argumentsObjectNeeded is true unless:
            // - Arrow function (step 18)
            // - "arguments" is a parameter name (step 19)
            // - hasParameterExpressions is false AND "arguments" is in functionNames/lexicalNames (step 20)
            // Note: If hasParameterExpressions is true, arguments object is needed even if body has "let arguments"
            var argumentsIsParameterName = _parameterNames.Contains(Symbol.Arguments);
            var argumentsInBodyLexicalNames = bodyLexicalSet.Contains(Symbol.Arguments);
            var canSkipArgumentsForBodyDeclaration = !_hasParameterExpressions && argumentsInBodyLexicalNames;
            _argumentsObjectNeeded = !IsArrowFunction && !argumentsIsParameterName && !canSkipArgumentsForBodyDeclaration;

            if (IsArrowFunction)
            {
                try
                {
                    if (_closure.TryGet(Symbol.This, out var capturedThis))
                    {
                        _lexicalThis = JsValue.FromObjectUnsafe(capturedThis);
                    }
                    else
                    {
                        _lexicalThis = JsValue.Undefined;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                             StringComparison.Ordinal))
                {
                    _lexicalThis = JsValue.Uninitialized;
                    _lexicalThisEnvironment = _closure;
                }
            }

            var paramCount = GetExpectedParameterCount(function.Parameters);
            var functionNameValue = _function.Name?.Name ?? string.Empty;
            if (_realmState.FunctionPrototype is not null)
            {
                _properties.SetPrototype(_realmState.FunctionPrototype);
            }

            // Functions expose a prototype object so instances created via `new` can inherit from it.
            // Async functions do NOT have a prototype property per ES spec 15.8.3 (MakeConstructor is not called).
            // We need to check both IsAsyncFunction and _wasAsyncFunction because the CPS transformer
            // transforms async functions to sync with WasAsync=true.
            if (!IsArrowFunction && !IsAsyncFunction && !_wasAsyncFunction && _isConstructorEnabled)
            {
                var functionPrototype = new JsObject();
                functionPrototype.RealmState = _realmState;
                functionPrototype.Origin = string.IsNullOrEmpty(functionNameValue)
                    ? "anonymous function prototype"
                    : $"prototype of {functionNameValue}";
                functionPrototype.SetPrototype(_realmState.ObjectPrototype);
                functionPrototype.DefineProperty("constructor",
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
                        Value = functionPrototype,
                        Writable = true,
                        Enumerable = false,
                        Configurable = false,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });
            }

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

            // Initialize precomputed fast path eligibility
            // At construction: _isClassConstructor=false, _capturedPrivateNameScopes=empty, PrivateNameScope=null,
            // _homeObject=null, _superConstructor=null, _superPrototype=null
            // So we only need to check _isSimpleFunction and _lexicalThisEnvironment
            _canUseFastPathBase = _isSimpleFunction && _lexicalThisEnvironment is null;
        }

        public bool IsAsyncFunction { get; }

        internal bool IsClassConstructor => _isClassConstructor;
        // Async functions are never constructors per ES spec 15.8.3
        // Use IsAsyncLike to catch both IsAsyncFunction and _wasAsyncFunction (CPS-transformed async)
        public bool DisallowConstruct => !_isConstructorEnabled || IsAsyncLike;

        internal bool IsDerivedClassConstructor => _isClassConstructor && _isDerivedClassConstructor;

        public bool IsAsyncLike => IsAsyncFunction || _wasAsyncFunction;
        public PrivateNameScope? PrivateNameScope { get; private set; }

        public bool IsArrowFunction { get; }
        public RealmState RealmState => _realmState;

        public bool IsExtensible => _properties.IsExtensible;

        public void PreventExtensions()
        {
            _properties.PreventExtensions();
        }

        /// <summary>
        /// Coerces 'this' value for non-strict mode function calls.
        /// In non-strict mode, primitives are boxed to objects and null/undefined become globalThis.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue CoerceThisValueForNonStrict(JsValue thisValue)
        {
            // Null/undefined → globalThis
            if (thisValue.IsNullish)
            {
                return _realmState.Engine is { GlobalObject: { } globalObj }
                    ? (JsValue)globalObj
                    : JsValue.Undefined;
            }

            // Primitives → boxed objects
            if (thisValue.IsNumber)
            {
                return JsValue.FromObjectUnsafe(StandardLibrary.CreateNumberWrapper(thisValue.AsDouble(), realm: _realmState));
            }
            if (thisValue.IsString)
            {
                return JsValue.FromObjectUnsafe(StandardLibrary.CreateStringWrapper(thisValue.AsString(), realm: _realmState));
            }
            if (thisValue.IsBoolean)
            {
                return JsValue.FromObjectUnsafe(StandardLibrary.CreateBooleanWrapper(thisValue.AsBoolean(), realm: _realmState));
            }
            if (thisValue.IsBigInt)
            {
                return JsValue.FromObjectUnsafe(StandardLibrary.CreateBigIntWrapper(thisValue.AsBigInt(), realm: _realmState));
            }
            if (thisValue.IsSymbol && thisValue.TryUnwrap<TypedAstSymbol>(out var typedSymbol))
            {
                return JsValue.FromObjectUnsafe(StandardLibrary.CreateSymbolWrapper(typedSymbol, realm: _realmState));
            }

            // Already an object
            return thisValue;
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

        // Ensure constructor [[OwnPropertyKeys]] start with length/name/prototype as required by
        // ClassDefinitionEvaluation (ECMA-262 16.5.6.6, steps 31-33).
        internal void SeedIntrinsicConstructorKeys()
        {
            _properties.SeedIntrinsicConstructorKeys();
        }

        public JsEnvironment? CallingJsEnvironment { get; set; }

        public JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            return InvokeWithContext(arguments, thisValue, null);
        }

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsFrozen => _properties.IsFrozen;

        public IEnumerable<string> Keys => _properties.Keys;

        IJsPropertyAccessor? IPrototypeAccessorProvider.PrototypeAccessor => _properties.PrototypeAccessor;

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

        public bool Delete(string name)
        {
            return _properties.DeleteOwnProperty(name);
        }

        public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
        {
            // Arrow functions, async functions, and non-constructor functions should not have a "prototype" property
            // Per ES spec: Arrow functions and async functions are not constructors and don't have prototype property
            if (string.Equals(name, "prototype", StringComparison.Ordinal) &&
                (IsArrowFunction || IsAsyncFunction || _wasAsyncFunction || !_isConstructorEnabled))
            {
                value = JsValue.Undefined;
                return false;
            }

            if (_properties.TryGetProperty(name, receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver, out value))
            {
                return true;
            }

            // Provide minimal Function.prototype-style helpers for typed
            // functions so patterns like fn.call/apply/bind work for code
            // emitted by tools like Babel/regenerator.
            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = (JsValue)new HostFunction((thisValue, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.SliceFrom(1);
                        return callable.Invoke(callArgs, thisArg);
                    }, isConstructor: false);
                    return true;

                case "apply":
                    value = (JsValue)new HostFunction((thisValue, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        IReadOnlyList<JsValue> argList;
                        if (args.Count > 1 && args[1].TryGetObject<JsArray>(out var jsArray))
                        {
                            // items[i] is already JsValue from JsArray.Items
                            var items = jsArray.Items;
                            var jsValues = new JsValue[items.Count];
                            for (var i = 0; i < items.Count; i++)
                            {
                                jsValues[i] = items[i];
                            }
                            argList = jsValues;
                        }
                        else
                        {
                            argList = ArgumentSlice.Empty;
                        }
                        return callable.Invoke(argList, thisArg);
                    }, isConstructor: false);
                    return true;

                case "bind":
                    value = (JsValue)new HostFunction((thisValue, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);
                        var targetIsConstructor = JsOps.IsConstructor(callable);
                        return (JsValue)HostFunction.CreateBoundFunction(callable, boundThis, boundArgs, targetIsConstructor,
                            _realmState);
                    }, isConstructor: false);
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
            _properties.SetProperty(name, value, receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver);
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

        IEnumerable<string> IJsPropertyAccessor.GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public void AddPrivateBrand(object brand)
        {
            _privateBrands.Add(brand);
        }

        public bool HasPrivateBrand(object brand)
        {
            return _privateBrands.Contains(brand);
        }

        public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
        {
            return _properties.TryDefineProperty(name, descriptor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JsValue InvokeWithContext(
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget = default)
        {
            // Fast-path for simple functions - uses precomputed _canUseFastPathBase
            // Only check newTarget at runtime (everything else is fixed after construction)
            if (_canUseFastPathBase && newTarget.IsUndefined)
            {
                return InvokeSimpleFast(arguments, thisValue, callingContext);
            }

            return InvokeWithContextSlow(arguments, thisValue, callingContext, newTarget);
        }

        /// <summary>
        /// Ultra-fast invoke for 1-argument calls - avoids array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JsValue InvokeWithContext1(
            JsValue arg0,
            JsValue thisValue,
            EvaluationContext callingContext)
        {
            if (_canUseFastPathBase)
            {
                return InvokeSimpleFast1(arg0, thisValue, callingContext);
            }
            return InvokeWithContextSlow([arg0], thisValue, callingContext, JsValue.Undefined);
        }

        /// <summary>
        /// Ultra-fast invoke for 1-argument calls with environment reuse optimization.
        /// When reuseEnvironment is provided, the callee will reuse it instead of allocating a new one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JsValue InvokeWithContext1Reuse(
            JsValue arg0,
            JsValue thisValue,
            EvaluationContext callingContext,
            JsEnvironment reuseEnvironment)
        {
            if (_canUseFastPathBase)
            {
                return InvokeSimpleFast1Reuse(arg0, thisValue, callingContext, reuseEnvironment);
            }
            return InvokeWithContextSlow([arg0], thisValue, callingContext, JsValue.Undefined);
        }

        /// <summary>
        /// Ultra-fast invoke for 2-argument calls - avoids array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JsValue InvokeWithContext2(
            JsValue arg0,
            JsValue arg1,
            JsValue thisValue,
            EvaluationContext callingContext)
        {
            if (_canUseFastPathBase)
            {
                return InvokeSimpleFast2(arg0, arg1, thisValue, callingContext);
            }
            return InvokeWithContextSlow([arg0, arg1], thisValue, callingContext, JsValue.Undefined);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue InvokeWithContextSlow(
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget)
        {

            var context = _realmState.RentContext(pushScope: false);
            context.AllowIdentifierCache = _allowIdentifierCache;
            _realmState.Logger?.LogInformation(
                "InvokeWithContext enter func={Function} isAsync={IsAsync} wasAsync={WasAsync}",
                _function.Name?.Name ?? "<anonymous>",
                IsAsyncFunction,
                _wasAsyncFunction);
            if (_realmState.Logger is { } entryLogger && _isClassConstructor)
            {
                entryLogger.LogInformation(
                    "ctor entry func={Function} receiver={Receiver} newTarget={NewTarget}",
                    _function.Name?.Name ?? "<anonymous>",
                    DescribeValue(thisValue.ToObject()),
                    DescribeValue(newTarget.ToObject()));
            }
            if (_realmState.Logger is { } logger && _isStrict && !thisValue.IsUndefined)
            {
                logger.LogInformation("TypedFunction strict received thisValue type={Type}",
                    thisValue.Kind);
            }
            if (callingContext is not null)
            {
                context.CallDepth = callingContext.CallDepth;
                context.MaxCallDepth = callingContext.MaxCallDepth;
            }

            if (_isClassConstructor && newTarget.IsUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    callingContext ?? context,
                    _realmState);
                throw new ThrowSignal(JsValue.FromObjectUnsafe(error));
            }

            var lexicalNames = RentSymbolSet(_lexicalTemplate);
            var catchParameterNames = RentSymbolSet(_catchParameterTemplate);
            var simpleCatchParameterNames = RentSymbolSet(_simpleCatchParameterTemplate);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : RentSymbolSet(_bodyLexicalTemplate);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);

            var functionMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var functionScopeFrame = context.PushScope(ScopeKind.Function, functionMode);

            // When parameter expressions are present, keep the parameter environment outside
            // the var environment so defaults cannot observe body var bindings (spec step 27).
            JsEnvironment parameterEnvironment;
            JsEnvironment functionEnvironment;
            JsEnvironment varEnvironment;
            if (_hasParameterExpressions)
            {
                functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    _functionDescription);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                // Don't initialize slots for complex parameter expressions (destructuring, defaults)
                // Values are bound via dictionary, not slots
                functionEnvironment.ScopeId = _function.ScopeId;
                functionEnvironment.SetSlotMap(_function.SlotMap);

                parameterEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict, _function.Source,
                    _functionDescription, isParameterEnvironment: true);
                parameterEnvironment.IsArrowFunctionEnvironment = IsArrowFunction;
                parameterEnvironment.SetBodyLexicalNames(bodyLexicalNames);

                varEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict, _function.Source,
                    _functionDescription);
                varEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            }
            else
            {
                functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    _functionDescription);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                // Don't initialize slots in InvokeWithContext - values are bound via dictionary
                // Only InvokeSimpleFast uses slot-based parameter binding
                functionEnvironment.ScopeId = _function.ScopeId;
                functionEnvironment.SetSlotMap(_function.SlotMap);
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(varEnvironment, false, _isStrict,
                _function.Source, _functionDescription, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // Mark environment as default derived constructor for special argument forwarding per ES spec 15.7.14
            if (_function.IsDefaultDerivedConstructor)
            {
                functionEnvironment.IsDefaultDerivedConstructor = true;
                executionEnvironment.IsDefaultDerivedConstructor = true;
            }

            using var capturedPrivateScopes = !_capturedPrivateNameScopes.IsDefaultOrEmpty
                ? context.EnterPrivateNameScopes(_capturedPrivateNameScopes)
                : null;
            using var privateScope = PrivateNameScope is not null
                ? context.EnterPrivateNameScope(PrivateNameScope)
                : null;
            PendingClassFieldInitialization pendingFieldInitialization = default;
            var hasPendingFieldInitialization = false;

            if (!IsArrowFunction)
            {
                var newTargetValue = newTarget.IsUndefined ? JsValue.Undefined : newTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexical: true,
                    blocksFunctionScopeOverride: true);
            }

            // Bind `this`.
            var boundThis = thisValue.ToObject();
            if (IsArrowFunction)
            {
                var lexicalThis = _lexicalThis.ToObject();
                var lexicalThisInitialized = !ReferenceEquals(lexicalThis, JsEnvironment.Uninitialized);
                if (_lexicalThisEnvironment is not null)
                {
                    try
                    {
                        lexicalThis = _lexicalThisEnvironment.Get(Symbol.This);
                        lexicalThisInitialized = !ReferenceEquals(lexicalThis, JsEnvironment.Uninitialized);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                 StringComparison.Ordinal))
                    {
                        lexicalThis = JsEnvironment.Uninitialized;
                        lexicalThisInitialized = false;
                    }
                }

                boundThis = lexicalThis ?? Symbol.Undefined;
                if (lexicalThisInitialized)
                {
                    context.MarkThisInitialized();
                }
                else
                {
                    context.MarkThisUninitialized();
                }
                functionEnvironment.DefineJsValue(Symbol.This, JsValue.FromObjectUnsafe(boundThis));

                // Store a reference to the original environment that owns the `this` binding.
                // This is needed for super() calls in arrow functions - super() must update
                // the original constructor's `this` binding, not the arrow function's local copy.
                if (_lexicalThisEnvironment is not null &&
                    _lexicalThisEnvironment.TryFindBinding(Symbol.This, allowUninitialized: true, out var originalThisEnv, out _))
                {
                    functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment, JsValue.FromObjectUnsafe(originalThisEnv), false, isLexical: true);
                }

                var hasCopiedInitialization = false;
                if (_closure.TryGet(Symbol.ThisInitialized, out var closureThisInitialized))
                {
                    SetThisInitializationStatus(functionEnvironment, JsOps.ToBoolean(closureThisInitialized));
                    hasCopiedInitialization = true;
                }
                else if (_closure.TryGet(Symbol.Super, out var closureSuperStatus) &&
                         closureSuperStatus is SuperBinding closureSuperBinding)
                {
                    SetThisInitializationStatus(functionEnvironment, closureSuperBinding.IsThisInitialized);
                    hasCopiedInitialization = true;
                }

                SuperBinding? lexicalSuperBinding = null;
                if (_superConstructor is not null || _superPrototype is not null)
                {
                    lexicalSuperBinding = new SuperBinding(_superConstructor, _superPrototype, thisValue, true);
                }
                else if (_closure.TryGet(Symbol.Super, out var inheritedSuper) &&
                         inheritedSuper is SuperBinding inheritedSuperBinding)
                {
                    lexicalSuperBinding = inheritedSuperBinding;
                }

                if (lexicalSuperBinding is not null)
                {
                    functionEnvironment.RealmState?.Logger?.LogInformation(
                        "SuperBinding: define lexical for arrow/lexical this protoNull={ProtoNull} thisInit={ThisInit}",
                        lexicalSuperBinding.Prototype is null,
                        lexicalSuperBinding.IsThisInitialized);
                    functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(lexicalSuperBinding), false, isLexical: true,
                        blocksFunctionScopeOverride: true);
                    if (!hasCopiedInitialization)
                    {
                        SetThisInitializationStatus(functionEnvironment, lexicalSuperBinding.IsThisInitialized);
                    }
                }
            }
            else
            {
                if (_isClassConstructor &&
                    ReferenceEquals(boundThis, Symbol.Undefined) &&
                    !newTarget.IsUndefined)
                {
                    var constructedThis = new JsObject();
                    constructedThis.RealmState = _realmState;
                    var newTargetObj = newTarget.ToObject();
                    if (newTargetObj is IJsPropertyAccessor prototypeSource &&
                        JsOps.TryGetPropertyValue(prototypeSource, "prototype", out var protoVal) &&
                        protoVal is IJsPropertyAccessor protoAccessor)
                    {
                        constructedThis.SetPrototype(protoAccessor);
                    }
                    else if (_realmState.ObjectPrototype is { } defaultProto)
                    {
                        constructedThis.SetPrototype(defaultProto);
                    }

                    _realmState.Logger?.LogInformation(
                        "ctor: synthesized receiver func={Function} receiver={Receiver} proto={Proto} newTargetType={NewTargetType}",
                        _function.Name?.Name ?? "<anonymous>",
                        DescribeValue(constructedThis),
                        DescribePrototype(constructedThis.PrototypeAccessor ?? constructedThis.Prototype),
                        newTargetObj?.GetType().Name ?? "null");

                    boundThis = constructedThis;
                }

                if (!_isStrict)
                {
                    if (thisValue.IsNullish)
                    {
                        boundThis = _realmState.Engine is { GlobalObject: { } globalObj }
                            ? globalObj
                            : Symbol.Undefined;
                    }

                    if (boundThis is not IJsPropertyAccessor &&
                        !IsNullish(boundThis) &&
                        boundThis is not IIsHtmlDda)
                    {
                        boundThis = ToObjectForDestructuring(boundThis, context);
                    }
                }

                object? initialThisValue;
                var initialThisInitialized = true;
                if (_isDerivedClassConstructor)
                {
                    context.MarkThisUninitialized();
                    initialThisInitialized = false;
                    initialThisValue = JsEnvironment.Uninitialized;
                }
                else
                {
                    context.MarkThisInitialized();
                    initialThisInitialized = true;
                    initialThisValue = boundThis;
                    if (!_isStrict && initialThisValue is null)
                    {
                        initialThisValue = new JsObject
                        {
                            RealmState = _realmState
                        };
                    }

                    boundThis = initialThisValue;
                }

                SetThisInitializationStatus(functionEnvironment, initialThisInitialized);
                functionEnvironment.DefineJsValue(Symbol.This, JsValue.FromObjectUnsafe(initialThisValue));

                if (_isClassConstructor && initialThisValue is JsObject ctorThis)
                {
                    _realmState.Logger?.LogInformation(
                        "ctor: bound this func={Function} this={This} proto={Proto} initialized={Initialized}",
                        _function.Name?.Name ?? "<anonymous>",
                        DescribeValue(ctorThis),
                        DescribePrototype(ctorThis.PrototypeAccessor ?? ctorThis.Prototype),
                        initialThisInitialized);
                }

                IJsPropertyAccessor? prototypeForSuper = null;
                if (_homeObject is not null)
                {
                    // Super property resolution is based on the current [[Prototype]] of the home object,
                    // even if it has been mutated after class definition (e.g. Object.setPrototypeOf).
                    prototypeForSuper = (_homeObject as IPrototypeAccessorProvider)?.PrototypeAccessor ??
                                        _homeObject.Prototype;
                    prototypeForSuper ??= _superPrototype;
                }
                else
                {
                    prototypeForSuper = _superPrototype;
                    if (prototypeForSuper is null && thisValue.TryGetObject<JsObject>(out var thisObj))
                    {
                        prototypeForSuper = thisObj.Prototype;
                    }
                }

                if (_homeObject is not null || _superConstructor is not null || prototypeForSuper is not null)
                {
                    var runtimeSuperConstructor = _superConstructor;
                    if (_isClassConstructor)
                    {
                        var runtimeCtorPrototype =
                            (this as IPrototypeAccessorProvider)?.PrototypeAccessor ?? Prototype;
                        if (runtimeCtorPrototype is IJsEnvironmentAwareCallable ctorLike)
                        {
                            runtimeSuperConstructor = ctorLike;
                        }
                    }

                    var binding = new SuperBinding(runtimeSuperConstructor, prototypeForSuper, JsValue.FromObjectUnsafe(boundThis),
                        initialThisInitialized);
                    functionEnvironment.RealmState?.Logger?.LogInformation(
                        "SuperBinding: define in function env env={Env} isCtor={IsCtor} isDerivedCtor={IsDerivedCtor} protoNull={ProtoNull} thisInit={ThisInit}",
                        functionEnvironment.GetHashCode(),
                        _isClassConstructor,
                        _isDerivedClassConstructor,
                        prototypeForSuper is null,
                        initialThisInitialized);
                    functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(binding));
                }

                if (_isClassConstructor && boundThis is JsObject thisInstance)
                {
                    if (_isDerivedClassConstructor)
                    {
                        pendingFieldInitialization = new PendingClassFieldInitialization(this, functionEnvironment);
                        context.PushClassFieldInitializer(pendingFieldInitialization);
                        hasPendingFieldInitialization = true;
                    }
                    else
                    {
                        InitializeInstance(thisInstance, functionEnvironment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (context.IsThrow)
                            {
                                var thrownDuringInitialization = context.FlowValue;
                                callingContext?.SetThrow(thrownDuringInitialization);
                                return thrownDuringInitialization;
                            }

                            return JsValue.Undefined;
                        }
                    }
                }
            }

            try
            {
                // Convert JsValue arguments to object? once - reused for both arguments object and parameter binding
                var argumentValues = new object?[arguments.Count];
                for (var i = 0; i < arguments.Count; i++)
                {
                    argumentValues[i] = arguments[i].ToObject();
                }

                // Create arguments object per ES2024 9.2.12 steps 17-20
                // Note: argumentsObjectNeeded handles all spec conditions (arrow, param name, lexical binding)
                if (_argumentsObjectNeeded)
                {
                    // Create the `arguments` binding up front so parameter default expressions can reference it.
                    var argumentsObject =
                        CreateArgumentsObject(_function, argumentValues, parameterEnvironment, _realmState, this,
                            _isStrict);
                    parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject), isLexical: false);
                    if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
                    {
                        functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject), isLexical: false);
                    }
                }

                // Named function expressions should see their name inside the body.
                if (!IsArrowFunction && _function.Name is { } functionName && !_hasFunctionNameEnvironment)
                {
                    parameterEnvironment.DefineJsValue(functionName, JsValue.FromObjectUnsafe(this), isConst: true, isLexical: true, blocksFunctionScopeOverride: true);
                }

                // Wrap parameter binding and body evaluation in the same try-catch for async functions.
                // This ensures ThrowSignal exceptions from TDZ errors during parameter default evaluation
                // are properly caught and converted to rejected promises.
                try
                {
                // Bind parameters using the same converted array
                BindFunctionParameters(_function, argumentValues, parameterEnvironment, context);
                if (context.ShouldStopEvaluation)
                {
                    if (context.IsThrow)
                    {
                        var thrownDuringBinding = context.FlowValue;
                        if (IsAsyncFunction || _wasAsyncFunction)
                        {
                            // Async functions must reject instead of throwing synchronously.
                            // Use CreateRejectedPromiseFromRealm which uses the RealmState's
                            // PromiseConstructor, ensuring we always have access to Promise.
                            callingContext?.Clear();

                            var rejectedBindingResult = CreateRejectedPromiseFromRealm(thrownDuringBinding);
                            return rejectedBindingResult is JsValue rejBindJs ? rejBindJs : JsValue.FromObjectUnsafe(rejectedBindingResult);
                        }

                        if (callingContext is not null)
                        {
                            callingContext.SetThrow(thrownDuringBinding);
                            return thrownDuringBinding;
                        }

                        throw new ThrowSignal(thrownDuringBinding);
                    }

                    return JsValue.Undefined;
                }

                if (_hasHoistableDeclarations)
                {
                    HoistVarDeclarations(_function.Body, executionEnvironment, context,
                        lexicalNames: lexicalNames,
                        catchParameterNames: catchParameterNames,
                        simpleCatchParameterNames: simpleCatchParameterNames);
                }

                if (_hasFunctionNameEnvironment &&
                    _function.Name is { } hoistedName &&
                    ContainsVarDeclaration(_function, hoistedName) &&
                    !functionEnvironment.TryGet(hoistedName, out _))
                {
                    functionEnvironment.DefineFunctionScoped(hoistedName, Symbol.Undefined, false, context: context);
                }

                    _ = EvaluateBlockJsValue(
                        _function.Body,
                        executionEnvironment,
                        context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    var thrownObj = thrown.ToObject();
                    _realmState.Logger?.LogInformation(
                        "InvokeWithContext propagating throw type={ThrowType} callerHasContext={HasCaller} func={FunctionName}",
                        thrownObj?.GetType().Name ?? "null",
                        callingContext is not null,
                        _function.Name?.Name ?? "<anonymous>");

                    if (IsAsyncFunction || _wasAsyncFunction)
                    {
                        var rejectedThrowResult = CreateRejectedPromise(thrown, executionEnvironment);
                        return rejectedThrowResult is JsValue rejThrowJs ? rejThrowJs : JsValue.FromObjectUnsafe(rejectedThrowResult);
                    }

                    if (callingContext is not null)
                    {
                        callingContext.SetThrow(thrown);
                        return thrown;
                    }

                    throw new ThrowSignal(thrown);
                }

                // Use IsAsyncLike so CPS-transformed async functions (WasAsync=true, IsAsync=false)
                // still wrap completion values in a promise.
                if (!IsAsyncLike)
                {
                    if (!context.IsReturn)
                    {
                        if (_isClassConstructor)
                        {
                            try
                            {
                                if (functionEnvironment.TryGet(Symbol.This, out var currentThis))
                            {
                                _realmState.Logger?.LogInformation(
                                    "Class constructor returning this={This}",
                                    DescribeValue(currentThis));
                                return JsValue.FromObjectUnsafe(currentThis);
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                   "ReferenceError: this",
                                   StringComparison.Ordinal))
                        {
                            // If `this` is uninitialized (e.g., derived ctor without super()), surface a JS ReferenceError.
                            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                            throw new ThrowSignal(JsValue.FromObjectUnsafe(errorObject));
                        }
                    }

                        return JsValue.Undefined;
                    }

                    var value = context.FlowValue;
                    context.ClearReturn();
                    var valueObj = value.ToObject();
                    if (_isClassConstructor &&
                        !value.TryGetObject<JsObject>(out _) &&
                        !value.TryGetObject<IJsObjectLike>(out _))
                    {
                        // Per ES spec 9.2.2 [[Construct]] step 13c:
                        // For derived class constructors, if return value is not undefined,
                        // throw TypeError. For base class constructors, fall back to `this`.
                        if (_isDerivedClassConstructor && !ReferenceEquals(valueObj, Symbol.Undefined))
                        {
                            throw StandardLibrary.ThrowTypeError(
                                "Derived constructors may only return object or undefined",
                                context,
                                _realmState);
                        }

                        try
                        {
                            if (functionEnvironment.TryGet(Symbol.This, out var currentThis) &&
                                !ReferenceEquals(currentThis, JsEnvironment.Uninitialized))
                            {
                                _realmState.Logger?.LogInformation(
                                    "Class constructor returning bound this instead of non-object return value");
                                return JsValue.FromObjectUnsafe(currentThis);
                            }

                            // Per ES spec 9.2.2 [[Construct]] step 15:
                            // If return value is undefined, call GetThisBinding() which
                            // throws ReferenceError if `this` is uninitialized (super() not called)
                            if (_isDerivedClassConstructor &&
                                (ReferenceEquals(currentThis, JsEnvironment.Uninitialized) ||
                                 ReferenceEquals(valueObj, Symbol.Undefined)))
                            {
                                var errorObject = StandardLibrary.CreateReferenceError(
                                    "ReferenceError: this is not defined - must call super() in derived class constructor",
                                    context,
                                    context.RealmState);
                                throw new ThrowSignal(JsValue.FromObjectUnsafe(errorObject));
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                     "ReferenceError: this",
                                     StringComparison.Ordinal))
                        {
                            // Per ES spec 9.2.2 [[Construct]] step 15:
                            // For derived class constructors, if return value is undefined (or not an object),
                            // the spec calls GetThisBinding() which throws ReferenceError if this is uninitialized
                            if (_isDerivedClassConstructor)
                            {
                                var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                                throw new ThrowSignal(JsValue.FromObjectUnsafe(errorObject));
                            }
                            _realmState.Logger?.LogInformation(
                                "Class constructor missing initialized this; falling back to return value reason={Reason}",
                                ex.Message);
                        }
                    }

                    return JsValue.FromObjectUnsafe(valueObj);
                }

                object? completionValue;
                if (context.IsReturn)
                {
                    completionValue = context.FlowValue;
                    context.ClearReturn();
                }
                else
                {
                    completionValue = Symbol.Undefined;
                }

                _realmState.Logger?.LogInformation(
                    "Async completion func={Function} isAsync={IsAsync} wasAsync={WasAsync} completionType={Type}",
                    _function.Name?.Name ?? "<anonymous>",
                    IsAsyncFunction,
                    _wasAsyncFunction,
                    completionValue?.GetType().Name ?? "null");
                var resolvedResult = CreateResolvedPromise(completionValue, executionEnvironment);
                return resolvedResult is JsValue resolvedJs ? resolvedJs : JsValue.FromObjectUnsafe(resolvedResult);
            }
            catch (ThrowSignal signal) when (IsAsyncFunction || _wasAsyncFunction)
            {
                // Use CreateRejectedPromiseFromRealm which uses the RealmState's PromiseConstructor
                // directly, avoiding environment lookup that might fail during parameter binding.
                var rejectedResult = CreateRejectedPromiseFromRealm(signal.ThrownValue);
                return rejectedResult is JsValue rejectedJs ? rejectedJs : JsValue.FromObjectUnsafe(rejectedResult);
            }
            finally
            {
                ReturnSymbolSet(lexicalNames);
                ReturnSymbolSet(catchParameterNames);
                ReturnSymbolSet(simpleCatchParameterNames);
                if (!ReferenceEquals(bodyLexicalNames, lexicalNames))
                {
                    ReturnSymbolSet(bodyLexicalNames);
                }
            }
        }
        finally
        {
            if (hasPendingFieldInitialization)
            {
                context.RemovePendingClassFieldInitializer(this);
            }
            _realmState.ReturnContext(context);
        }
    }

        private static HashSet<Symbol> RentSymbolSet()
        {
            if (SymbolSetPool.TryTake(out var set))
            {
                set.Clear();
                return set;
            }

            return new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        }

        private static HashSet<Symbol> RentSymbolSet(IEnumerable<Symbol> seed)
        {
            var set = RentSymbolSet();
            set.UnionWith(seed);
            return set;
        }

        private static void ReturnSymbolSet(HashSet<Symbol> set)
        {
            set.Clear();
            SymbolSetPool.Add(set);
        }

        /// <summary>
        /// Creates a rejected promise using the realm's Promise constructor.
        /// Unlike CreateRejectedPromise which looks up Promise in the environment,
        /// this method uses the RealmState's PromiseConstructor directly.
        /// </summary>
        private object? CreateRejectedPromiseFromRealm(JsValue reason)
        {
            var promiseCtor = _realmState.PromiseConstructor;
            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("reject", out var rejectValue) &&
                rejectValue.TryGetObject<IJsCallable>(out var rejectCallable))
            {
                return rejectCallable.Invoke([reason], JsValue.FromObjectUnsafe(promiseCtor)).ToObject();
            }

            // Fallback if Promise.reject isn't available - return the reason directly
            // This shouldn't happen in normal operation since Promise is always registered
            return reason.ToObject();
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

        public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true,
            bool includeNonEnumerable = true)
        {
            return _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);
        }

        public void SetPrivateNameScope(PrivateNameScope? scope)
        {
            PrivateNameScope = scope;
            if (scope is not null)
            {
                _canUseFastPathBase = false;
            }
        }

        public void SetCapturedPrivateNameScopes(ImmutableArray<PrivateNameScope> scopes)
        {
            _capturedPrivateNameScopes = scopes;
            if (!scopes.IsDefaultOrEmpty)
            {
                _canUseFastPathBase = false;
            }
        }

        public void SetSuperBinding(IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype)
        {
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
            if (superConstructor is not null || superPrototype is not null)
            {
                _canUseFastPathBase = false;
            }
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
            _canUseFastPathBase = false;
        }

        public void DisableConstruction()
        {
            if (!_isConstructorEnabled)
            {
                return;
            }

            _prototypeObject = null;
            _properties.DeleteOwnProperty("prototype");
            _isConstructorEnabled = false;
        }

        public void SetPrototypeObject(JsObject prototype)
        {
            _prototypeObject = prototype;
        }

        public void SetIsClassConstructor(bool isDerived)
        {
            _isClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
            _canUseFastPathBase = false;
        }

        public void SetInstanceFields(ImmutableArray<ClassField> fields)
        {
            _instanceFields = fields;
        }

        /// <summary>
        /// Tries to get the current prototype value, which could be any object-like value
        /// (JsObject, TypedFunction, HostFunction, etc). Returns true if a valid prototype exists.
        /// </summary>
        internal bool TryGetPrototypeValue(out IJsObjectLike? prototype)
        {
            // Always check the current prototype property value first, in case it was reassigned
            // (e.g., FooObj.prototype = anotherFunction). Per ES spec, if the prototype property
            // is not an object, we should use the intrinsic %Object.prototype% instead, but
            // this is handled at the call site.
            if (_properties.TryGetProperty("prototype", JsValue.FromObjectUnsafe(this), out var value) && value.TryGetObject<IJsObjectLike>(out var objLike))
            {
                prototype = objLike;
                // Also cache if it's a JsObject for backwards compatibility
                if (objLike is JsObject jsObj)
                {
                    _prototypeObject = jsObj;
                }
                return true;
            }

            // Return cached value if we previously created one
            if (_prototypeObject is not null)
            {
                prototype = _prototypeObject;
                return true;
            }

            prototype = null;
            return false;
        }

        internal JsObject GetOrCreatePrototypeObject()
        {
            // Always check the current prototype property value first, in case it was reassigned
            // (e.g., FooObj.prototype = protoObj). The _prototypeObject cache is only used
            // as a fallback when the property hasn't been explicitly set.
            if (_properties.TryGetProperty("prototype", JsValue.FromObjectUnsafe(this), out var value) && value.TryGetObject<JsObject>(out var jsObj))
            {
                _prototypeObject = jsObj;
                return jsObj;
            }

            // Return cached value if we previously created one
            if (_prototypeObject is not null)
            {
                return _prototypeObject;
            }

            var created = new JsObject(_realmState.ObjectPrototype)
            {
                RealmState = _realmState,
                Origin = string.IsNullOrEmpty(_function.Name?.Name)
                    ? "anonymous function prototype (materialized)"
                    : $"prototype of {_function.Name!.Name} (materialized)"
            };
            _properties.SetProperty("prototype", (JsValue)created);
            _prototypeObject = created;
            return created;
        }

        private SuperBinding? ResolveInstanceFieldSuperBinding(JsEnvironment constructorEnvironment,
            IJsObjectLike instance)
        {
            if (constructorEnvironment.TryGet(Symbol.Super, out var existingBinding) &&
                existingBinding is SuperBinding binding)
            {
                return binding;
            }

            var prototypeForSuper = _superPrototype;
            if (prototypeForSuper is null)
            {
                prototypeForSuper = instance.Prototype?.Prototype;
            }

            if (prototypeForSuper is null && _superConstructor is null && instance.Prototype is null)
            {
                return null;
            }

            return new SuperBinding(_superConstructor, prototypeForSuper, JsValue.FromObjectUnsafe(instance), true);
        }

        public void InitializeInstance(IJsObjectLike instance, JsEnvironment environment, EvaluationContext context)
        {
            if (PrivateNameScope is not null && instance is IPrivateBrandHolder brandHolder)
            {
                // Per ES spec 7.3.28 PrivateMethodOrAccessorAdd, throw TypeError if private elements
                // already exist on the object (i.e., double initialization)
                if (brandHolder.HasPrivateBrand(PrivateNameScope.BrandToken))
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot initialize private members of the same class twice on the same object",
                        context,
                        context.RealmState);
                }

                brandHolder.AddPrivateBrand(PrivateNameScope.BrandToken);
            }

            if (_instanceFields.IsDefaultOrEmpty || _instanceFields.Length == 0)
            {
                return;
            }

            using var _ = PrivateNameScope is not null ? context.EnterPrivateNameScope(PrivateNameScope) : null;
            using var instanceFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict);

            foreach (var field in _instanceFields)
            {
                if (field.IsPrivate && PrivateNameScope is not null && instance is not IPrivateBrandHolder)
                {
                    throw StandardLibrary.ThrowTypeError("Invalid private field receiver", context, context.RealmState);
                }

                using var classFieldInitScope = context.EnterClassFieldInitializer();
                var initEnv = new JsEnvironment(environment, isStrict: true);
                initEnv.DefineJsValue(EvalHostFunction.FieldInitializerEvalFlag, JsValue.True, isConst: true, isLexical: true,
                    blocksFunctionScopeOverride: true);
                initEnv.DefineJsValue(Symbol.This, JsValue.FromObjectUnsafe(instance));

                var fieldSuperBinding = ResolveInstanceFieldSuperBinding(environment, instance);
                if (fieldSuperBinding is not null)
                {
                    initEnv.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(fieldSuperBinding), true, isLexical: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGet(Symbol.NewTarget, out var newTargetValue))
                {
                    // Class field initializers execute outside of any function body; shadow new.target with undefined.
                    initEnv.DefineJsValue(Symbol.NewTarget, JsValue.Undefined, true, isLexical: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGet(Symbol.Arguments, out var argumentsValue))
                {
                    initEnv.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsValue), isLexical: false);
                }

                var propertyName = field.Name;
                if (field.IsComputed)
                {
                    if (field.ComputedName is null)
                    {
                        throw new InvalidOperationException("Computed class field is missing name expression.");
                    }

                    var nameValue = EvaluateExpression(field.ComputedName, initEnv, context).ToObject();
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    propertyName = JsOps.GetRequiredPropertyName(nameValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }
                else if (field.IsPrivate && PrivateNameScope is not null && !propertyName.Contains('@'))
                {
                    propertyName = PrivateNameScope.GetKey(propertyName);
                }

                context.RealmState.Logger?.LogInformation(
                    "Initializing instance field '{PropertyName}' (computed={IsComputed}, private={IsPrivate})",
                    propertyName,
                    field.IsComputed,
                    field.IsPrivate);

                object? value = Symbol.Undefined;
                if (field.Initializer is not null)
                {
                    value = EvaluateExpression(field.Initializer, initEnv, context).ToObject();
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    if (value is TypedFunction typedFunction &&
                        typedFunction.IsArrowFunction &&
                        fieldSuperBinding is not null)
                    {
                        typedFunction.SetSuperBinding(fieldSuperBinding.Constructor, fieldSuperBinding.Prototype);
                    }

                    if (IsAnonymousFunctionDefinitionNode(field.Initializer))
                    {
                        var displayName = field.IsComputed ? propertyName : field.Name;
                        var atIndex = displayName.IndexOf('@');
                        if (atIndex > 0)
                        {
                            displayName = displayName[..atIndex];
                        }

                        SetAnonymousFunctionName(value, displayName);
                    }
                }

                context.RealmState.Logger?.LogInformation(
                    "InitInstance: ctor={Ctor} instance={Instance} field={Field} valueType={ValueType} value={Value}",
                    _function.Name?.Name ?? "<anonymous>",
                    DescribeValue(instance),
                    propertyName,
                    value?.GetType().Name ?? "null",
                    value);

                var descriptor = new PropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                };

                if (instance is IPropertyDefinitionHost definitionHost)
                {
                    if (!definitionHost.TryDefineProperty(propertyName, descriptor))
                    {
                        throw StandardLibrary.ThrowTypeError("Cannot define class field", context, context.RealmState);
                    }
                }
                else if (instance != null)
                {
                    instance.DefineProperty(propertyName, descriptor);
                }
                else
                {
                    throw StandardLibrary.ThrowTypeError("Cannot define class field", context, context.RealmState);
                }
            }

            context.RealmState.Logger?.LogInformation(
                "InitInstance complete: ctor={Ctor} instance={Instance} keys={Keys}",
                _function.Name?.Name ?? "<anonymous>",
                DescribeValue(instance),
                string.Join(",", instance.GetOwnPropertyKeysInOrder().Select(k => k.ToString())));
        }

        private static void SetAnonymousFunctionName(object? value, string displayName)
        {
            switch (value)
            {
                case TypedFunction typedFunction:
                    typedFunction.EnsureHasName(displayName, overwriteExisting: true);
                    break;
                case TypedGeneratorFactory generatorFactory:
                    generatorFactory.EnsureHasName(displayName, overwriteExisting: true);
                    break;
                case AsyncGeneratorFactory asyncGeneratorFactory:
                    asyncGeneratorFactory.EnsureHasName(displayName, overwriteExisting: true);
                    break;
            }
        }

        private static string DescribePrototype(object? proto)
        {
            if (proto is null)
            {
                return "null";
            }

            if (proto is JsObject jsObj)
            {
                var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
                return $"JsObject@{RuntimeHelpers.GetHashCode(jsObj)} origin='{origin}'";
            }

            return $"{proto.GetType().Name}@{RuntimeHelpers.GetHashCode(proto)}";
        }

        private static string DescribeValue(object? value)
        {
            if (value is JsObject jsObj)
            {
                var proto = jsObj.PrototypeAccessor ?? jsObj.Prototype;
                var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
                return $"JsObject@{RuntimeHelpers.GetHashCode(jsObj)} origin='{origin}' proto={DescribePrototype(proto)}";
            }

            if (value is null)
            {
                return "null";
            }

            return $"{value.GetType().Name}@{RuntimeHelpers.GetHashCode(value)}";
        }

        private static bool ContainsVarDeclaration(FunctionExpression function, Symbol name)
        {
            var work = new Stack<StatementNode>();
            work.Push(function.Body);

            while (work.Count > 0)
            {
                var statement = work.Pop();
                switch (statement)
                {
                    case VariableDeclaration { Kind: VariableKind.Var } varDecl:
                        foreach (var declarator in varDecl.Declarators)
                        {
                            if (BindingTargetContainsName(declarator.Target, name))
                            {
                                return true;
                            }
                        }

                        break;
                    case BlockStatement block:
                        foreach (var inner in block.Statements)
                        {
                            work.Push(inner);
                        }

                        break;
                    case IfStatement ifStatement:
                        work.Push(ifStatement.Then);
                        if (ifStatement.Else is { } elseBranch)
                        {
                            work.Push(elseBranch);
                        }

                        break;
                    case WhileStatement whileStatement:
                        work.Push(whileStatement.Body);
                        break;
                    case DoWhileStatement doWhileStatement:
                        work.Push(doWhileStatement.Body);
                        break;
                    case WithStatement withStatement:
                        work.Push(withStatement.Body);
                        break;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar)
                        {
                            work.Push(initVar);
                        }

                        if (forStatement.Body is not null)
                        {
                            work.Push(forStatement.Body);
                        }

                        break;
                    case ForEachStatement forEachStatement:
                        if (forEachStatement.DeclarationKind == VariableKind.Var &&
                            BindingTargetContainsName(forEachStatement.Target, name))
                        {
                            return true;
                        }

                        work.Push(forEachStatement.Body);
                        break;
                    case LabeledStatement labeled:
                        work.Push(labeled.Statement);
                        break;
                    case TryStatement tryStatement:
                        work.Push(tryStatement.TryBlock);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            work.Push(catchClause.Body);
                        }

                        if (tryStatement.Finally is { } finallyBlock)
                        {
                            work.Push(finallyBlock);
                        }

                        break;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            work.Push(switchCase.Body);
                        }

                        break;
                }
            }

            return false;
        }

        private static bool BindingTargetContainsName(BindingTarget? target, Symbol name)
        {
            while (target is not null)
            {
                switch (target)
                {
                    case IdentifierBinding id:
                        return Equals(id.Name, name);
                    case ArrayBinding array:
                        foreach (var element in array.Elements)
                        {
                            if (BindingTargetContainsName(element.Target, name))
                            {
                                return true;
                            }
                        }

                        target = array.RestElement;
                        continue;
                    case ObjectBinding obj:
                        foreach (var property in obj.Properties)
                        {
                            if (BindingTargetContainsName(property.Target, name))
                            {
                                return true;
                            }
                        }

                        target = obj.RestElement;
                        continue;
                    default:
                        return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a function has only simple identifier parameters (no destructuring, no rest, no defaults).
        /// </summary>
        private static bool HasOnlySimpleIdentifierParameters(FunctionExpression function)
        {
            HashSet<Symbol>? seenNames = null;
            foreach (var param in function.Parameters)
            {
                // Must have Name set and no Pattern/DefaultValue
                if (param.Name is null || param.Pattern is not null || param.DefaultValue is not null || param.IsRest)
                {
                    return false;
                }

                // Check for duplicate parameter names - can't use fast path since
                // parameter count != slot count when duplicates exist
                seenNames ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                if (!seenNames.Add(param.Name))
                {
                    return false; // Duplicate parameter name
                }
            }
            return true;
        }

        /// <summary>
        /// Fast-path invocation for simple functions. Uses pooled EvaluationContext.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFast(IReadOnlyList<JsValue> arguments, JsValue thisValue, EvaluationContext? callingContext)
        {
            // Ultra-fast path for simple recursive functions (no arguments object, poolable environment)
            // This path has no try/catch to allow inlining
            if (_canPoolInvocationEnvironment && !_usesArguments && callingContext is not null)
            {
                return InvokeSimpleFastCore(arguments, thisValue, callingContext);
            }

            // Standard fast path with try/catch for exception handling
            return InvokeSimpleFastWithExceptionHandling(arguments, thisValue, callingContext);
        }

        /// <summary>
        /// Ultra-fast 1-argument invoke - avoids array allocation entirely.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFast1(JsValue arg0, JsValue thisValue, EvaluationContext callingContext)
        {
            if (_canPoolInvocationEnvironment && !_usesArguments)
            {
                return InvokeSimpleFastCore1(arg0, thisValue, callingContext);
            }
            return InvokeSimpleFastWithExceptionHandling([arg0], thisValue, callingContext);
        }

        /// <summary>
        /// Ultra-fast 1-argument invoke with environment reuse - avoids both array and environment allocation.
        /// The provided environment is reset and reused instead of allocating a new one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFast1Reuse(JsValue arg0, JsValue thisValue, EvaluationContext callingContext, JsEnvironment reuseEnvironment)
        {
            if (_canPoolInvocationEnvironment && !_usesArguments)
            {
                return InvokeSimpleFastCore1Reuse(arg0, thisValue, callingContext, reuseEnvironment);
            }
            return InvokeSimpleFastWithExceptionHandling([arg0], thisValue, callingContext);
        }

        /// <summary>
        /// Ultra-fast 2-argument invoke - avoids array allocation entirely.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFast2(JsValue arg0, JsValue arg1, JsValue thisValue, EvaluationContext callingContext)
        {
            if (_canPoolInvocationEnvironment && !_usesArguments)
            {
                return InvokeSimpleFastCore2(arg0, arg1, thisValue, callingContext);
            }
            return InvokeSimpleFastWithExceptionHandling([arg0, arg1], thisValue, callingContext);
        }

        /// <summary>
        /// Ultra-fast core invocation - no try/catch to allow JIT inlining.
        /// Only used when we can guarantee no ThrowSignal will escape (errors propagate via context).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFastCore(IReadOnlyList<JsValue> arguments, JsValue thisValue, EvaluationContext callingContext)
        {
            // Rent context from pool
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            var context = _realmState.RentContext(ScopeKind.Function, scopeMode, pushScope: true);
            context.AllowIdentifierCache = _allowIdentifierCache;
            context.CallDepth = callingContext.CallDepth;
            context.MaxCallDepth = callingContext.MaxCallDepth;

            // Rent environment from pool
            var functionEnvironment = _realmState.RentEnvironment(_closure, true, _isStrict, _function.Source, _functionDescription);
            functionEnvironment.ScopeId = _function.ScopeId;
            functionEnvironment.SetSlotMap(_function.SlotMap);
            if (_function.SlotCount > 0)
            {
                functionEnvironment.InitializeSlots(_function.SlotCount);
            }

            // Bind this - use lexical this for arrow functions, parameter for others
            JsValue boundThisValue;
            if (IsArrowFunction)
            {
                boundThisValue = _lexicalThis.IsUndefined ? JsValue.Undefined : _lexicalThis;
            }
            else if (_isStrict)
            {
                boundThisValue = thisValue;
            }
            else
            {
                boundThisValue = CoerceThisValueForNonStrict(thisValue);
            }
            functionEnvironment._thisValue = boundThisValue;
            functionEnvironment._hasThisValue = true;

            // Bind parameters to slots
            var slots = functionEnvironment._slots;
            if (slots is not null)
            {
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    slots[i] = value;
                    // When this function has closures (inner functions that capture variables),
                    // also bind to dictionary so closure lookups via TryLocateBinding work.
                    // This is needed when inner functions use dynamic scope (with/eval).
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[i], value);
                    }
                }
            }
            else
            {
                // Fallback when slots not available
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    functionEnvironment.DefineParameterFast(_parameterNames[i], value);
                }
            }

            // Execute body
            _ = EvaluateBlockJsValue(_function.Body, functionEnvironment, context);

            // Get result
            JsValue result;
            if (context.IsThrow)
            {
                result = context.FlowValue;
                Console.WriteLine($"[DEBUG InvokeSimpleFastCore] context.IsThrow=true, result type: {result.GetType()}, IsUndefined: {result.IsUndefined}, Kind: {result.Kind}");
                context.Clear();
                Console.WriteLine($"[DEBUG InvokeSimpleFastCore] Before SetThrow, callingContext.IsThrow: {callingContext.IsThrow}");
                callingContext.SetThrow(result);
                Console.WriteLine($"[DEBUG InvokeSimpleFastCore] After SetThrow, callingContext.IsThrow: {callingContext.IsThrow}, callingContext.FlowValue.Kind: {callingContext.FlowValue.Kind}");
            }
            else if (context.IsReturn)
            {
                result = context.FlowValue;
                context.ClearReturn();
            }
            else
            {
                result = JsValue.Undefined;
            }

            // Return pooled resources
            _realmState.ReturnContext(context);
            _realmState.ReturnEnvironment(functionEnvironment);

            return result;
        }

        /// <summary>
        /// Ultra-fast 1-argument core invocation - no array allocation, no try/catch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFastCore1(JsValue arg0, JsValue thisValue, EvaluationContext callingContext)
        {
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            var context = _realmState.RentContext(ScopeKind.Function, scopeMode, pushScope: true);
            context.AllowIdentifierCache = _allowIdentifierCache;
            context.CallDepth = callingContext.CallDepth;
            context.MaxCallDepth = callingContext.MaxCallDepth;

            var functionEnvironment = _realmState.RentEnvironment(_closure, true, _isStrict, _function.Source, _functionDescription);
            functionEnvironment.ScopeId = _function.ScopeId;
            functionEnvironment.SetSlotMap(_function.SlotMap);
            if (_function.SlotCount > 0)
            {
                functionEnvironment.InitializeSlots(_function.SlotCount);
            }

            // Bind this
            JsValue boundThisValue;
            if (IsArrowFunction)
            {
                boundThisValue = _lexicalThis.IsUndefined ? JsValue.Undefined : _lexicalThis;
            }
            else if (_isStrict)
            {
                boundThisValue = thisValue;
            }
            else
            {
                boundThisValue = CoerceThisValueForNonStrict(thisValue);
            }
            functionEnvironment._thisValue = boundThisValue;
            functionEnvironment._hasThisValue = true;

            // Bind single parameter directly - no array allocation
            var slots = functionEnvironment._slots;
            if (slots is not null && _parameterNames.Length > 0)
            {
                slots[0] = arg0;
                if (_function.HasClosures)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                }
            }
            else if (_parameterNames.Length > 0)
            {
                // Fallback when slots not available
                functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
            }

            _ = EvaluateBlockJsValue(_function.Body, functionEnvironment, context);

            JsValue result;
            if (context.IsThrow)
            {
                result = context.FlowValue;
                context.Clear();
                callingContext.SetThrow(result);
            }
            else if (context.IsReturn)
            {
                result = context.FlowValue;
                context.ClearReturn();
            }
            else
            {
                result = JsValue.Undefined;
            }

            _realmState.ReturnContext(context);
            _realmState.ReturnEnvironment(functionEnvironment);
            return result;
        }

        /// <summary>
        /// Ultra-fast 1-argument core invocation with environment reuse.
        /// Reuses the provided environment AND the calling context - avoids all pooling allocations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFastCore1Reuse(JsValue arg0, JsValue thisValue, EvaluationContext callingContext, JsEnvironment reuseEnvironment)
        {
            // Use the calling context directly - no renting needed for simple recursive functions
            // This avoids ConcurrentStack Node allocations from pool rent/return

            // Reuse the provided environment instead of renting a new one
            // Use ResetForReuse which keeps the slots array to avoid allocation
            reuseEnvironment.ResetForReuse(_closure, true, _isStrict, _function.Source, _functionDescription);
            reuseEnvironment.ScopeId = _function.ScopeId;
            reuseEnvironment.SetSlotMap(_function.SlotMap);
            // Skip InitializeSlots - for simple functions we only have parameters (no local vars),
            // and we're about to set the parameter slot directly below. This avoids the Array.Fill.

            // Bind this
            JsValue boundThisValue;
            if (IsArrowFunction)
            {
                boundThisValue = _lexicalThis.IsUndefined ? JsValue.Undefined : _lexicalThis;
            }
            else if (_isStrict)
            {
                boundThisValue = thisValue;
            }
            else
            {
                boundThisValue = CoerceThisValueForNonStrict(thisValue);
            }
            reuseEnvironment._thisValue = boundThisValue;
            reuseEnvironment._hasThisValue = true;

            // Bind single parameter directly - no array allocation, no Array.Fill needed
            var slots = reuseEnvironment._slots;
            if (slots is not null && _parameterNames.Length > 0)
            {
                slots[0] = arg0;
                if (_function.HasClosures)
                {
                    reuseEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                }
            }
            else if (_parameterNames.Length > 0)
            {
                // Fallback when slots not available
                reuseEnvironment.DefineParameterFast(_parameterNames[0], arg0);
            }

            _ = EvaluateBlockJsValue(_function.Body, reuseEnvironment, callingContext);

            JsValue result;
            if (callingContext.IsThrow)
            {
                result = callingContext.FlowValue;
                // Don't clear throw - let it propagate to caller
            }
            else if (callingContext.IsReturn)
            {
                result = callingContext.FlowValue;
                callingContext.ClearReturn();
            }
            else
            {
                result = JsValue.Undefined;
            }

            // Note: Do NOT return context or environment - caller owns them
            return result;
        }

        /// <summary>
        /// Ultra-fast 2-argument core invocation - no array allocation, no try/catch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue InvokeSimpleFastCore2(JsValue arg0, JsValue arg1, JsValue thisValue, EvaluationContext callingContext)
        {
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            var context = _realmState.RentContext(ScopeKind.Function, scopeMode, pushScope: true);
            context.AllowIdentifierCache = _allowIdentifierCache;
            context.CallDepth = callingContext.CallDepth;
            context.MaxCallDepth = callingContext.MaxCallDepth;

            var functionEnvironment = _realmState.RentEnvironment(_closure, true, _isStrict, _function.Source, _functionDescription);
            functionEnvironment.ScopeId = _function.ScopeId;
            functionEnvironment.SetSlotMap(_function.SlotMap);
            if (_function.SlotCount > 0)
            {
                functionEnvironment.InitializeSlots(_function.SlotCount);
            }

            // Bind this
            JsValue boundThisValue;
            if (IsArrowFunction)
            {
                boundThisValue = _lexicalThis.IsUndefined ? JsValue.Undefined : _lexicalThis;
            }
            else if (_isStrict)
            {
                boundThisValue = thisValue;
            }
            else
            {
                boundThisValue = CoerceThisValueForNonStrict(thisValue);
            }
            functionEnvironment._thisValue = boundThisValue;
            functionEnvironment._hasThisValue = true;

            // Bind both parameters directly - no array allocation
            var slots = functionEnvironment._slots;
            if (slots is not null)
            {
                if (_parameterNames.Length > 0)
                {
                    slots[0] = arg0;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                    }
                }
                if (_parameterNames.Length > 1)
                {
                    slots[1] = arg1;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[1], arg1);
                    }
                }
            }
            else
            {
                // Fallback when slots not available
                if (_parameterNames.Length > 0) functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                if (_parameterNames.Length > 1) functionEnvironment.DefineParameterFast(_parameterNames[1], arg1);
            }

            _ = EvaluateBlockJsValue(_function.Body, functionEnvironment, context);

            JsValue result;
            if (context.IsThrow)
            {
                result = context.FlowValue;
                context.Clear();
                callingContext.SetThrow(result);
            }
            else if (context.IsReturn)
            {
                result = context.FlowValue;
                context.ClearReturn();
            }
            else
            {
                result = JsValue.Undefined;
            }

            _realmState.ReturnContext(context);
            _realmState.ReturnEnvironment(functionEnvironment);
            return result;
        }

        /// <summary>
        /// Standard fast path with exception handling for functions that may throw.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue InvokeSimpleFastWithExceptionHandling(IReadOnlyList<JsValue> arguments, JsValue thisValue, EvaluationContext? callingContext)
        {
            // Rent context from pool - avoids allocation per call
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            var context = _realmState.RentContext(ScopeKind.Function, scopeMode, pushScope: false);
            context.AllowIdentifierCache = _allowIdentifierCache;

            if (callingContext is not null)
            {
                context.CallDepth = callingContext.CallDepth;
                context.MaxCallDepth = callingContext.MaxCallDepth;
            }

            // Create environment for function execution - use pooling when safe (no inner closures)
            var functionEnvironment = _canPoolInvocationEnvironment
                ? _realmState.RentEnvironment(_closure, true, _isStrict, _function.Source, _functionDescription)
                : new JsEnvironment(_closure, true, _isStrict, _function.Source, _functionDescription);

            // Initialize slots for O(1) variable access when scope analysis provided slot count
            // Always set ScopeId since we use it as indicator for _thisValue validity
            functionEnvironment.ScopeId = _function.ScopeId;
            functionEnvironment.SetSlotMap(_function.SlotMap);
            if (_function.SlotCount > 0)
            {
                functionEnvironment.InitializeSlots(_function.SlotCount);
            }

            // Bind this - keep as JsValue to avoid unnecessary boxing/unboxing
            JsValue boundThisValue;
            if (IsArrowFunction)
            {
                boundThisValue = _lexicalThis.IsUndefined ? JsValue.Undefined : _lexicalThis;
            }
            else if (_isStrict)
            {
                // In strict mode, this is passed through unchanged - null/undefined stay as-is
                boundThisValue = thisValue;
            }
            else
            {
                // In sloppy mode: null/undefined become global object, primitives get boxed
                boundThisValue = CoerceThisValueForNonStrict(thisValue);
            }
            // Bind this using fast field access (avoids dictionary allocation)
            functionEnvironment._thisValue = boundThisValue;
            functionEnvironment._hasThisValue = true;

            // Bind parameters directly to slots for O(1) access (avoids dictionary allocation)
            var slots = functionEnvironment._slots;
            if (slots is not null)
            {
                // Fast path: use slots
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    slots[i] = value;
                    // When this function has closures (inner functions that capture variables),
                    // also bind to dictionary so closure lookups via TryLocateBinding work.
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[i], value);
                    }
                }
            }
            else
            {
                // Fallback: use dictionary when slots not available
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    functionEnvironment.DefineParameterFast(_parameterNames[i], value);
                }
            }

            // Only create arguments object if the function body actually references it
            if (_usesArguments)
            {
                var argumentValues = new object?[arguments.Count];
                for (var i = 0; i < arguments.Count; i++)
                {
                    argumentValues[i] = arguments[i].ToObject();
                }
                var argumentsObject = new JsArgumentsObject(
                    argumentValues,
                    new Symbol?[arguments.Count], // No mapped parameters in strict mode
                    functionEnvironment,
                    mappedEnabled: false,
                    _realmState,
                    this,
                    isStrict: true);
                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject), isLexical: false);
            }

            try
            {
                _ = EvaluateBlockJsValue(_function.Body, functionEnvironment, context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (callingContext is not null)
                    {
                        callingContext.SetThrow(thrown);
                        return thrown;
                    }
                    throw new ThrowSignal(thrown);
                }

                if (context.IsReturn)
                {
                    var value = context.FlowValue;
                    context.ClearReturn();
                    return value; // FlowValue already returns JsValue, no need to wrap
                }

                return JsValue.Undefined;
            }
            catch (ThrowSignal signal)
            {
                if (callingContext is not null)
                {
                    callingContext.SetThrow(signal.ThrownValue);
                    return signal.ThrownValue;
                }
                throw;
            }
            finally
            {
                // Return context to pool for reuse
                _realmState.ReturnContext(context);

                // Return environment to pool if pooling was used
                if (_canPoolInvocationEnvironment)
                {
                    _realmState.ReturnEnvironment(functionEnvironment);
                }
            }
        }
    }
}
