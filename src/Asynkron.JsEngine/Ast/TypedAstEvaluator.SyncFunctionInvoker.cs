#region

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    public sealed class SyncFunctionInvoker : IJsEnvironmentAwareCallable, IJsObjectLike,
        ICallableMetadata, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost,
        IExtensibilityControl, IPrototypeAccessorProvider, IAsJsValue
    {
        private static readonly ObjectPool<HashSet<Symbol>> SymbolSetPool = new(32,
            static () => new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance));

        /// <summary>
        /// Tracks the currently executing SyncFunctionInvoker on this thread.
        /// Used to resolve the non-standard Function.caller property (Annex B).
        /// </summary>
        [ThreadStatic]
        private static SyncFunctionInvoker? t_currentlyExecuting;

        // Cached JsValue to avoid repeated struct creation
        private readonly JsValue _cachedJsValue;

        private readonly bool _allowIdentifierCache;
        private readonly bool _argumentsObjectNeeded;
        private readonly ImmutableArray<Symbol> _bodyLexicalTemplate;
        private readonly bool _canPoolInvocationEnvironment;
        private readonly ImmutableArray<Symbol> _catchParameterTemplate;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly string _functionDescription;
        private readonly bool _hasFunctionNameEnvironment;
        private readonly bool _hasParameterExpressions;
        private readonly bool _isStrict;
        private readonly Dictionary<Symbol, bool> _lexicalDeclarationKinds;
        private readonly ImmutableArray<Symbol> _lexicalTemplate;
        private readonly JsValue _lexicalThis;
        private readonly JsValue _lexicalNewTarget;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly ImmutableArray<Symbol> _parameterNames;
        private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
        private readonly JsObject _properties = new();
        private readonly ImmutableArray<Symbol> _simpleCatchParameterTemplate;
        private readonly HashSet<Symbol> _topLevelLexicalNames;
        private readonly bool _usesArguments;
        private readonly int _functionScopeId;

        private readonly bool _wasAsyncFunction;
        private readonly FunctionExecutionPlanSeed _planSeed;

        // Precomputed fast path eligibility - combines all conditions except newTarget.IsUndefined
        // Updated when setters are called that could invalidate fast path
        private bool _canUseFastPathBase;
        private ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private IJsObjectLike? _homeObject;
        private ImmutableArray<ResolvedClassField> _instanceFields = ImmutableArray<ResolvedClassField>.Empty;
        private bool _isConstructorEnabled;
        private bool _isDerivedClassConstructor;
        private JsObject? _prototypeObject;
        private Parser.SourceReference? _sourceReferenceOverride;
        private IJsEnvironmentAwareCallable? _superConstructor;
        private IJsPropertyAccessor? _superPrototype;

        /// <summary>
        /// The function that most recently called this function (Annex B Function.caller).
        /// Set during invocation and cleared on exit.
        /// </summary>
        private IJsCallable? _currentCaller;

        internal SyncFunctionInvoker(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment = false,
            bool isConstructorFunction = true,
            FunctionExecutionPlanSeed planSeed = default)
        {
            // Initialize cached JsValue first (before any code that might reference 'this')
            _cachedJsValue = new JsValue(JsValueKind.Object, 0.0, this);

            if (function.IsGenerator)
            {
                throw new NotSupportedException(
                    "Generator functions should be created via the generator factory.");
            }

            _function = function;
            _closure = closure;
            RealmState = realmState;
            _properties.RealmState = RealmState;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            IsAsyncFunction = function.IsAsync;
            _wasAsyncFunction = function.WasAsync;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            IsArrowFunction = function.IsArrow;
            _isConstructorEnabled = isConstructorFunction;
            _planSeed = planSeed;
            var hoistPlan = ((IAstCacheable<HoistPlan>)function.Body).GetOrCreateCache();
            var bodyLexicalNames = hoistPlan.LexicalNames;
            var hasHoistableDeclarations = ((IAstCacheable<HoistableDeclarationsPlan>)function.Body)
                .GetOrCreateCache()
                .HasHoistableDeclarations;
            _hasParameterExpressions = _function.HasParameterExpressions();
            // Allow identifier caching only if the function body has no with/eval AND
            // the closure chain has no with environments (functions defined inside with blocks
            // need to check with bindings at runtime)
            _allowIdentifierCache = AllowsIdentifierCaching(_function) && !closure.HasWithObjectInChain();
            _usesArguments = !IsArrowFunction && UsesArgumentsIdentifier(_function);
            _functionScopeId = ResolveFunctionScopeId(function);

            // Detect simple functions for fast-path invocation
            // A simple function has: no async, no defaults, no destructuring, no body lexicals, no hoisting needed
            // Note: _hasFunctionNameEnvironment being true is fine - it just means the function name binding is
            // in an intermediate scope (for named function expressions), not in the invocation environment.
            // For non-strict mode: can use fast path if the function doesn't use 'arguments' identifier,
            // since mapped arguments object (which links argument values to parameter bindings) is not needed.
            var hasSimpleParams = HasOnlySimpleIdentifierParameters(function);
            var canUseFastPathForStrictness = _isStrict || !_usesArguments;
            var isSimpleFunction = canUseFastPathForStrictness &&
                                   !function.IsAsync &&
                                   !_wasAsyncFunction &&
                                   !_hasParameterExpressions &&
                                   hoistPlan.LexicalTemplate.Length == 0 &&
                                   !hasHoistableDeclarations &&
                                   _allowIdentifierCache &&
                                   hasSimpleParams;

            // Can pool invocation environment if simple function AND no inner functions that would capture it
            _canPoolInvocationEnvironment = isSimpleFunction &&
                                            !ContainsInnerFunctionExpression(function);

            // Cache the function description to avoid string allocation per call
            _functionDescription = function.Name is { } funcName ? $"function {funcName.Name}" : "anonymous function";

            var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)_function).GetOrCreateCache()
                .ParameterNames;
            _parameterNames = parameterNames;
            _lexicalTemplate = hoistPlan.LexicalTemplate;
            _lexicalDeclarationKinds = hoistPlan.LexicalDeclarationKinds;
            _topLevelLexicalNames = hoistPlan.TopLevelLexicalNames;
            _catchParameterTemplate = hoistPlan.CatchParameterTemplate;
            _simpleCatchParameterTemplate = hoistPlan.SimpleCatchParameterTemplate;
            _bodyLexicalTemplate = hoistPlan.BodyLexicalTemplate;

            // ES2024 9.2.12 FunctionDeclarationInstantiation steps 17-20:
            // argumentsObjectNeeded is true unless:
            // - Arrow function (step 18)
            // - "arguments" is a parameter name (step 19)
            // - hasParameterExpressions is false AND "arguments" is in functionNames/lexicalNames (step 20)
            // Note: If hasParameterExpressions is true, arguments object is needed even if body has "let arguments"
            var argumentsIsParameterName = _parameterNames.Contains(Symbol.Arguments);
            var argumentsInBodyLexicalNames = bodyLexicalNames.Contains(Symbol.Arguments) &&
                                              !hoistPlan.SimpleCatchParameterNames.Contains(Symbol.Arguments);
            var canSkipArgumentsForBodyDeclaration = !_hasParameterExpressions && argumentsInBodyLexicalNames;
            _argumentsObjectNeeded =
                !IsArrowFunction && !argumentsIsParameterName && !canSkipArgumentsForBodyDeclaration;

            if (IsArrowFunction)
            {
                // Use TryFindBindingJsValue with allowUninitialized=true to:
                // 1. Avoid throwing exceptions for uninitialized `this` in derived constructors
                // 2. Capture the environment that OWNS the `this` binding for super() calls
                if (_closure.TryFindBindingJsValue(Symbol.This, true, out var owningEnv, out var capturedThis))
                {
                    if (capturedThis.IsUninitialized)
                    {
                        // `this` exists but is uninitialized (derived constructor before super())
                        // Store the owning environment so super() can update the correct binding
                        _lexicalThis = JsValue.Uninitialized;
                        _lexicalThisEnvironment = owningEnv;
                    }
                    else
                    {
                        // `this` is initialized - capture its value
                        _lexicalThis = capturedThis;
                    }
                }
                else
                {
                    // No `this` binding in the environment chain
                    _lexicalThis = JsValue.Undefined;
                }

                // Capture new.target from the lexical scope for arrow functions.
                // Per ES spec, arrow functions inherit new.target from the enclosing function.
                if (_closure.TryFindBindingJsValue(Symbol.NewTarget, true, out _, out var capturedNewTarget))
                {
                    _lexicalNewTarget = capturedNewTarget;
                }
            }

            var paramCount = function.Parameters.GetExpectedParameterCount();
            var functionNameValue = _function.Name?.Name ?? string.Empty;
            if (IsAsyncLike)
            {
                var asyncProto = RealmState.AsyncFunctionPrototype;
                if (asyncProto is null)
                {
                    RealmState.AsyncFunctionConstructor ??= AsyncFunctionConstructor.CreateConstructor(RealmState);
                    asyncProto = RealmState.AsyncFunctionPrototype;
                }

                if (asyncProto is not null)
                {
                    _properties.SetPrototype(asyncProto);
                }
            }
            else if (RealmState.FunctionPrototype is not null)
            {
                _properties.SetPrototype(RealmState.FunctionPrototype);
            }

            // Functions expose a prototype objeßct, so instances created via `new` can inherit from it.
            // Async functions do NOT have a prototype property per ES spec 15.8.3 (MakeConstructor is not called).
            // We need to check both IsAsyncFunction and _wasAsyncFunction because the CPS transformer
            // transforms async functions to sync with WasAsync=true.
            if (!IsArrowFunction && !IsAsyncFunction && !_wasAsyncFunction && _isConstructorEnabled)
            {
                var functionPrototype = new JsObject();
                functionPrototype.RealmState = RealmState;
                functionPrototype.Origin = string.IsNullOrEmpty(functionNameValue)
                    ? "anonymous function prototype"
                    : $"prototype of {functionNameValue}";
                functionPrototype.SetPrototype(RealmState.ObjectPrototype);
                functionPrototype.DefinePropertyDirect("constructor",
                    new PropertyDescriptor
                    {
                        Value = this,
                        Writable = true,
                        Enumerable = false,
                        Configurable = true
                    });
                _properties.DefinePropertyDirect("prototype",
                    new PropertyDescriptor
                    {
                        Value = functionPrototype,
                        Writable = true,
                        Enumerable = false,
                        Configurable = false
                    });
            }

            _properties.DefinePropertyDirect("length",
                new PropertyDescriptor
                {
                    Value = (double)paramCount,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });

            _properties.DefinePropertyDirect("name",
                new PropertyDescriptor
                {
                    Value = functionNameValue,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });

            // Initialize precomputed fast path eligibility
            // At construction: _isClassConstructor=false, _capturedPrivateNameScopes=empty, PrivateNameScope=null,
            // _homeObject=null, _superConstructor=null, _superPrototype=null
            // So we only need to check _isSimpleFunction and _lexicalThisEnvironment
            //
            // IMPORTANT: Functions containing inner function expressions (closures) must use IR path.
            // The fast path uses _function.ScopeId for environments, while IR uses _plan.RootScopeId.
            // If parent uses fast path but child uses IR, scope IDs won't match and variable
            // lookup via scope chain will fail. By forcing parent to IR, we ensure consistent scope IDs.
            _canUseFastPathBase = isSimpleFunction && _lexicalThisEnvironment is null &&
                                  !ContainsInnerFunctionExpression(function);
        }

        public bool IsAsyncFunction { get; }

        /// <inheritdoc />
        public ref readonly JsValue AsJsValue => ref _cachedJsValue;

        internal bool IsClassConstructor { get; private set; }

        internal bool IsDerivedClassConstructor => IsClassConstructor && _isDerivedClassConstructor;

        public bool IsAsyncLike => IsAsyncFunction || _wasAsyncFunction;

        public PrivateNameScope? PrivateNameScope { get; private set; }

        // Async functions are never constructors per ES spec 15.8.3
        // Use IsAsyncLike to catch both IsAsyncFunction and _wasAsyncFunction (CPS-transformed async)
        public bool DisallowConstruct => !_isConstructorEnabled || IsAsyncLike;

        public bool IsArrowFunction { get; }

        internal bool HasHomeObject => _homeObject is not null;

        public RealmState RealmState { get; }
        public Parser.SourceReference? SourceReference => _sourceReferenceOverride ?? _function.Source;

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

            _properties.DefinePropertyDirect("name",
                new PropertyDescriptor
                {
                    JsValue = new JsValue(name),
                    Writable = false,
                    Enumerable = false,
                    Configurable = true
                });
        }

        internal void SetSourceReference(Parser.SourceReference? sourceReference)
        {
            _sourceReferenceOverride = sourceReference;
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
            // Annex B: Non-strict functions expose a "caller" property that returns
            // the function that most recently called this one. Must be checked before
            // _properties to avoid hitting the poison pill accessor on Function.prototype.
            // Strict functions rely on the poison pill (set up in FunctionPrototype).
            if (!_isStrict && !IsArrowFunction && string.Equals(name, "caller", StringComparison.Ordinal))
            {
                if (_currentCaller is null)
                {
                    value = JsValue.Null;
                }
                else if (_currentCaller is SyncFunctionInvoker { _isStrict: true })
                {
                    // Cross-strict boundary: caller is strict, so return null per spec.
                    value = JsValue.Null;
                }
                else if (_currentCaller is IAsJsValue asJsValue)
                {
                    value = asJsValue.AsJsValue;
                }
                else
                {
                    value = JsValue.FromObjectUnsafe(_currentCaller);
                }

                return true;
            }

            if (_properties.TryGetProperty(name, receiver.IsUndefined ? _cachedJsValue : receiver,
                    out value))
            {
                return true;
            }

            // Arrow functions, async functions, and non-constructor functions do not expose
            // a default "prototype" property, but an explicitly defined own property must still win.
            if (string.Equals(name, "prototype", StringComparison.Ordinal) &&
                (IsArrowFunction || IsAsyncFunction || _wasAsyncFunction || !_isConstructorEnabled))
            {
                value = JsValue.Undefined;
                return false;
            }

            // Provide minimal Function.prototype-style helpers for typed
            // functions so patterns like fn.call/apply/bind work for code
            // emitted by tools like Babel/regenerator.
            IJsCallable callable = this;
            switch (name)
            {
                case "call":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.SliceFrom(1);
                        return callable.Invoke(callArgs, thisArg);
                    }, isConstructor: false);
                    return true;

                case "apply":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var argList = ReflectHelper.CreateFunctionApplyArgumentList(args.GetArgument(1), RealmState);

                        return callable.Invoke(argList, thisArg);
                    }, isConstructor: false);
                    return true;

                case "bind":
                    var cachedThis = _cachedJsValue; // Capture to avoid closure over 'this'
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);
                        var targetIsConstructor = JsOps.IsConstructor(cachedThis);
                        return (JsValue)HostFunction.CreateBoundFunction(callable, boundThis, boundArgs,
                            targetIsConstructor,
                            RealmState);
                    }, isConstructor: false);
                    return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        public bool TryGetProperty(string name, out JsValue value)
        {
            return TryGetProperty(name, _cachedJsValue, out value);
        }

        public void SetProperty(string name, JsValue value)
        {
            SetProperty(name, value, _cachedJsValue);
        }

        public void SetProperty(string name, JsValue value, JsValue receiver)
        {
            _properties.SetProperty(name, value, receiver.IsUndefined ? _cachedJsValue : receiver);
        }

        PropertyDescriptor? IJsPropertyAccessor.GetOwnPropertyDescriptor(string name)
        {
            return _properties.GetOwnPropertyDescriptor(name);
        }

        IEnumerable<string> IJsPropertyAccessor.GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public IEnumerable<string> GetEnumerablePropertyNames()
        {
            return _properties.GetEnumerablePropertyNames();
        }

        public IEnumerable<string> GetOwnPropertyKeysInOrder(bool includeSymbols = true,
            bool includeNonEnumerable = true)
        {
            return _properties.GetOwnPropertyKeysInOrder(includeSymbols, includeNonEnumerable);
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

        IJsPropertyAccessor? IPrototypeAccessorProvider.PrototypeAccessor => _properties.PrototypeAccessor;

        /// <summary>
        /// Coerces 'this' value for non-strict mode function calls.
        /// In non-strict mode, primitives are boxed to objects and null/undefined become globalThis.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue CoerceThisValueForNonStrict(JsValue thisValue)
        {
            // Null/undefined → globalThis
            if (thisValue.IsNullish)
            {
                return RealmState.Engine is { GlobalObject: { } globalObj }
                    ? (JsValue)globalObj
                    : JsValue.Undefined;
            }

            // Primitives → boxed objects
            if (thisValue.IsNumber)
            {
                return JsValue.FromJsObject(NumberHelper.CreateNumberWrapper(thisValue.AsDouble(),
                    realm: RealmState));
            }

            if (thisValue.IsString)
            {
                return JsValue.FromJsObject(StringHelper.CreateStringWrapper(thisValue.AsString(),
                    realm: RealmState));
            }

            if (thisValue.IsBoolean)
            {
                return JsValue.FromJsObject(
                    BooleanHelper.CreateBooleanWrapper(thisValue.AsBoolean(), realm: RealmState));
            }

            if (thisValue.IsBigInt)
            {
                return JsValue.FromJsObject(BigIntHelper.CreateBigIntWrapper(thisValue.AsBigInt(),
                    realm: RealmState));
            }

            if (thisValue.IsSymbol && thisValue.TryUnwrap<JsSymbol>(out var typedSymbol))
            {
                return JsValue.FromJsObject(SymbolHelper.CreateSymbolWrapper(typedSymbol, realm: RealmState));
            }

            // Already an object
            return thisValue;
        }

        // Ensure constructor [[OwnPropertyKeys]] start with length/name/prototype as required by
        // ClassDefinitionEvaluation (ECMA-262 16.5.6.6, steps 31-33).
        internal void SeedIntrinsicConstructorKeys()
        {
            _properties.SeedIntrinsicConstructorKeys();
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext(
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget = default)
        {
            return InvokeWithContextSlow(arguments, thisValue, callingContext, newTarget);
        }

        /// <summary>
        /// Ultra-fast invoke for 1-argument calls - avoids array allocation.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext1(
            JsValue arg0,
            JsValue thisValue,
            EvaluationContext callingContext)
        {
            return InvokeWithContextSlow([arg0], thisValue, callingContext, JsValue.Undefined);
        }

        /// <summary>
        /// Ultra-fast invoke for 1-argument calls with environment reuse optimization.
        /// When reuseEnvironment is provided, the callee will reuse it instead of allocating a new one.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext1Reuse(
            JsValue arg0,
            JsValue thisValue,
            EvaluationContext callingContext,
            JsEnvironment reuseEnvironment)
        {
            _ = reuseEnvironment;

            return InvokeWithContextSlow([arg0], thisValue, callingContext, JsValue.Undefined);
        }

        /// <summary>
        /// Ultra-fast invoke for 2-argument calls - avoids array allocation.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext2(
            JsValue arg0,
            JsValue arg1,
            JsValue thisValue,
            EvaluationContext callingContext)
        {
            return InvokeWithContextSlow([arg0, arg1], thisValue, callingContext, JsValue.Undefined);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue InvokeWithContextSlow(
            IReadOnlyList<JsValue> arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget)
        {
            var context = RealmState.RentContext(pushScope: false);
            var constructErrorRealm = callingContext?.RealmState ?? RealmState;
            context.AllowIdentifierCache = _allowIdentifierCache;
            RealmState.Logger?.LogInformation(
                "InvokeWithContext enter func={Function} isAsync={IsAsync} wasAsync={WasAsync}",
                _function.Name?.Name ?? "<anonymous>",
                IsAsyncFunction,
                _wasAsyncFunction);
            if (RealmState.Logger is { } entryLogger && IsClassConstructor)
            {
                entryLogger.LogInformation(
                    "ctor entry func={Function} receiver={Receiver} newTarget={NewTarget}",
                    _function.Name?.Name ?? "<anonymous>",
                    DescribeValueJsValue(thisValue),
                    DescribeValueJsValue(newTarget));
            }

            if (RealmState.Logger is { } logger && _isStrict && !thisValue.IsUndefined)
            {
                logger.LogInformation("SyncFunctionInvoker strict received thisValue type={Type}",
                    thisValue.Kind);
            }

            if (callingContext is not null)
            {
                context.CallDepth = callingContext.CallDepth;
                context.MaxCallDepth = callingContext.MaxCallDepth;
            }

            // Track Function.caller (Annex B) for non-strict functions.
            // Save and restore the caller chain so recursive/nested calls work correctly.
            var previousCaller = _currentCaller;
            var previouslyExecuting = t_currentlyExecuting;
            var previousEvaluationContext = EvaluationContext.Current;
            _currentCaller = t_currentlyExecuting;
            t_currentlyExecuting = this;
            EvaluationContext.Current = context;
            try
            {

            if (IsClassConstructor && newTarget.IsUndefined)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    context,
                    RealmState);
                throw new ThrowSignal(error);
            }

            // Async functions use the generator IR executor for non-blocking await
            // This routes async functions through ExecutionPlanRunner with _asyncStepMode=true
            if (IsAsyncLike && !IsClassConstructor)
            {
                RealmState.ReturnContext(context);
                try
                {
                    RealmState.Logger?.LogInformation(
                        "[SyncFunctionInvoker] Routing async-like function {Function} to AsyncFunctionInvoker",
                        _function.Name?.Name ?? "<anonymous>");
                    var executor = new AsyncFunctionInvoker(
                        _function,
                        _closure,
                        arguments,
                        thisValue,
                        this,
                        RealmState,
                        _isStrict,
                        _hasFunctionNameEnvironment,
                        _homeObject,
                        PrivateNameScope,
                        _capturedPrivateNameScopes,
                        _planSeed);
                    return executor.Execute();
                }
                catch (ThrowSignal signal) when (callingContext is not null)
                {

                    callingContext.SetThrow(signal.ThrownValue);
                    return signal.ThrownValue;
                }
            }

            if (!_function.IsGenerator && !IsAsyncFunction && _planSeed.Failure is not null)
            {
                RealmState.ReturnContext(context);
                throw new NotSupportedException(
                    $"IR plan generation failed for function: {_planSeed.FailureReason}");
            }

            RealmState.Logger?.LogInformation(
                "[SyncFunctionInvoker.Invoke.ALL] _function.Hash={Hash} _allowIdentifierCache={AllowCache} _function.Name={Name}",
                _function.GetHashCode(),
                _allowIdentifierCache,
                _function.Name?.Name ?? "<anonymous>");

            ExecutionPlan? plan = null;
            string? failureReason = null;
            var usedCachedPlanSeed = false;
            if (!_function.IsGenerator && !IsAsyncFunction)
            {
                plan = _planSeed.Plan;
                failureReason = _planSeed.FailureReason;
                usedCachedPlanSeed = plan is not null || _planSeed.Failure is not null;
                if (!usedCachedPlanSeed)
                {
                    var planCache = ((IAstCacheable<ExecutionPlanCache>)_function).GetOrCreateCache();
                    plan = planCache.Plan;
                    failureReason = planCache.FailureReason;
                }
            }

            // Sync callables can use the IR runner whenever they have a lowered plan or an explicit
            // lowering failure to surface. That keeps captured with-closures on the no-slot IR path
            // and prevents silent re-entry into legacy AST execution.
            var canUseIrPlan =
                !_function.IsGenerator &&
                !IsAsyncFunction &&
                (_allowIdentifierCache || !_closure.HasWithObjectInChain() || plan is not null || failureReason is not null);
            if (canUseIrPlan)
            {
                RealmState.Logger?.LogInformation(
                    "[SyncFunctionInvoker.Invoke] _function.Hash={Hash} planSource={PlanSource} planSucceeded={Succeeded} plan.Hash={PlanHash}",
                    _function.GetHashCode(),
                    usedCachedPlanSeed ? "class-cache" : "function-cache",
                    plan is not null,
                    plan?.GetHashCode() ?? -1);
                if (plan is not null)
                {
                    // For arrow functions, use lexically captured this and new.target
                    var effectiveThisValue = thisValue;
                    var effectiveNewTarget = newTarget;
                    if (IsArrowFunction)
                    {
                        var lexicalThis = _lexicalThis;
                        if (_lexicalThisEnvironment is not null &&
                            _lexicalThisEnvironment.TryFindBindingJsValue(Symbol.This, true, out _, out var envThis))
                        {
                            lexicalThis = envThis;
                        }

                        effectiveThisValue = lexicalThis.IsUninitialized ? JsValue.Undefined : lexicalThis;

                        // Arrow functions inherit new.target from the enclosing function
                        if (effectiveNewTarget.IsUndefined && !_lexicalNewTarget.IsUndefined)
                        {
                            effectiveNewTarget = _lexicalNewTarget;
                        }
                    }

                    try
                    {
                        // For base class constructors, we need to initialize the instance before running
                        // the constructor body. This includes adding the private brand and initializing
                        // instance fields. For derived classes, initialization happens in the AST path
                        // when super() is called.
                        var needsInstanceInit = IsClassConstructor && !_isDerivedClassConstructor;
                        IJsObjectLike? instanceToInit = null;
                        var constructorThisValue = effectiveThisValue;

                        if (needsInstanceInit)
                        {
                            // For base class constructors called with `new`, create a new instance
                            // if thisValue is undefined (same logic as in the AST execution path)
                            if (!newTarget.IsUndefined && effectiveThisValue.IsUndefined)
                            {
                                var constructedThis = CreateConstructedThis(newTarget, RealmState);
                                constructorThisValue = JsValue.FromObjectUnsafe(constructedThis);
                                instanceToInit = constructedThis;
                            }
                            else if (effectiveThisValue.TryGetObject<IJsObjectLike>(out var existingInstance))
                            {
                                instanceToInit = existingInstance;
                            }
                        }

                        var runner = new ExecutionPlanRunner(
                            _function,
                            _closure,
                            arguments,
                            constructorThisValue,
                            this,
                            RealmState,
                            _isStrict,
                            _hasFunctionNameEnvironment,
                            _homeObject,
                            PrivateNameScope,
                            _capturedPrivateNameScopes,
                            effectiveNewTarget,
                            _lexicalThisEnvironment,
                            _superConstructor,
                            _superPrototype,
                            context,
                            derivedClassErrorRealm: constructErrorRealm,
                            planOverride: plan,
                            planFailureOverride: _planSeed.Failure);

                        var runnerContext = runner.EnsureEvaluationContext();

                        if (IsClassConstructor && _isDerivedClassConstructor)
                        {
                            var pendingFieldInitialization = new PendingClassFieldInitialization(
                                this,
                                runner.GetOrCreateExecutionEnvironmentForInternalUse());
                            runnerContext.PushClassFieldInitializer(pendingFieldInitialization);
                        }

                        // Initialize instance BEFORE running constructor body (adds private brand and initializes fields)
                        if (instanceToInit is not null)
                        {
                            var initContext = runnerContext;
                            var initEnv = JsEnvironment.CreateInstance(_closure, isStrict: _isStrict);
                            InitializeInstance(instanceToInit, initEnv, initContext);
                            if (initContext.IsThrow)
                            {
                                callingContext?.SetThrow(initContext.FlowValue);
                                return initContext.FlowValue;
                            }
                        }

                        try
                        {
                            return runner.RunSync();
                        }
                        catch (ThrowSignal signal) when (callingContext is not null && IsClassConstructor && _isDerivedClassConstructor)
                        {
                            var normalized = NormalizeDerivedClassRealmError(signal, callingContext);
                            if (!normalized.IsUndefined)
                            {
                                callingContext.SetThrow(normalized);
                                return normalized;
                            }

                            callingContext.SetThrow(signal.ThrownValue);
                            return signal.ThrownValue;
                        }
                    }
                    catch (ThrowSignal signal) when (callingContext is not null)
                    {
                        callingContext.SetThrow(signal.ThrownValue);
                        return signal.ThrownValue;
                    }
                    finally
                    {
                        RealmState.ReturnContext(context);
                    }
                }

                RealmState.ReturnContext(context);
                throw new NotSupportedException(
                    $"IR plan generation failed for function: {failureReason}");
            }

            if (!_function.IsGenerator && !IsAsyncFunction)
            {
                RealmState.Logger?.LogInformation(
                    "Executing sync function via dynamic-scope executor func={Function}",
                    _function.Name?.Name ?? "<anonymous>");
            }

            var lexicalNames = RentSymbolSet(_lexicalTemplate);
            // Used to compute body-lexical blocking; cleared and reused as an "active catch" set for hoisting.
            var simpleCatchParameterNames = RentSymbolSet(_simpleCatchParameterTemplate);
            // Track active catch parameters while hoisting (Annex B.3.5/B.3.3.3); start empty.
            var catchParameterNames = RentSymbolSet();
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : RentSymbolSet(_bodyLexicalTemplate);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);
            simpleCatchParameterNames.Clear();

            var functionMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var functionScopeFrame = context.PushScope(ScopeKind.Function, functionMode);

            // When parameter expressions are present, keep the parameter environment outside
            // the var environment so defaults cannot observe body var bindings (spec step 27).
            JsEnvironment parameterEnvironment;
            JsEnvironment functionEnvironment;
            JsEnvironment varEnvironment;
            if (_hasParameterExpressions)
            {
                functionEnvironment = JsEnvironment.CreateInstance(_closure, true, _isStrict, _function.Source,
                    _functionDescription);
                functionEnvironment.IsArrowFunctionEnvironment = IsArrowFunction;
                // Don't initialize slots for complex parameter expressions (destructuring, defaults)
                // Values are bound via dictionary, not slots - only set scope metadata
                functionEnvironment.ScopeId = _function.ScopeId;
                functionEnvironment.SetSlotMap(_function.SlotMap);

                parameterEnvironment = JsEnvironment.CreateInstance(functionEnvironment, false, _isStrict, _function.Source,
                    _functionDescription, isParameterEnvironment: true);
                parameterEnvironment.IsArrowFunctionEnvironment = IsArrowFunction;

                varEnvironment = JsEnvironment.CreateInstance(parameterEnvironment, true, _isStrict, _function.Source,
                    _functionDescription);
                varEnvironment.IsArrowFunctionEnvironment = IsArrowFunction;
            }
            else
            {
                functionEnvironment = JsEnvironment.CreateInstance(_closure, true, _isStrict, _function.Source,
                    _functionDescription);
                functionEnvironment.IsArrowFunctionEnvironment = IsArrowFunction;
                // InvokeWithContext uses dictionary-based lookups (slow path).
                // Set ScopeId for scope chain navigation but DON'T initialize slots.
                // Function declarations are hoisted into the dictionary via DefineFunctionScoped,
                // and initializing slots would shadow them with Undefined values.
                functionEnvironment.ScopeId = _function.ScopeId;
                functionEnvironment.SetSlotMap(_function.SlotMap);
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = JsEnvironment.CreateInstance(varEnvironment, false, _isStrict,
                _function.Source, _functionDescription, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // Per Annex B.3.3.1: compute names blocked from AnnexB function-scope hoisting.
            // These are body-level lexical names, parameter names, and non-simple catch params.
            {
                var hoistPlanForBlocked = ((IAstCacheable<HoistPlan>)_function.Body).GetOrCreateCache();
                var catchNamesForBlocked = hoistPlanForBlocked.CatchParameterNames;
                var simpleCatchNamesForBlocked = hoistPlanForBlocked.SimpleCatchParameterNames;
                if (!_isStrict && (bodyLexicalNames.Count > 0 || _parameterNames.Length > 0 ||
                                   catchNamesForBlocked.Count > 0 || _argumentsObjectNeeded))
                {
                    var blockedNames =
                        new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
                    foreach (var pn in _parameterNames)
                    {
                        blockedNames.Add(pn);
                    }

                    // B.3.5: non-simple catch parameters (destructured) block AnnexB hoisting
                    foreach (var cn in catchNamesForBlocked)
                    {
                        if (!simpleCatchNamesForBlocked.Contains(cn))
                        {
                            blockedNames.Add(cn);
                        }
                    }

                    // Per spec step 22.f: when argumentsObjectNeeded, "arguments" blocks AnnexB
                    if (_argumentsObjectNeeded)
                    {
                        blockedNames.Add(Symbol.Arguments);
                    }

                    if (blockedNames.Count > 0)
                    {
                        varEnvironment.SetAnnexBBlockedNames(blockedNames);
                    }
                }
            }

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
            var hasPendingFieldInitialization = false;

                if (!IsArrowFunction)
                {
                    var newTargetValue = newTarget.IsUndefined ? JsValue.Undefined : newTarget;
                    functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    functionEnvironment.DefineJsValue(Symbol.ActiveFunction, _cachedJsValue, true,
                        isLexicalBinding: true, blocksFunctionScopeOverride: true);
                }

            // Bind `this`.
            if (IsArrowFunction)
            {
                var lexicalThis = _lexicalThis;
                var lexicalThisInitialized = !lexicalThis.IsUninitialized;
                if (_lexicalThisEnvironment is not null)
                {
                    // Try to get the this binding from the lexical environment, allowing uninitialized
                    if (_lexicalThisEnvironment.TryFindBindingJsValue(Symbol.This, true, out _,
                            out var envThis))
                    {
                        lexicalThis = envThis;
                        lexicalThisInitialized = !lexicalThis.IsUninitialized;
                    }
                    else
                    {
                        // Binding not found - treat as uninitialized
                        lexicalThis = JsValue.Uninitialized;
                        lexicalThisInitialized = false;
                    }
                }

                var boundThis = lexicalThisInitialized ? lexicalThis : JsValue.Undefined;
                if (lexicalThisInitialized)
                {
                    context.MarkThisInitialized();
                }
                else
                {
                    context.MarkThisUninitialized();
                }

                functionEnvironment._thisValue = boundThis;
                functionEnvironment._hasThisValue = true;
                functionEnvironment.DefineJsValue(Symbol.This, boundThis);

                // Store a reference to the original environment that owns the `this` binding.
                // This is needed for super() calls in arrow functions - super() must update
                // the original constructor's `this` binding, not the arrow function's local copy.
                if (_lexicalThisEnvironment is not null &&
                    _lexicalThisEnvironment.TryFindBindingJsValue(Symbol.This, true,
                        out var originalThisEnv, out _))
                {
                    functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                        JsValue.FromObjectUnsafe(originalThisEnv));
                }

                var hasCopiedInitialization = false;
                if (_closure.TryGetJsValue(Symbol.ThisInitialized, out var closureThisInitialized))
                {
                    functionEnvironment.SetThisInitializationStatus(JsOps.ToBoolean(closureThisInitialized));
                    hasCopiedInitialization = true;
                }
                else if (_closure.TryGetObject<SuperBinding>(Symbol.Super, out var closureSuperBinding))
                {
                    functionEnvironment.SetThisInitializationStatus(closureSuperBinding.IsThisInitialized);
                    hasCopiedInitialization = true;
                }

                SuperBinding? lexicalSuperBinding = null;
                if (_superConstructor is not null || _superPrototype is not null)
                {
                    lexicalSuperBinding = new SuperBinding(
                        _superConstructor,
                        _superPrototype,
                        boundThis,
                        lexicalThisInitialized);
                }
                else if (_closure.TryGetObject<SuperBinding>(Symbol.Super, out var inheritedSuperBinding))
                {
                    lexicalSuperBinding = new SuperBinding(
                        inheritedSuperBinding.Constructor,
                        inheritedSuperBinding.Prototype,
                        boundThis,
                        lexicalThisInitialized);
                }

                if (lexicalSuperBinding is not null)
                {
                    functionEnvironment.RealmState?.Logger?.LogInformation(
                        "SuperBinding: define lexical for arrow/lexical this protoNull={ProtoNull} thisInit={ThisInit}",
                        lexicalSuperBinding.Prototype is null,
                        lexicalSuperBinding.IsThisInitialized);
                    functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(lexicalSuperBinding),
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    if (!hasCopiedInitialization)
                    {
                        functionEnvironment.SetThisInitializationStatus(lexicalSuperBinding.IsThisInitialized);
                    }
                }
            }
            else
            {
                // Non-arrow function - use object? for boundThis due to pattern matching needs
                // Inlined ToObject() conversion to avoid obsolete warning
                var boundThis = thisValue.Kind switch
                {
                    JsValueKind.Undefined => Symbol.Undefined,
                    JsValueKind.Null => null,
                    JsValueKind.Boolean => JsValueCache.GetBoolean(thisValue.NumberValue != 0.0),
                    JsValueKind.Number => JsValueCache.GetNumber(thisValue.NumberValue),
                    JsValueKind.Uninitialized => JsEnvironment.Uninitialized,
                    _ => thisValue.ObjectValue
                };

                if (IsClassConstructor &&
                    ReferenceEquals(boundThis, Symbol.Undefined) &&
                    !newTarget.IsUndefined)
                {
                    var constructedThis = CreateConstructedThis(newTarget, RealmState);

                    RealmState.Logger?.LogInformation(
                        "ctor: synthesized receiver func={Function} receiver={Receiver} proto={Proto} newTargetKind={NewTargetKind}",
                        _function.Name?.Name ?? "<anonymous>",
                        DescribeValue(constructedThis),
                        DescribePrototype(constructedThis.PrototypeAccessor ?? constructedThis.Prototype),
                        newTarget.Kind);

                    boundThis = constructedThis;
                }

                if (!_isStrict)
                {
                    if (thisValue.IsNullish)
                    {
                        boundThis = RealmState.Engine is { GlobalObject: { } globalObj }
                            ? globalObj
                            : Symbol.Undefined;
                    }

                    if (boundThis is not IJsPropertyAccessor &&
                        boundThis is not null && !ReferenceEquals(boundThis, Symbol.Undefined) &&
                        boundThis is not IIsHtmlDda)
                    {
                        boundThis = ToObjectForDestructuringJsValue(JsValue.FromObjectUnsafe(boundThis), context);
                    }
                }

                object? initialThisValue;
                bool initialThisInitialized;
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
                        initialThisValue = new JsObject { RealmState = RealmState };
                    }

                    boundThis = initialThisValue;
                }

                functionEnvironment.SetThisInitializationStatus(initialThisInitialized);
                // In strict mode, `this` can be undefined - handle Symbol.Undefined marker correctly
                var thisJsValue = ReferenceEquals(initialThisValue, Symbol.Undefined)
                    ? JsValue.Undefined
                    : JsValue.FromObjectUnsafe(initialThisValue);
                functionEnvironment._thisValue = thisJsValue;
                functionEnvironment._hasThisValue = true;
                functionEnvironment.DefineJsValue(Symbol.This, thisJsValue);

                if (IsClassConstructor && initialThisValue is JsObject ctorThis)
                {
                    RealmState.Logger?.LogInformation(
                        "ctor: bound this func={Function} this={This} proto={Proto} initialized={Initialized}",
                        _function.Name?.Name ?? "<anonymous>",
                        DescribeValue(ctorThis),
                        DescribePrototype(ctorThis.PrototypeAccessor ?? ctorThis.Prototype),
                        initialThisInitialized);
                }

                IJsPropertyAccessor? prototypeForSuper;
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

                var shouldDefineSuperBinding = IsClassConstructor ||
                                              _homeObject is not null ||
                                              _superConstructor is not null ||
                                              prototypeForSuper is not null;
                if (shouldDefineSuperBinding)
                {
                    var runtimeSuperConstructor = _superConstructor;
                    if (IsClassConstructor)
                    {
                        var runtimeCtorPrototype =
                            (this as IPrototypeAccessorProvider).PrototypeAccessor ?? Prototype;
                        if (runtimeCtorPrototype is IJsEnvironmentAwareCallable ctorLike)
                        {
                            runtimeSuperConstructor = ctorLike;
                        }
                    }

                    var thisForSuper = initialThisInitialized &&
                                       boundThis is not null &&
                                       !ReferenceEquals(boundThis, JsEnvironment.Uninitialized)
                        ? JsValue.FromObjectUnsafe(boundThis)
                        : JsValue.Undefined;
                    var binding = new SuperBinding(runtimeSuperConstructor, prototypeForSuper,
                        thisForSuper, initialThisInitialized);
                    functionEnvironment.RealmState?.Logger?.LogInformation(
                        "SuperBinding: define in function env env={Env} isCtor={IsCtor} isDerivedCtor={IsDerivedCtor} protoNull={ProtoNull} thisInit={ThisInit}",
                        functionEnvironment.GetHashCode(),
                        IsClassConstructor,
                        _isDerivedClassConstructor,
                        prototypeForSuper is null,
                        initialThisInitialized);
                    functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(binding));
                }

                if (IsClassConstructor)
                {
                    if (_isDerivedClassConstructor)
                    {
                        var pendingFieldInitialization = new PendingClassFieldInitialization(this, functionEnvironment);
                        context.PushClassFieldInitializer(pendingFieldInitialization);
                        hasPendingFieldInitialization = true;
                    }
                    else if (boundThis is JsObject thisInstance)
                    {
                        InitializeInstance(thisInstance, functionEnvironment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (!context.IsThrow)
                            {
                                return JsValue.Undefined;
                            }

                            var thrownDuringInitialization = context.FlowValue;
                            callingContext?.SetThrow(thrownDuringInitialization);
                            return thrownDuringInitialization;
                        }
                    }
                }
            }

            try
            {
                // Create arguments object per ES2024 9.2.12 steps 17-20
                // Note: argumentsObjectNeeded handles all spec conditions (arrow, param name, lexical binding)
                if (_argumentsObjectNeeded)
                {
                    // Create the `arguments` binding up front so parameter default expressions can reference it.
                    var argumentsObject = _function.CreateArgumentsObject(arguments, executionEnvironment, RealmState,
                        this,
                        _isStrict);
                    parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                        isLexicalBinding: false);
                    if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
                    {
                        functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                            isLexicalBinding: false);
                    }
                }

                // Named function expressions should see their name inside the body.
                if (!IsArrowFunction && _function.Name is { } functionName && !_hasFunctionNameEnvironment)
                {
                    parameterEnvironment.DefineJsValue(functionName, _cachedJsValue, true,
                        isLexicalBinding: true, blocksFunctionScopeOverride: true);
                }

                // Wrap parameter binding and body evaluation in the same try-catch for async functions.
                // This ensures ThrowSignal exceptions from TDZ errors during parameter default evaluation
                // are properly caught and converted to rejected promises.
                try
                {
                    // Bind parameters
                    _function.BindFunctionParameters(arguments, parameterEnvironment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (!context.IsThrow)
                        {
                            return JsValue.Undefined;
                        }

                        var thrownDuringBinding = context.FlowValue;
                        if (IsAsyncFunction || _wasAsyncFunction)
                        {
                            // Async functions must reject instead of throwing synchronously.
                            // Use CreateRejectedPromiseFromRealm, which uses the RealmState's
                            // PromiseConstructor, ensuring we always have access to Promise.
                            callingContext?.Clear();

                            var rejectedBindingResult = CreateRejectedPromiseFromRealm(thrownDuringBinding);
                            return rejectedBindingResult;
                        }

                        if (callingContext is null)
                        {
                            throw new ThrowSignal(thrownDuringBinding);
                        }

                        callingContext.SetThrow(thrownDuringBinding);
                        return thrownDuringBinding;
                    }

                    _function.Body.HoistVarDeclarations(executionEnvironment, context,
                        lexicalNames: lexicalNames,
                        catchParameterNames: catchParameterNames,
                        simpleCatchParameterNames: simpleCatchParameterNames);

                    if (_hasFunctionNameEnvironment &&
                        _function.Name is { } hoistedName &&
                        ContainsVarDeclaration(_function, hoistedName) &&
                        !functionEnvironment.HasBinding(hoistedName))
                    {
                        functionEnvironment.DefineFunctionScoped(hoistedName, JsValue.Undefined, false,
                            context: context);
                    }

                    // ES2024 9.2.12 FunctionDeclarationInstantiation step 34-35:
                    // Create TDZ bindings for lexical declarations (let/const) in the function environment.
                    // This must happen BEFORE the body is evaluated so that closures that reference these
                    // variables will find them in TDZ state and throw ReferenceError if accessed before initialization.
                    // NOTE: We use _topLevelLexicalNames which excludes for-loop/for-of initializer variables
                    // (those create their own per-iteration environments and should NOT be in function TDZ).
                    foreach (var lexicalName in _topLevelLexicalNames)
                    {
                        if (!executionEnvironment.HasBinding(lexicalName))
                        {
                            var isConst = _lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                            executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isConst: isConst,
isLexicalBinding: true, blocksFunctionScopeOverride: true);
                        }
                    }

                    var previousEnvironment = JsEnvironment.Current;
                    JsEnvironment.Current = executionEnvironment;
                    try
                    {
                        _ = _function.Body.EvaluateBlockJsValue(executionEnvironment,
                            context);
                    }
                    finally
                    {
                        JsEnvironment.Current = previousEnvironment;
                    }

                    if (context.IsThrow)
                    {
                        var thrown = context.FlowValue;
                        RealmState.Logger?.LogInformation(
                            "InvokeWithContext propagating throw kind={ThrowKind} callerHasContext={HasCaller} func={FunctionName}",
                            thrown.Kind,
                            callingContext is not null,
                            _function.Name?.Name ?? "<anonymous>");

                        if (IsAsyncFunction || _wasAsyncFunction)
                        {
                            var rejectedThrowResult = CreateRejectedPromise(thrown, executionEnvironment);
                            return rejectedThrowResult;
                        }

                        if (callingContext is null)
                        {
                            throw new ThrowSignal(thrown);
                        }

                        callingContext.SetThrow(thrown);
                        return thrown;
                    }

                    // Use IsAsyncLike so CPS-transformed async functions (WasAsync=true, IsAsync=false)
                    // still wrap completion values in a promise.
                    if (!IsAsyncLike)
                    {
                        if (!context.IsReturn)
                        {
                            if (!IsClassConstructor)
                            {
                                return JsValue.Undefined;
                            }

                            try
                            {
                                if (functionEnvironment.TryGetJsValue(Symbol.This, out var currentThis))
                                {
                                    RealmState.Logger?.LogInformation(
                                        "Class constructor returning this={This}",
                                        DescribeValue(currentThis.ObjectValue));
                                    return currentThis;
                                }
                            }
                            catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                                                           "ReferenceError: this",
                                                                           StringComparison.Ordinal))
                            {
                                // If `this` is uninitialized (e.g., derived ctor without super()), surface a JS ReferenceError.
                                var errorObject =
                                    StandardLibrary.CreateReferenceError(ex.Message, context, constructErrorRealm);
                                throw new ThrowSignal(errorObject);
                            }
                            catch (ThrowSignal signal) when (_isDerivedClassConstructor &&
                                                            signal.Message.Contains("ReferenceError",
                                                                StringComparison.Ordinal))
                            {
                                var errorObject = StandardLibrary.CreateReferenceError(
                                    "ReferenceError: this is not defined - must call super() in derived class constructor",
                                    context,
                                    constructErrorRealm);
                                throw new ThrowSignal(errorObject);
                            }

                            return JsValue.Undefined;
                        }

                        var value = context.FlowValue;
                        context.ClearReturn();
                        if (IsClassConstructor &&
                            !value.TryGetObject<JsObject>(out _) &&
                            !value.TryGetObject<IJsObjectLike>(out _))
                        {
                            // Per ES spec 9.2.2 [[Construct]] step 13c:
                            // For derived class constructors, if return value is not undefined,
                            // throw TypeError. For base class constructors, fall back to `this`.
                            if (_isDerivedClassConstructor && !value.IsUndefined)
                            {
                                throw StandardLibrary.ThrowTypeError(
                                    "Derived constructors may only return object or undefined",
                                    context,
                                    constructErrorRealm);
                            }

                            try
                            {
                                if (functionEnvironment.TryGetJsValue(Symbol.This, out var currentThisValue) &&
                                    currentThisValue.ObjectValue is not null &&
                                    !ReferenceEquals(currentThisValue.ObjectValue, JsEnvironment.Uninitialized))
                                {
                                    RealmState.Logger?.LogInformation(
                                        "Class constructor returning bound this instead of non-object return value");
                                    return currentThisValue;
                                }

                                // Per ES spec 9.2.2 [[Construct]] step 15:
                                // If return value is undefined, call GetThisBinding() which
                                // throws ReferenceError if `this` is uninitialized (super() not called)
                                if (_isDerivedClassConstructor &&
                                    (ReferenceEquals(currentThisValue.ObjectValue, JsEnvironment.Uninitialized) ||
                                     value.IsUndefined))
                                {
                                    var errorObject = StandardLibrary.CreateReferenceError(
                                        "ReferenceError: this is not defined - must call super() in derived class constructor",
                                        context,
                                        constructErrorRealm);
                                    throw new ThrowSignal(errorObject);
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
                                    var errorObject =
                                        StandardLibrary.CreateReferenceError(ex.Message, context, constructErrorRealm);
                                    throw new ThrowSignal(errorObject);
                                }

                                RealmState.Logger?.LogInformation(
                                    "Class constructor missing initialized this; falling back to return value reason={Reason}",
                                    ex.Message);
                            }
                            catch (ThrowSignal signal) when (_isDerivedClassConstructor &&
                                                            signal.Message.Contains("ReferenceError",
                                                                StringComparison.Ordinal))
                            {
                                var errorObject = StandardLibrary.CreateReferenceError(
                                    "ReferenceError: this is not defined - must call super() in derived class constructor",
                                    context,
                                    constructErrorRealm);
                                throw new ThrowSignal(errorObject);
                            }
                        }

                        return value;
                    }

                    var completionValue = JsValue.Undefined;
                    if (context.IsReturn)
                    {
                        completionValue = context.FlowValue;
                        context.ClearReturn();
                    }

                    RealmState.Logger?.LogInformation(
                        "Async completion func={Function} isAsync={IsAsync} wasAsync={WasAsync} completionKind={Kind}",
                        _function.Name?.Name ?? "<anonymous>",
                        IsAsyncFunction,
                        _wasAsyncFunction,
                        completionValue.Kind);
                    System.Console.WriteLine(
                        $"[SyncFunctionInvoker] Async completion func={_function.Name?.Name ?? "<anonymous>"} isAsync={IsAsyncFunction} wasAsync={_wasAsyncFunction} completionKind={completionValue.Kind} shouldStop={context.ShouldStopEvaluation} isReturn={context.IsReturn} isThrow={context.IsThrow}");
                    var resolvedResult = CreateResolvedPromise(completionValue, executionEnvironment);
                    return resolvedResult;
                }
                catch (ThrowSignal signal) when (IsAsyncFunction || _wasAsyncFunction)
                {
                    // Use CreateRejectedPromiseFromRealm which uses the RealmState's PromiseConstructor
                    // directly, avoiding environment lookup that might fail during parameter binding.
                    var rejectedResult = CreateRejectedPromiseFromRealm(signal.ThrownValue);
                    return rejectedResult;
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

                RealmState.ReturnContext(context);
            }
            }
            finally
            {
                // Restore Function.caller tracking (Annex B).
                EvaluationContext.Current = previousEvaluationContext;
                _currentCaller = previousCaller;
                t_currentlyExecuting = previouslyExecuting;
            }
        }

        private static HashSet<Symbol> RentSymbolSet()
        {
            return SymbolSetPool.Rent();
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
            SymbolSetPool.Return(set);
        }

        /// <summary>
        /// Creates a rejected promise using the realm's Promise constructor.
        /// Unlike CreateRejectedPromise which looks up Promise in the environment,
        /// this method uses the RealmState's PromiseConstructor directly.
        /// </summary>
        private JsValue CreateRejectedPromiseFromRealm(JsValue reason)
        {
            var promiseCtor = RealmState.PromiseConstructor;
            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("reject", out var rejectValue) &&
                rejectValue.TryGetObject<IJsCallable>(out var rejectCallable))
            {
                return rejectCallable.Invoke(new SingleValueArgs(reason), JsValue.FromObjectUnsafe(promiseCtor));
            }

            // Fallback if Promise.reject isn't available - return the reason directly
            // This shouldn't happen in normal operation since Promise is always registered
            return reason;
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
            IsClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
            _canUseFastPathBase = false;
        }

        internal void SetInstanceFields(ImmutableArray<ResolvedClassField> fields)
        {
            _instanceFields = fields;
        }

        /// <summary>
        /// Tries to get the current prototype value, which could be any object-like value
        /// (JsObject, SyncFunctionInvoker, HostFunction, etc). Returns true if a valid prototype exists.
        /// </summary>
        internal bool TryGetPrototypeValue(out IJsObjectLike? prototype)
        {
            // Always check the current prototype property value first, in case it was reassigned
            // (e.g., FooObj.prototype = anotherFunction). Per ES spec, if the prototype property
            // is not an object, we should use the intrinsic %Object.prototype% instead, but
            // this is handled at the call site.
            if (_properties.TryGetProperty("prototype", _cachedJsValue, out var value) &&
                value.TryGetObject<IJsObjectLike>(out var objLike))
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
            if (_properties.TryGetProperty("prototype", _cachedJsValue, out var value) &&
                value.TryGetObject<JsObject>(out var jsObj))
            {
                _prototypeObject = jsObj;
                return jsObj;
            }

            // Return cached value if we previously created one
            if (_prototypeObject is not null)
            {
                return _prototypeObject;
            }

            var created = new JsObject(RealmState.ObjectPrototype)
            {
                RealmState = RealmState,
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
            if (constructorEnvironment.TryGetObject<SuperBinding>(Symbol.Super, out var binding))
            {
                return binding;
            }

            var prototypeForSuper = _superPrototype ?? instance.Prototype?.Prototype;

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

            foreach (var resolvedField in _instanceFields)
            {
                if (resolvedField.IsPrivate && PrivateNameScope is not null && instance is not IPrivateBrandHolder)
                {
                    throw StandardLibrary.ThrowTypeError("Invalid private field receiver", context, context.RealmState);
                }

                using var classFieldInitScope = context.EnterClassFieldInitializer();
                var initEnv = JsEnvironment.CreateInstance(environment, isStrict: true);
                initEnv.DefineJsValue(EvalHostFunction.FieldInitializerEvalFlag, JsValue.True, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                initEnv.DefineJsValue(Symbol.This, JsValue.FromObjectUnsafe(instance));

                var fieldSuperBinding = ResolveInstanceFieldSuperBinding(environment, instance);
                if (fieldSuperBinding is not null)
                {
                    initEnv.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(fieldSuperBinding), true,
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }

                //TODO: does this do a double lookup for Symbol.NewTarget ?
                if (environment.HasBinding(Symbol.NewTarget))
                {
                    // Class field initializers execute outside of any function body; shadow new.target with undefined.
                    initEnv.DefineJsValue(Symbol.NewTarget, JsValue.Undefined, true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGetJsValue(Symbol.Arguments, out var argumentsValue))
                {
                    initEnv.DefineJsValue(Symbol.Arguments, argumentsValue, isLexicalBinding: false);
                }

                var propertyName = resolvedField.Name;

                context.RealmState.Logger?.LogInformation(
                    "Initializing instance field '{PropertyName}' (private={IsPrivate})",
                    propertyName,
                    resolvedField.IsPrivate);

                var valueJs = JsValue.Undefined;
                if (resolvedField.InitializerProgram is { } initializerProgram)
                {
                    valueJs = EvaluateLoweredExpressionProgram(
                        initializerProgram,
                        initEnv,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    if (valueJs.ObjectValue is SyncFunctionInvoker { IsArrowFunction: true } typedFunction &&
                        fieldSuperBinding is not null)
                    {
                        typedFunction.SetSuperBinding(fieldSuperBinding.Constructor, fieldSuperBinding.Prototype);
                    }

                    if (resolvedField.AnonymousFunctionName is { } displayName)
                    {
                        SetAnonymousFunctionName(valueJs, displayName);
                    }
                }

                context.RealmState.Logger?.LogInformation(
                    "InitInstance: ctor={Ctor} instance={Instance} field={Field} valueKind={ValueKind}",
                    _function.Name?.Name ?? "<anonymous>",
                    DescribeValue(instance),
                    propertyName,
                    valueJs.Kind);

                var descriptor = new PropertyDescriptor
                {
                    JsValue = valueJs,
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
                else
                {
                    instance.DefineProperty(propertyName, descriptor);
                }
            }

            context.RealmState.Logger?.LogInformation(
                "InitInstance complete: ctor={Ctor} instance={Instance} keys={Keys}",
                _function.Name?.Name ?? "<anonymous>",
                DescribeValue(instance),
                string.Join(',', instance.GetOwnPropertyKeysInOrder().Select(static k => k)));
        }

        private static string DescribePrototype(object? proto)
        {
            switch (proto)
            {
                case null:
                    return "null";
                case JsObject jsObj:
                    {
                        var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
                        return
                            $"JsObject@{RuntimeHelpers.GetHashCode(jsObj).ToString(CultureInfo.InvariantCulture)} origin='{origin}'";
                    }
                default:
                    return
                        $"{proto.GetType().Name}@{RuntimeHelpers.GetHashCode(proto).ToString(CultureInfo.InvariantCulture)}";
            }
        }

        private static string DescribeValueJsValue(JsValue value)
        {
            if (value is { Kind: JsValueKind.Object, ObjectValue: JsObject jsObj })
            {
                var proto = jsObj.PrototypeAccessor ?? jsObj.Prototype;
                var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
                return
                    $"JsObject@{RuntimeHelpers.GetHashCode(jsObj)} origin='{origin}' proto={DescribePrototype(proto)}";
            }

            if (value.IsNull)
            {
                return "null";
            }

            if (value.IsUndefined)
            {
                return "undefined";
            }

            return $"{value.Kind}";
        }

        private static string DescribeValue(object? value)
        {
            switch (value)
            {
                case JsObject jsObj:
                    {
                        var proto = jsObj.PrototypeAccessor ?? jsObj.Prototype;
                        var origin = string.IsNullOrEmpty(jsObj.Origin) ? "unknown" : jsObj.Origin;
                        return
                            $"JsObject@{RuntimeHelpers.GetHashCode(jsObj).ToString(CultureInfo.InvariantCulture)} origin='{origin}' proto={DescribePrototype(proto)}";
                    }
                case null:
                    return "null";
                default:
                    return $"{value.GetType().Name}@{RuntimeHelpers.GetHashCode(value)}";
            }
        }

        private static bool ContainsVarDeclaration(FunctionExpression function, Symbol name)
        {
            return VarDeclarationDetector.ContainsVarDeclaration(function.Body, name);
        }

        /// <summary>
        /// Checks if a function has only simple identifier parameters (no destructuring, no rest, no defaults).
        /// </summary>
        private static int ResolveFunctionScopeId(FunctionExpression function)
        {
            if (function.ScopeId > 0)
            {
                return function.ScopeId;
            }

            var planCache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (planCache.Plan is { RootScopeId: > 0 } plan)
            {
                return plan.RootScopeId;
            }

            return function.ScopeId;
        }

        private static bool HasOnlySimpleIdentifierParameters(FunctionExpression function)
        {
            HashSet<Symbol>? seenNames = null;
            for (var i = 0; i < function.Parameters.Length; i++)
            {
                var param = function.Parameters[i];
                if (param.Name is null || param.Pattern is not null || param.DefaultValue is not null || param.IsRest)
                {
                    return false;
                }

                seenNames ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                if (!seenNames.Add(param.Name))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Fast-path invocation for simple functions. Uses pooled EvaluationContext.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFast(IReadOnlyList<JsValue> arguments, JsValue thisValue,
            EvaluationContext? callingContext)
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
        [MethodImpl(JsEngineConstants.Inlining)]
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
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFast1Reuse(JsValue arg0, JsValue thisValue, EvaluationContext callingContext,
            JsEnvironment reuseEnvironment)
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
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFast2(JsValue arg0, JsValue arg1, JsValue thisValue,
            EvaluationContext callingContext)
        {
            if (_canPoolInvocationEnvironment && !_usesArguments)
            {
                return InvokeSimpleFastCore2(arg0, arg1, thisValue, callingContext);
            }

            return InvokeSimpleFastWithExceptionHandling([arg0, arg1], thisValue, callingContext);
        }

        /// <summary>
        /// Sets up the execution context and environment for ultra-fast function invocation.
        /// Shared by InvokeSimpleFastCore, InvokeSimpleFastCore1, and InvokeSimpleFastCore2.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void SetupFastFunctionContext(
            JsValue thisValue,
            EvaluationContext callingContext,
            out EvaluationContext context,
            out JsEnvironment functionEnvironment)
        {
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            context = RealmState.RentContext(ScopeKind.Function, scopeMode);
            context.AllowIdentifierCache = _allowIdentifierCache;
            context.CallDepth = callingContext.CallDepth;
            context.MaxCallDepth = callingContext.MaxCallDepth;

            functionEnvironment =
                JsEnvironmentPool.Rent(_closure, true, _isStrict, _function.Source, _functionDescription, logger: RealmState.Logger);
            InitializeFunctionEnvironmentForThis(functionEnvironment, thisValue);
        }

        /// <summary>
        /// Binds parameters from the argument list to slots (fast path) or dictionary (fallback).
        /// Handles closure dictionary binding when needed.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private void BindParametersFromList(JsEnvironment env, IReadOnlyList<JsValue> arguments)
        {
            var slots = env._slots;
            if (slots is not null && env._slotCount > 0)
            {
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    slots[i].Value = value;
                    if (_function.HasClosures)
                    {
                        env.DefineParameterFast(_parameterNames[i], value);
                    }
                }
            }
            else
            {
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = i < arguments.Count ? arguments[i] : JsValue.Undefined;
                    env.DefineParameterFast(_parameterNames[i], value);
                }
            }
        }

        private void InitializeFunctionEnvironmentForThis(JsEnvironment functionEnvironment, JsValue thisValue)
        {
            functionEnvironment.ScopeId = _functionScopeId;
            functionEnvironment.SetSlotMap(_function.SlotMap);
            if (_function.SlotCount > 0)
            {
                functionEnvironment.InitializeSlots(_function.SlotCount);
            }

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
            functionEnvironment.DefineJsValue(Symbol.This, boundThisValue);
        }

        /// <summary>
        /// Executes the function body, handles result/throw/return, and returns pooled resources.
        /// Shared by InvokeSimpleFastCore, InvokeSimpleFastCore1, and InvokeSimpleFastCore2.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue ExecuteFunctionAndReturnResources(
            JsEnvironment functionEnvironment,
            EvaluationContext context,
            EvaluationContext callingContext)
        {
            var previousContext = EvaluationContext.Current;
            var previousEnvironment = JsEnvironment.Current;
            EvaluationContext.Current = context;
            JsEnvironment.Current = functionEnvironment;
            try
            {
                _ = _function.Body.EvaluateBlockJsValue(functionEnvironment, context);
            }
            finally
            {
                EvaluationContext.Current = previousContext;
                JsEnvironment.Current = previousEnvironment;
            }

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

            RealmState.ReturnContext(context);
            JsEnvironmentPool.Return(functionEnvironment, RealmState.Logger);
            return result;
        }

        /// <summary>
        /// Ultra-fast core invocation - no try/catch to allow JIT inlining.
        /// Only used when we can guarantee no ThrowSignal will escape (errors propagate via context).
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFastCore(IReadOnlyList<JsValue> arguments, JsValue thisValue,
            EvaluationContext callingContext)
        {
            SetupFastFunctionContext(thisValue, callingContext, out var context, out var functionEnvironment);
            BindParametersFromList(functionEnvironment, arguments);
            return ExecuteFunctionAndReturnResources(functionEnvironment, context, callingContext);
        }

        /// <summary>
        /// Ultra-fast 1-argument core invocation - no array allocation, no try/catch.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFastCore1(JsValue arg0, JsValue thisValue, EvaluationContext callingContext)
        {
            SetupFastFunctionContext(thisValue, callingContext, out var context, out var functionEnvironment);

            // Bind first parameter directly - no array allocation
            var slots = functionEnvironment._slots;
            if (slots is not null && functionEnvironment._slotCount > 0 && _parameterNames.Length > 0)
            {
                slots[0].Value = arg0;
                if (_function.HasClosures)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                }

                // Bind remaining parameters to undefined (when function has more params than args)
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    slots[i].Value = JsValue.Undefined;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                    }
                }
            }
            else if (_parameterNames.Length > 0)
            {
                // Fallback when slots not available
                functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                // Bind remaining parameters to undefined
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                }
            }

            return ExecuteFunctionAndReturnResources(functionEnvironment, context, callingContext);
        }

        /// <summary>
        /// Ultra-fast 1-argument core invocation with environment reuse.
        /// Reuses the provided environment AND the calling context - avoids all pooling allocations.
        /// </summary>
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFastCore1Reuse(JsValue arg0, JsValue thisValue, EvaluationContext callingContext,
            JsEnvironment reuseEnvironment)
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

            // Bind first parameter directly - no array allocation, no Array.Fill needed
            var slots = reuseEnvironment._slots;
            if (slots is not null && reuseEnvironment._slotCount > 0 && _parameterNames.Length > 0)
            {
                slots[0].Value = arg0;
                if (_function.HasClosures)
                {
                    reuseEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                }

                // Bind remaining parameters to undefined (when function has more params than args)
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    slots[i].Value = JsValue.Undefined;
                    if (_function.HasClosures)
                    {
                        reuseEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                    }
                }
            }
            else if (_parameterNames.Length > 0)
            {
                // Fallback when slots not available
                reuseEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                // Bind remaining parameters to undefined
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    reuseEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                }
            }

            var previousEnvironment = JsEnvironment.Current;
            JsEnvironment.Current = reuseEnvironment;
            try
            {
                _ = _function.Body.EvaluateBlockJsValue(reuseEnvironment, callingContext);
            }
            finally
            {
                JsEnvironment.Current = previousEnvironment;
            }

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
        [MethodImpl(JsEngineConstants.Inlining)]
        private JsValue InvokeSimpleFastCore2(JsValue arg0, JsValue arg1, JsValue thisValue,
            EvaluationContext callingContext)
        {
            SetupFastFunctionContext(thisValue, callingContext, out var context, out var functionEnvironment);

            // Bind first two parameters directly - no array allocation
            var slots = functionEnvironment._slots;
            if (slots is not null && functionEnvironment._slotCount > 0)
            {
                if (_parameterNames.Length > 0)
                {
                    slots[0].Value = arg0;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                    }
                }

                if (_parameterNames.Length > 1)
                {
                    slots[1].Value = arg1;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[1], arg1);
                    }
                }

                // Bind remaining parameters to undefined (when function has more params than args)
                for (var i = 2; i < _parameterNames.Length; i++)
                {
                    slots[i].Value = JsValue.Undefined;
                    if (_function.HasClosures)
                    {
                        functionEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                    }
                }
            }
            else
            {
                // Fallback when slots not available
                if (_parameterNames.Length > 0)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[0], arg0);
                }

                if (_parameterNames.Length > 1)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[1], arg1);
                }

                // Bind remaining parameters to undefined
                for (var i = 2; i < _parameterNames.Length; i++)
                {
                    functionEnvironment.DefineParameterFast(_parameterNames[i], JsValue.Undefined);
                }
            }

            return ExecuteFunctionAndReturnResources(functionEnvironment, context, callingContext);
        }

        /// <summary>
        /// Standard fast path with exception handling for functions that may throw.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue InvokeSimpleFastWithExceptionHandling(IReadOnlyList<JsValue> arguments, JsValue thisValue,
            EvaluationContext? callingContext)
        {
            // Rent context from pool - avoids allocation per call
            var scopeMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            var context = RealmState.RentContext(ScopeKind.Function, scopeMode);
            var functionScopeFrame = context.PushScope(ScopeKind.Function, scopeMode);
            context.AllowIdentifierCache = _allowIdentifierCache;

            if (callingContext is not null)
            {
                context.CallDepth = callingContext.CallDepth;
                context.MaxCallDepth = callingContext.MaxCallDepth;
            }

            // Create environment for function execution - use pooling when safe (no inner closures)
            var functionEnvironment = _canPoolInvocationEnvironment
                ? JsEnvironmentPool.Rent(_closure, true, _isStrict, _function.Source, _functionDescription, logger: RealmState.Logger)
                : JsEnvironment.CreateInstance(_closure, true, _isStrict, _function.Source, _functionDescription);

            InitializeFunctionEnvironmentForThis(functionEnvironment, thisValue);

            BindParametersFromList(functionEnvironment, arguments);

            // Only create arguments object if the function body actually references it
            if (_usesArguments)
            {
                var argumentsObject = new JsArgumentsObject(
                    arguments,
                    new Symbol?[arguments.Count], // No mapped parameters in strict mode
                    functionEnvironment,
                    false,
                    RealmState,
                    this,
                    true);
                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexicalBinding: false);
            }

            var previousContext = EvaluationContext.Current;
            var previousEnvironment = JsEnvironment.Current;
            EvaluationContext.Current = context;
            JsEnvironment.Current = functionEnvironment;
            try
            {
                _ = _function.Body.EvaluateBlockJsValue(functionEnvironment, context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (callingContext is null)
                    {
                        throw new ThrowSignal(thrown);
                    }

                    callingContext.SetThrow(thrown);
                    return thrown;

                }

                if (!context.IsReturn)
                {
                    return JsValue.Undefined;
                }

                var value = context.FlowValue;
                context.ClearReturn();
                return value; // FlowValue already returns JsValue, no need to wrap

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
                EvaluationContext.Current = previousContext;
                JsEnvironment.Current = previousEnvironment;
                functionScopeFrame.Dispose();
                // Return context to pool for reuse
                RealmState.ReturnContext(context);

                // Return environment to pool if pooling was used
                if (_canPoolInvocationEnvironment)
                {
                    JsEnvironmentPool.Return(functionEnvironment, RealmState.Logger);
                }
            }
        }
    }

    private static JsValue NormalizeDerivedClassRealmError(ThrowSignal signal, EvaluationContext callingContext)
    {
        if (!signal.ThrownValue.TryGetObject<JsObject>(out var errorObject))
        {
            return JsValue.Undefined;
        }

        if (!errorObject.TryGetProperty("name", out var nameValue) ||
            !nameValue.TryGetString(out var errorName))
        {
            return JsValue.Undefined;
        }

        if (!errorObject.TryGetProperty("message", out var messageValue) ||
            !messageValue.TryGetString(out var message))
        {
            return JsValue.Undefined;
        }

        if (errorName == "TypeError" &&
            message == "Derived constructors may only return object or undefined")
        {
            return StandardLibrary.CreateTypeError(
                message,
                callingContext,
                callingContext.RealmState);
        }

        if (errorName == "ReferenceError" &&
            (message.Contains("must call super() in derived class constructor", StringComparison.Ordinal) ||
             message.Contains("this is not defined", StringComparison.Ordinal) ||
             message.Contains("uninitialized", StringComparison.Ordinal) ||
             callingContext.RealmState != errorObject.RealmState))
        {
            return StandardLibrary.CreateReferenceError(
                message,
                callingContext,
                callingContext.RealmState);
        }

        return JsValue.Undefined;
    }

    private static JsObject CreateConstructedThis(JsValue newTarget, RealmState realmState)
    {
        var constructedThis = new JsObject { RealmState = realmState };
        if (!TryApplyNewTargetPrototype(constructedThis, newTarget) &&
            realmState.ObjectPrototype is { } defaultProto)
        {
            constructedThis.SetPrototype(defaultProto);
        }

        return constructedThis;
    }

    private static bool TryApplyNewTargetPrototype(JsObject constructedThis, JsValue newTarget)
    {
        if (!newTarget.TryGetObject<IJsPropertyAccessor>(out var prototypeSource) ||
            !JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(prototypeSource), "prototype", out var protoVal) ||
            !protoVal.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
        {
            return false;
        }

        constructedThis.SetPrototype(protoAccessor);
        return true;

    }
}
