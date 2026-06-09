using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    public sealed partial class SyncFunctionInvoker : IJsEnvironmentAwareCallable, IJsObjectLike,
        ICallableMetadata, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost,
        IExtensibilityControl, IPrototypeAccessorProvider, IAsJsValue, IHomeObjectConfigurableCallable
    {
        private static readonly ObjectPool<HashSet<Symbol>> SymbolSetPool = new(32,
            static () => new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance));
        private const int MaxCachedProductionUnifiedBytecodeSlotCount = 64;
        private const byte UnifiedBytecodeEligibilityUnknown = 0;
        private const byte UnifiedBytecodeEligibilityRejected = 1;
        private const byte UnifiedBytecodeEligibilityAccepted = 2;

        private readonly record struct SimpleNumericSelfRecursionFastPath(
            Symbol FunctionName,
            int BaseThreshold,
            SimpleNumericSelfRecursionBase Base,
            SimpleNumericSelfRecursionOperation Operation,
            int LeftDelta,
            int RightDelta,
            int ConstantTerm)
        {
            public const int MaxFastInput = 64;
        }

        private readonly record struct SimpleNumericSelfRecursionBase(
            bool ReturnsParameter,
            double Constant);

        private enum SimpleNumericSelfRecursionOperation : byte
        {
            AddSelfCalls,
            AddParameterAndSelfCall,
            MultiplyParameterAndSelfCall,
            AddConstantAndSelfCall
        }

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
        private readonly bool _hasCapturedActivationInClosure;
        private readonly bool _hasOnlySimpleIdentifierParameters;
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
        private readonly bool _needsArgumentsBinding;
        private readonly int _activationMinimumCapacity;
        private readonly int _functionScopeId;
        private readonly ActivationSlotShape? _activationSlots;
        private readonly bool _hasSimpleReturnParameterBinaryFastPath;
        private readonly SimpleReturnParameterBinaryExpression _simpleReturnParameterBinaryFastPath;
        private readonly bool _hasSimpleReturnParameterBinaryChainFastPath;
        private readonly SimpleReturnParameterBinaryChainExpression _simpleReturnParameterBinaryChainFastPath;
        private readonly bool _hasSimpleReturnLiteralFastPath;
        private readonly JsValue _simpleReturnLiteralFastPath;
        private readonly bool _hasSimpleReturnParameterNoArgsFastPath;
        private readonly SimpleNumericSelfRecursionFastPath? _simpleNumericSelfRecursionFastPath;
        private readonly ImmutableArray<Symbol> _legacyTailRestartResetVarNames;
        private readonly bool _hasNonParameterCalleeCall;
        private readonly bool _hasFunctionDeclarationParameterConflict;
        private readonly bool _hasFunctionDeclarations;
        private readonly bool _hasHoistableDeclarations;
        private readonly bool _hasBodyWithStatement;
        private readonly bool _hasDirectEvalInBodyOrParameters;
        private readonly bool _hasClosureWithObject;
        private readonly bool _canUseArrayIterationSingleArgumentFastPath;
        private readonly bool _canUseArrayReduceTwoArgumentFastPath;

        private readonly bool _wasAsyncFunction;
        private readonly FunctionExecutionPlanSeed _planSeed;

        // Precomputed fast path eligibility - combines all conditions except newTarget.IsUndefined
        // Updated when setters are called that could invalidate fast path
        private bool _canUseFastPathBase;
        private bool _canUseSimpleIrActivationFastBase;
        private ExecutionPlan? _syncIrTrampolineEligibilityPlan;
        private byte _syncIrTrampolineEligibility;
        private ExecutionPlan? _unifiedBytecodeProductionEligibilityPlan;
        private byte _unifiedBytecodeProductionEligibility;
        private bool _unifiedBytecodeProductionEligibilityNewTargetIsUndefined;
        private UnifiedBytecodeProgram? _unifiedBytecodeProductionProgram;
        private JsValue[]? _productionUnifiedBytecodeSlotStorage;
        private int _productionUnifiedBytecodeSlotStorageInUse;
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
            _activationSlots = planSeed.Plan?.ActivationSlots;
            var hoistPlan = ((IAstCacheable<HoistPlan>)function.Body).GetOrCreateCache();
            var bodyLexicalNames = hoistPlan.LexicalNames;
            var hasHoistableDeclarations = ((IAstCacheable<HoistableDeclarationsPlan>)function.Body)
                .GetOrCreateCache()
                .HasHoistableDeclarations;
            _hasHoistableDeclarations = hasHoistableDeclarations;
            var hasFunctionDeclarations = hoistPlan.HasFunctionDeclarations;
            _hasFunctionDeclarations = hasFunctionDeclarations;
            _hasParameterExpressions = _function.HasParameterExpressions();
            // Allow identifier caching only if the function body has no with/eval AND
            // the closure chain has no with environments (functions defined inside with blocks
            // need to check with bindings at runtime)
            _hasBodyWithStatement = DynamicScopeDetector.ContainsWithStatement(_function.Body);
            _hasDirectEvalInBodyOrParameters =
                DynamicScopeDetector.ContainsDirectEvalInParameters(_function.Parameters) ||
                DynamicScopeDetector.ContainsDirectEval(_function.Body);
            _hasClosureWithObject = closure.HasWithObjectInChain();
            _allowIdentifierCache = AllowsIdentifierCaching(_function) && !_hasClosureWithObject;

            // Use cached static analysis — these are pure AST properties, safe to cache per FunctionExpression.
            // Retrieved early so _usesArguments/_needsArgumentsBinding can read from the cache instead of
            // repeating AST traversals that also fire on every call in ExecutionPlanRunner.CreateExecutionEnvironment.
            var invokerStatics = ((IAstCacheable<FunctionInvokerStaticPlan>)_function).GetOrCreateCache();
            _usesArguments = !IsArrowFunction && invokerStatics.UsesArguments;
            _needsArgumentsBinding = !IsArrowFunction && invokerStatics.NeedsArgumentsBinding;

            _hasCapturedActivationInClosure = HasCapturedActivationInClosure(closure);
            _functionScopeId = ResolveFunctionScopeId(function);
            _activationMinimumCapacity = ComputeActivationMinimumCapacity();

            // Detect simple functions for fast-path invocation
            // A simple function has: no async, no defaults, no destructuring, no body lexicals, no hoisting needed
            // Note: _hasFunctionNameEnvironment being true is fine - it just means the function name binding is
            // in an intermediate scope (for named function expressions), not in the invocation environment.
            // For non-strict mode: can use fast path if the function doesn't use 'arguments' identifier,
            // since mapped arguments object (which links argument values to parameter bindings) is not needed.
            var hasSimpleParams = HasOnlySimpleIdentifierParameters(function);
            _hasOnlySimpleIdentifierParameters = hasSimpleParams;
            _canUseArrayIterationSingleArgumentFastPath =
                IsArrowFunction &&
                function.Parameters.Length <= 1 &&
                !_hasParameterExpressions &&
                !IsAsyncLike &&
                !function.IsGenerator &&
                hasSimpleParams;
            _canUseArrayReduceTwoArgumentFastPath =
                IsArrowFunction &&
                function.Parameters.Length <= 2 &&
                !_hasParameterExpressions &&
                !IsAsyncLike &&
                !function.IsGenerator &&
                hasSimpleParams;
            var canUseFastPathForStrictness = _isStrict || !_usesArguments;
            var isSimpleFunction = canUseFastPathForStrictness &&
                                   !function.IsAsync &&
                                   !_wasAsyncFunction &&
                                   !_hasParameterExpressions &&
                                   hoistPlan.LexicalTemplate.Length == 0 &&
                                   !hasHoistableDeclarations &&
                                   _allowIdentifierCache &&
                                   hasSimpleParams;

            // Initialize; finalize after recursive/non-parameter callee analysis below.
            _canPoolInvocationEnvironment = false;

            // Cache the function description to avoid string allocation per call
            _functionDescription = function.Name is { } funcName ? $"function {funcName.Name}" : "anonymous function";

            var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)_function).GetOrCreateCache()
                .ParameterNames;
            _parameterNames = parameterNames;
            _legacyTailRestartResetVarNames = BuildLegacyTailRestartResetVarNames(function.Body, parameterNames);

            _hasFunctionDeclarationParameterConflict = invokerStatics.HasFunctionDeclarationParameterConflict;
            _hasNonParameterCalleeCall = invokerStatics.HasNonParameterCalleeCall;

            // Recursive/self-call-like shapes must get a fresh activation per invocation.
            if (_hasNonParameterCalleeCall)
            {
                _canUseFastPathBase = false;
                _canUseSimpleIrActivationFastBase = false;
            }

            // Pool only when fast/simple and proven non-recursive for identifier callees.
            _canPoolInvocationEnvironment = isSimpleFunction &&
                                            !invokerStatics.HasInnerFunctionExpression &&
                                            !_hasNonParameterCalleeCall;
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
                                  !invokerStatics.HasInnerFunctionExpression &&
                                  !_hasNonParameterCalleeCall;
            _canUseSimpleIrActivationFastBase = canUseFastPathForStrictness &&
                                                !function.IsAsync &&
                                                !_wasAsyncFunction &&
                                                !_hasParameterExpressions &&
                                                hoistPlan.LexicalTemplate.Length == 0 &&
                                                !hasHoistableDeclarations &&
                                                _allowIdentifierCache &&
                                                hasSimpleParams &&
                                                _lexicalThisEnvironment is null &&
                                                !invokerStatics.HasInnerFunctionExpression &&
                                                !_hasNonParameterCalleeCall;
            if (planSeed.Plan is { SimpleReturnParameterBinary: { } parameterBinary } plan &&
                CanUseSimpleIrActivationPlanShape(plan))
            {
                _hasSimpleReturnParameterBinaryFastPath = true;
                _simpleReturnParameterBinaryFastPath = parameterBinary;
            }

            if (planSeed.Plan is { SimpleReturnParameterBinaryChain: { } parameterBinaryChain } chainPlan &&
                CanUseSimpleIrActivationPlanShape(chainPlan))
            {
                _hasSimpleReturnParameterBinaryChainFastPath = true;
                _simpleReturnParameterBinaryChainFastPath = parameterBinaryChain;
            }

            if (_canUseSimpleIrActivationFastBase &&
                planSeed.Plan is { SimpleReturnLiteral: { } literal } literalPlan &&
                CanUseSimpleIrActivationPlanShape(literalPlan))
            {
                _hasSimpleReturnLiteralFastPath = true;
                _simpleReturnLiteralFastPath = literal.Value;
            }

            if (_canUseSimpleIrActivationFastBase &&
                planSeed.Plan is { SimpleReturnParameter: not null } parameterPlan &&
                CanUseSimpleIrActivationPlanShape(parameterPlan))
            {
                _hasSimpleReturnParameterNoArgsFastPath = true;
            }

            if (_isStrict &&
                isSimpleFunction &&
                _lexicalThisEnvironment is null &&
                !ContainsInnerFunctionExpression(function) &&
                TryCreateSimpleNumericSelfRecursionFastPath(function, parameterNames, out var selfRecursionFastPath))
            {
                _simpleNumericSelfRecursionFastPath = selfRecursionFastPath;
            }
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

        internal bool CanUseArrayIterationSingleArgumentFastPath => _canUseArrayIterationSingleArgumentFastPath;

        internal bool CanUseArrayReduceTwoArgumentFastPath => _canUseArrayReduceTwoArgumentFastPath;

        [MethodImpl(JsEngineConstants.Inlining)]
        internal bool TryInvokeArrayReduceTwoArgumentNumericFastPath(
            JsValue accumulator,
            JsValue value,
            out JsValue result)
        {
            result = JsValue.Undefined;
            if (!_canUseArrayReduceTwoArgumentFastPath ||
                !_hasSimpleReturnParameterBinaryFastPath ||
                !accumulator.IsNumber ||
                !value.IsNumber ||
                IsClassConstructor ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _lexicalThisEnvironment is not null ||
                _homeObject is not null ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty ||
                !TryGetSimpleNumberArgument(
                    accumulator,
                    value,
                    _simpleReturnParameterBinaryFastPath.LeftParameterIndex,
                    out var left) ||
                !TryGetSimpleNumberArgument(
                    accumulator,
                    value,
                    _simpleReturnParameterBinaryFastPath.RightParameterIndex,
                    out var right))
            {
                return false;
            }

            result = _simpleReturnParameterBinaryFastPath.Operator switch
            {
                BinaryOperator.Add => JsValue.FromDouble(left + right),
                BinaryOperator.Subtract => JsValue.FromDouble(left - right),
                BinaryOperator.Multiply => JsValue.FromDouble(left * right),
                _ => JsValue.FromDouble(left / right)
            };
            return true;
        }

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
        private JsValue CoerceThisValueForNonStrict(JsValue thisValue) =>
            CoerceThisValueForNonStrict(thisValue, RealmState);

        /// <summary>
        ///     Coerces a non-strict <c>this</c> binding per ECMA-262 (OrdinaryCallBindThis): null/undefined
        ///     become <c>globalThis</c> and primitives are boxed. Shared by the sync production route and the
        ///     resumable (async/generator) routes so <c>this</c> coercion stays identical across both.
        /// </summary>
        internal static JsValue CoerceThisValueForNonStrict(JsValue thisValue, RealmState realmState)
        {
            // Null/undefined → globalThis
            if (thisValue.IsNullish)
            {
                return realmState.Engine is { GlobalObject: { } globalObj }
                    ? (JsValue)globalObj
                    : JsValue.Undefined;
            }

            // Primitives → boxed objects
            if (thisValue.IsNumber)
            {
                return JsValue.FromJsObject(NumberHelper.CreateNumberWrapper(thisValue.AsDouble(),
                    realm: realmState));
            }

            if (thisValue.IsString)
            {
                return JsValue.FromJsObject(StringHelper.CreateStringWrapper(thisValue.AsString(),
                    realm: realmState));
            }

            if (thisValue.IsBoolean)
            {
                return JsValue.FromJsObject(
                    BooleanHelper.CreateBooleanWrapper(thisValue.AsBoolean(), realm: realmState));
            }

            if (thisValue.IsBigInt)
            {
                return JsValue.FromJsObject(BigIntHelper.CreateBigIntWrapper(thisValue.AsBigInt(),
                    realm: realmState));
            }

            if (thisValue.IsSymbol && thisValue.TryUnwrap<JsSymbol>(out var typedSymbol))
            {
                return JsValue.FromJsObject(SymbolHelper.CreateSymbolWrapper(typedSymbol, realm: realmState));
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
            if (TryInvokeSimpleReturnFastPath(arguments.Count, newTarget, out var fastResult))
            {
                return fastResult;
            }

            return InvokeWithContextSlow(arguments, thisValue, callingContext, newTarget);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget = default)
            where TArgs : IReadOnlyList<JsValue>
        {
            if (TryInvokeSimpleReturnFastPath(arguments.Count, newTarget, out var fastResult))
            {
                return fastResult;
            }

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
            if (TryInvokeSimpleReturnFastPath(1, JsValue.Undefined, out var literalResult))
            {
                return literalResult;
            }

            if (TryInvokeSimpleNumericSelfRecursion1(arg0, out var fastResult))
            {
                return fastResult;
            }

            return InvokeWithContextSlow(new SingleValueArgs(arg0), thisValue, callingContext, JsValue.Undefined);
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

            return InvokeWithContextSlow(new SingleValueArgs(arg0), thisValue, callingContext, JsValue.Undefined);
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
            if (TryInvokeSimpleReturnFastPath(2, JsValue.Undefined, out var literalResult))
            {
                return literalResult;
            }

            if (TryInvokePrecomputedSimpleNumberParameterBinary2(arg0, arg1, out var fastResult))
            {
                return fastResult;
            }

            return InvokeWithContextSlow(new TwoValueArgs(arg0, arg1), thisValue, callingContext, JsValue.Undefined);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        public JsValue InvokeWithContext3(
            JsValue arg0,
            JsValue arg1,
            JsValue arg2,
            JsValue thisValue,
            EvaluationContext callingContext)
        {
            if (TryInvokeSimpleReturnFastPath(3, JsValue.Undefined, out var literalResult))
            {
                return literalResult;
            }

            if (TryInvokePrecomputedSimpleNumberParameterBinaryChain3(arg0, arg1, arg2, out var fastResult))
            {
                return fastResult;
            }

            return InvokeWithContextSlow(
                new ThreeValueArgs(arg0, arg1, arg2),
                thisValue,
                callingContext,
                JsValue.Undefined);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryInvokeSimpleReturnFastPath(
            int argumentCount,
            JsValue newTarget,
            out JsValue result)
        {
            result = JsValue.Undefined;
            if (ShouldDeferSimpleIrFastPathToProductionUnifiedBytecode(newTarget))
            {
                return false;
            }

            if ((!_hasSimpleReturnLiteralFastPath && !_hasSimpleReturnParameterNoArgsFastPath) ||
                !newTarget.IsUndefined ||
                IsClassConstructor ||
                IsArrowFunction ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _needsArgumentsBinding ||
                _homeObject is not null ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty)
            {
                return false;
            }

            if (_hasSimpleReturnLiteralFastPath)
            {
                result = _simpleReturnLiteralFastPath;
                return true;
            }

            if (_hasSimpleReturnParameterNoArgsFastPath && argumentCount == 0)
            {
                RealmState.Logger?.LogInformation(
                    "simple-ir-return-fast-path func={Function} argc=0",
                    _function.Name?.Name ?? "<anonymous>");
                result = JsValue.Undefined;
                return true;
            }

            return false;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryInvokeSimpleNumericSelfRecursion1(
            JsValue arg0,
            out JsValue result)
        {
            result = JsValue.Undefined;
            if (_simpleNumericSelfRecursionFastPath is not { } fastPath ||
                !arg0.IsNumber ||
                IsClassConstructor ||
                IsArrowFunction ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _homeObject is not null ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty ||
                !IsCurrentRecursiveNameBinding(fastPath.FunctionName))
            {
                return false;
            }

            var value = arg0.NumberValue;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            if (value <= fastPath.BaseThreshold)
            {
                result = fastPath.Base.ReturnsParameter
                    ? arg0
                    : JsValue.FromDouble(fastPath.Base.Constant);
                return true;
            }

            if (value != Math.Truncate(value) || value > SimpleNumericSelfRecursionFastPath.MaxFastInput)
            {
                return false;
            }

            result = JsValue.FromDouble(EvaluateSimpleNumericSelfRecursion((int)value, fastPath));
            return true;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool IsCurrentRecursiveNameBinding(Symbol functionName)
        {
            if (!_closure.TryFindBindingJsValue(functionName, true, out _, out var currentBinding))
            {
                return false;
            }

            return currentBinding.TryGetObject<SyncFunctionInvoker>(out var currentFunction) &&
                   ReferenceEquals(currentFunction, this);
        }

        internal static bool TryGetLegacySameFunctionTailRestartTarget(
            CallExpression expression,
            JsEnvironment environment,
            EvaluationContext context,
            out SyncFunctionInvoker current)
        {
            current = null!;
            var executing = t_currentlyExecuting;
            if (executing?._isStrict != true ||
                executing.IsAsyncLike ||
                executing.IsClassConstructor ||
                !executing.HasOnlySimpleLegacyTailRestartParameters() ||
                !executing.CanReuseLegacyTailRestartActivation(environment) ||
                expression.IsOptional)
            {
                return false;
            }

            if (expression.Callee is not IdentifierExpression calleeId)
            {
                return false;
            }

            foreach (var argument in expression.Arguments)
            {
                if (argument.IsSpread)
                {
                    return false;
                }
            }

            var calleeValue = calleeId is { SlotIndex: >= 0, ScopeId: >= 0 } &&
                              environment.TryReadIdentifierWithSlot(calleeId, context, out var slotCallee)
                ? slotCallee
                : context.GetIdentifier(environment, calleeId.Name);
            if (context.ShouldStopEvaluation ||
                !calleeValue.TryGetObject<SyncFunctionInvoker>(out var callable) ||
                !ReferenceEquals(callable, executing))
            {
                return false;
            }

            current = executing;
            return true;
        }

        internal bool CanReuseLegacyTailRestartActivation(JsEnvironment environment)
        {
            var current = environment;
            while (current is not null && !ReferenceEquals(current, _closure))
            {
                if (current.IsCaptured)
                {
                    return false;
                }

                current = current.Enclosing;
            }

            return true;
        }

        internal bool CapturesActivationBetween(JsEnvironment environment, JsEnvironment closure)
        {
            var current = environment;
            while (current is not null && !ReferenceEquals(current, closure))
            {
                if (ReferenceEquals(current, _closure))
                {
                    return true;
                }

                current = current.Enclosing;
            }

            return false;
        }

        internal bool CapturesActivationTransitivelyBetween(JsEnvironment environment, JsEnvironment closure)
        {
            HashSet<object>? visitedFunctions = null;
            return CapturesActivationTransitivelyBetweenCore(environment, closure, ref visitedFunctions);
        }

        private bool CapturesActivationTransitivelyBetweenCore(
            JsEnvironment environment,
            JsEnvironment closure,
            ref HashSet<object>? visitedFunctions)
        {
            visitedFunctions ??= new HashSet<object>(ReferenceEqualityComparer<object>.Instance);
            if (!visitedFunctions.Add(this))
            {
                return false;
            }

            if (CapturesActivationBetween(environment, closure))
            {
                return true;
            }

            for (var current = _closure; current is not null; current = current.Enclosing)
            {
                for (var i = 0; i < current.SlotCount; i++)
                {
                    ref var slot = ref current.GetSlotByIndex(i);
                    if (slot.IsUninitialized ||
                        slot.HasSpecialBinding ||
                        slot.Value.Kind != JsValueKind.Object ||
                        !slot.Value.TryGetObject<SyncFunctionInvoker>(out var nested))
                    {
                        continue;
                    }

                    if (nested.CapturesActivationTransitivelyBetweenCore(environment, closure, ref visitedFunctions))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasOnlySimpleLegacyTailRestartParameters()
        {
            HashSet<Symbol>? seenNames = null;
            for (var i = 0; i < _function.Parameters.Length; i++)
            {
                var parameter = _function.Parameters[i];
                if (parameter is not { Name: { } name, Pattern: null, DefaultValue: null, IsRest: false })
                {
                    return false;
                }

                seenNames ??= new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                if (!seenNames.Add(name))
                {
                    return false;
                }
            }

            return true;
        }

        private static ImmutableArray<Symbol> BuildLegacyTailRestartResetVarNames(
            BlockStatement body,
            ImmutableArray<Symbol> parameterNames)
        {
            var declaredNames = new List<Symbol>();
            VarNameCollector.CollectVarDeclaredNames(body, declaredNames);
            if (declaredNames.Count == 0)
            {
                return ImmutableArray<Symbol>.Empty;
            }

            HashSet<Symbol>? parameterNameSet = null;
            if (!parameterNames.IsEmpty)
            {
                parameterNameSet = new HashSet<Symbol>(parameterNames, ReferenceEqualityComparer<Symbol>.Instance);
            }

            var seenNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            var resetNames = ImmutableArray.CreateBuilder<Symbol>();
            foreach (var name in declaredNames)
            {
                if (parameterNameSet?.Contains(name) == true ||
                    !seenNames.Add(name))
                {
                    continue;
                }

                resetNames.Add(name);
            }

            return resetNames.Count == 0 ? ImmutableArray<Symbol>.Empty : resetNames.ToImmutable();
        }

        private void ResetLegacyTailRestartActivation(
            JsEnvironment varEnvironment,
            JsEnvironment executionEnvironment)
        {
            foreach (var name in _legacyTailRestartResetVarNames)
            {
                varEnvironment.DefineFunctionScoped(name, JsValue.Undefined, hasInitializer: true);
            }

            foreach (var lexicalName in _topLevelLexicalNames)
            {
                var isConst = _lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isConst: isConst,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }
        }

        private static double EvaluateSimpleNumericSelfRecursion(
            int input,
            SimpleNumericSelfRecursionFastPath fastPath)
        {
            Span<double> values = stackalloc double[input + 1];
            for (var i = 0; i <= input; i++)
            {
                values[i] = i <= fastPath.BaseThreshold
                    ? GetSimpleNumericSelfRecursionBaseValue(i, fastPath.Base)
                    : EvaluateSimpleNumericSelfRecursionStep(i, values, fastPath);
            }

            return values[input];
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static double EvaluateSimpleNumericSelfRecursionStep(
            int input,
            Span<double> values,
            SimpleNumericSelfRecursionFastPath fastPath)
        {
            var left = GetSimpleNumericSelfRecursionValue(values, input - fastPath.LeftDelta, fastPath.BaseThreshold,
                fastPath.Base);
            return fastPath.Operation switch
            {
                SimpleNumericSelfRecursionOperation.AddSelfCalls =>
                    left + GetSimpleNumericSelfRecursionValue(values, input - fastPath.RightDelta,
                        fastPath.BaseThreshold, fastPath.Base),
                SimpleNumericSelfRecursionOperation.AddParameterAndSelfCall => input + left,
                SimpleNumericSelfRecursionOperation.MultiplyParameterAndSelfCall => input * left,
                SimpleNumericSelfRecursionOperation.AddConstantAndSelfCall => fastPath.ConstantTerm + left,
                _ => input
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static double GetSimpleNumericSelfRecursionBaseValue(
            int input,
            SimpleNumericSelfRecursionBase @base)
        {
            return @base.ReturnsParameter ? input : @base.Constant;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static double GetSimpleNumericSelfRecursionValue(
            Span<double> values,
            int index,
            int baseThreshold,
            SimpleNumericSelfRecursionBase @base)
        {
            return index <= baseThreshold ? GetSimpleNumericSelfRecursionBaseValue(index, @base) : values[index];
        }

        private static bool TryCreateSimpleNumericSelfRecursionFastPath(
            FunctionExpression function,
            ImmutableArray<Symbol> parameterNames,
            out SimpleNumericSelfRecursionFastPath fastPath)
        {
            fastPath = default;
            if (parameterNames.Length != 1 ||
                function.Body.Statements.Length != 2)
            {
                return false;
            }

            var parameterName = parameterNames[0];
            return function.Body.Statements[0] is IfStatement
            {
                Condition: BinaryExpression
                {
                    Operator: BinaryOperator.LessThanOrEqual,
                    Left: IdentifierExpression conditionIdentifier,
                    Right: LiteralExpression { Value.IsNumber: true } conditionLimit
                },
                Then: ReturnStatement { Expression: { } baseReturn },
                Else: null
            } &&
ReferenceEquals(conditionIdentifier.Name, parameterName) &&
TryGetSimpleNumericSelfRecursionBase(baseReturn, parameterName, out var recursionBase) &&
TryGetSmallInteger(conditionLimit.Value, out var baseThreshold) &&
                baseThreshold >= 0 &&
                function.Body.Statements[1] is ReturnStatement
                {
                    Expression: BinaryExpression returnExpression
                } &&
TryCreateSimpleNumericSelfRecursionFastPath(
                    returnExpression,
                    parameterName,
                    baseThreshold,
                    recursionBase,
                    out fastPath);
        }

        private static bool TryCreateSimpleNumericSelfRecursionFastPath(
            BinaryExpression returnExpression,
            Symbol parameterName,
            int baseThreshold,
            SimpleNumericSelfRecursionBase recursionBase,
            out SimpleNumericSelfRecursionFastPath fastPath)
        {
            fastPath = default;
            if (returnExpression.Operator == BinaryOperator.Add &&
                TryGetSelfCallSubtractDelta(
                    returnExpression.Left,
                    parameterName,
                    out var leftFunctionName,
                    out var leftDelta) &&
                !ReferenceEquals(parameterName, leftFunctionName) &&
                TryGetSelfCallSubtractDelta(
                    returnExpression.Right,
                    parameterName,
                    out var rightFunctionName,
                    out var rightDelta) &&
                ReferenceEquals(leftFunctionName, rightFunctionName))
            {
                fastPath = new SimpleNumericSelfRecursionFastPath(
                    leftFunctionName,
                    baseThreshold,
                    recursionBase,
                    SimpleNumericSelfRecursionOperation.AddSelfCalls,
                    leftDelta,
                    rightDelta,
                    0);
                return true;
            }

            if ((returnExpression.Operator == BinaryOperator.Add || returnExpression.Operator == BinaryOperator.Multiply) &&
                TryGetLinearSelfRecursionTerms(
                    returnExpression,
                    parameterName,
                    out var functionName,
                    out var delta))
            {
                fastPath = new SimpleNumericSelfRecursionFastPath(
                    functionName,
                    baseThreshold,
                    recursionBase,
                    returnExpression.Operator == BinaryOperator.Add
                        ? SimpleNumericSelfRecursionOperation.AddParameterAndSelfCall
                        : SimpleNumericSelfRecursionOperation.MultiplyParameterAndSelfCall,
                    delta,
                    0,
                    0);
                return true;
            }

            if (returnExpression.Operator == BinaryOperator.Add &&
                TryGetConstantSelfRecursionTerms(
                    returnExpression,
                    parameterName,
                    out functionName,
                    out delta,
                    out var constant))
            {
                fastPath = new SimpleNumericSelfRecursionFastPath(
                    functionName,
                    baseThreshold,
                    recursionBase,
                    SimpleNumericSelfRecursionOperation.AddConstantAndSelfCall,
                    delta,
                    0,
                    constant);
                return true;
            }

            return false;
        }

        private static bool TryGetSimpleNumericSelfRecursionBase(
            ExpressionNode expression,
            Symbol parameterName,
            out SimpleNumericSelfRecursionBase recursionBase)
        {
            recursionBase = default;
            if (expression is IdentifierExpression baseReturn &&
                ReferenceEquals(baseReturn.Name, parameterName))
            {
                recursionBase = new SimpleNumericSelfRecursionBase(ReturnsParameter: true, Constant: 0);
                return true;
            }

            if (expression is LiteralExpression { Value.IsNumber: true } literal &&
                TryGetSmallInteger(literal.Value, out var constant))
            {
                recursionBase = new SimpleNumericSelfRecursionBase(ReturnsParameter: false, constant);
                return true;
            }

            return false;
        }

        private static bool TryGetLinearSelfRecursionTerms(
            BinaryExpression expression,
            Symbol parameterName,
            out Symbol functionName,
            out int delta)
        {
            if (expression.Left is IdentifierExpression leftParameter &&
                ReferenceEquals(leftParameter.Name, parameterName) &&
                TryGetSelfCallSubtractDelta(expression.Right, parameterName, out functionName, out delta) &&
                !ReferenceEquals(functionName, parameterName))
            {
                return true;
            }

            if (expression.Right is IdentifierExpression rightParameter &&
                ReferenceEquals(rightParameter.Name, parameterName) &&
                TryGetSelfCallSubtractDelta(expression.Left, parameterName, out functionName, out delta) &&
                !ReferenceEquals(functionName, parameterName))
            {
                return true;
            }

            functionName = Symbol.Undefined;
            delta = 0;
            return false;
        }

        private static bool TryGetConstantSelfRecursionTerms(
            BinaryExpression expression,
            Symbol parameterName,
            out Symbol functionName,
            out int delta,
            out int constant)
        {
            if (expression.Left is LiteralExpression { Value.IsNumber: true } leftLiteral &&
                TryGetSmallInteger(leftLiteral.Value, out constant) &&
                TryGetSelfCallSubtractDelta(expression.Right, parameterName, out functionName, out delta) &&
                !ReferenceEquals(functionName, parameterName))
            {
                return true;
            }

            if (expression.Right is LiteralExpression { Value.IsNumber: true } rightLiteral &&
                TryGetSmallInteger(rightLiteral.Value, out constant) &&
                TryGetSelfCallSubtractDelta(expression.Left, parameterName, out functionName, out delta) &&
                !ReferenceEquals(functionName, parameterName))
            {
                return true;
            }

            functionName = Symbol.Undefined;
            delta = 0;
            constant = 0;
            return false;
        }

        private static bool TryGetSelfCallSubtractDelta(
            ExpressionNode expression,
            Symbol parameterName,
            out Symbol functionName,
            out int delta)
        {
            functionName = Symbol.Undefined;
            delta = 0;
            if (expression is not CallExpression
                {
                    IsOptional: false,
                    Callee: IdentifierExpression callee,
                    Arguments.Length: 1
                } call ||
                call.Arguments[0].IsSpread ||
                call.Arguments[0].Expression is not BinaryExpression
                {
                    Operator: BinaryOperator.Subtract,
                    Left: IdentifierExpression argumentIdentifier,
                    Right: LiteralExpression { Value.IsNumber: true } decrement
                } ||
                !ReferenceEquals(argumentIdentifier.Name, parameterName) ||
                !TryGetSmallInteger(decrement.Value, out delta) ||
                delta <= 0)
            {
                return false;
            }

            functionName = callee.Name;
            return true;
        }

        private static bool TryGetSmallInteger(JsValue value, out int result)
        {
            result = 0;
            var number = value.NumberValue;
            if (double.IsNaN(number) ||
                double.IsInfinity(number) ||
                number != Math.Truncate(number) ||
                number < int.MinValue ||
                number > int.MaxValue)
            {
                return false;
            }

            result = (int)number;
            return true;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryInvokePrecomputedSimpleNumberParameterBinary2(
            JsValue arg0,
            JsValue arg1,
            out JsValue result)
        {
            result = JsValue.Undefined;
            if (!_hasSimpleReturnParameterBinaryFastPath ||
                !_canUseSimpleIrActivationFastBase ||
                !arg0.IsNumber ||
                !arg1.IsNumber ||
                IsClassConstructor ||
                IsArrowFunction ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _lexicalThisEnvironment is not null ||
                _homeObject is not null ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty ||
                !TryGetSimpleNumberArgument(arg0, arg1, _simpleReturnParameterBinaryFastPath.LeftParameterIndex,
                    out var left) ||
                !TryGetSimpleNumberArgument(arg0, arg1, _simpleReturnParameterBinaryFastPath.RightParameterIndex,
                    out var right))
            {
                return false;
            }

            if (ShouldDeferSimpleIrFastPathToProductionUnifiedBytecode(JsValue.Undefined))
            {
                return false;
            }

            RealmState.Logger?.LogInformation(
                "simple-ir-parameter-number-binary-fast-path func={Function}",
                _function.Name?.Name ?? "<anonymous>");

            result = _simpleReturnParameterBinaryFastPath.Operator switch
            {
                BinaryOperator.Add => JsValue.FromDouble(left + right),
                BinaryOperator.Subtract => JsValue.FromDouble(left - right),
                BinaryOperator.Multiply => JsValue.FromDouble(left * right),
                _ => JsValue.FromDouble(left / right)
            };
            return true;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryInvokePrecomputedSimpleNumberParameterBinaryChain3(
            JsValue arg0,
            JsValue arg1,
            JsValue arg2,
            out JsValue result)
        {
            result = JsValue.Undefined;
            if (!_hasSimpleReturnParameterBinaryChainFastPath ||
                !_canUseSimpleIrActivationFastBase ||
                !arg0.IsNumber ||
                !arg1.IsNumber ||
                !arg2.IsNumber ||
                IsClassConstructor ||
                IsArrowFunction ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _lexicalThisEnvironment is not null ||
                _homeObject is not null ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty ||
                !TryGetSimpleNumberArgument(
                    arg0,
                    arg1,
                    arg2,
                    _simpleReturnParameterBinaryChainFastPath.LeftParameterIndex,
                    out var left) ||
                !TryGetSimpleNumberArgument(
                    arg0,
                    arg1,
                    arg2,
                    _simpleReturnParameterBinaryChainFastPath.RightParameterIndex,
                    out var right) ||
                !TryGetSimpleNumberArgument(
                    arg0,
                    arg1,
                    arg2,
                    _simpleReturnParameterBinaryChainFastPath.ThirdParameterIndex,
                    out var third))
            {
                return false;
            }

            if (ShouldDeferSimpleIrFastPathToProductionUnifiedBytecode(JsValue.Undefined))
            {
                return false;
            }

            RealmState.Logger?.LogInformation(
                "simple-ir-parameter-number-binary-chain-fast-path func={Function}",
                _function.Name?.Name ?? "<anonymous>");

            var firstResult = EvaluateSimpleNumberParameterBinaryOperator(
                _simpleReturnParameterBinaryChainFastPath.FirstOperator,
                left,
                right);
            result = JsValue.FromDouble(EvaluateSimpleNumberParameterBinaryOperator(
                _simpleReturnParameterBinaryChainFastPath.SecondOperator,
                firstResult,
                third));
            return true;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static double EvaluateSimpleNumberParameterBinaryOperator(
            BinaryOperator op,
            double left,
            double right)
        {
            return op switch
            {
                BinaryOperator.Add => left + right,
                BinaryOperator.Subtract => left - right,
                BinaryOperator.Multiply => left * right,
                BinaryOperator.Divide => left / right,
                BinaryOperator.BitwiseXor => JsNumericConversions.ToInt32(left) ^
                                             JsNumericConversions.ToInt32(right),
                _ => double.NaN
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static bool TryGetSimpleNumberArgument(
            JsValue arg0,
            JsValue arg1,
            int index,
            out double value)
        {
            switch (index)
            {
                case 0 when arg0.IsNumber:
                    value = arg0.NumberValue;
                    return true;

                case 1 when arg1.IsNumber:
                    value = arg1.NumberValue;
                    return true;

                default:
                    value = 0.0;
                    return false;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static bool TryGetSimpleNumberArgument(
            JsValue arg0,
            JsValue arg1,
            JsValue arg2,
            int index,
            out double value)
        {
            switch (index)
            {
                case 0 when arg0.IsNumber:
                    value = arg0.NumberValue;
                    return true;

                case 1 when arg1.IsNumber:
                    value = arg1.NumberValue;
                    return true;

                case 2 when arg2.IsNumber:
                    value = arg2.NumberValue;
                    return true;

                default:
                    value = 0.0;
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue InvokeWithContextSlow<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget)
            where TArgs : IReadOnlyList<JsValue>
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
                context.DisableSyncIrCallTrampoline = callingContext.DisableSyncIrCallTrampoline;
            }

            // Track Function.caller (Annex B) for non-strict functions.
            // Save and restore the caller chain so recursive/nested calls work correctly.
            var previousCaller = _currentCaller;
            var previouslyExecuting = t_currentlyExecuting;
            var previousEvaluationContext = EvaluationContext.Current;
            var previousRealm = global::Asynkron.JsEngine.Runtime.RealmState.Current;
            _currentCaller = t_currentlyExecuting;
            t_currentlyExecuting = this;
            EvaluationContext.Current = context;
            global::Asynkron.JsEngine.Runtime.RealmState.Current = RealmState;
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
                var hasFunctionCodeIrSeam =
                    context.ExecutionKind == ExecutionKind.Script &&
                    _allowIdentifierCache &&
                    (_hasFunctionDeclarationParameterConflict ||
                     (_hasHoistableDeclarations && _isStrict) ||
                     (_hasNonParameterCalleeCall && (!_isStrict || _hasHoistableDeclarations)));
                if (hasFunctionCodeIrSeam &&
                    !IsClassConstructor &&
                    plan is not null &&
                    !_usesArguments &&
                    !_needsArgumentsBinding &&
                    _legacyTailRestartResetVarNames.IsEmpty &&
                    !(_isStrict && HasBlockScopedFunctionDeclarationInstruction(plan)) &&
                    CanUseProductionUnifiedBytecodeFastPath(plan, newTarget))
                {
                    hasFunctionCodeIrSeam = false;
                }

                var canUseIrPlan =
                    !_function.IsGenerator &&
                    !IsAsyncFunction &&
                    // Keep IR for non-script function contexts; block known function-code seams in script mode.
                    !hasFunctionCodeIrSeam &&
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
                        if (IsClassConstructor &&
                            CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath(plan, newTarget) &&
                            TryInvokeProductionUnifiedBytecode(
                                arguments,
                                thisValue,
                                newTarget,
                                plan,
                                context,
                                callingContext,
                                out var productionConstructorResult,
                                constructErrorRealm))
                        {
                            return productionConstructorResult;
                        }

                        if (!IsClassConstructor &&
                            TryInvokeIrFast(
                                arguments,
                                thisValue,
                                callingContext,
                                newTarget,
                                plan,
                                context,
                                out var fastResult))
                        {
                            return fastResult;
                        }

                        if (IsClassConstructor)
                        {
                            RealmState.Logger?.LogInformation(
                                "class-constructor production bytecode declined; falling through to classified IR residue func={Function}",
                                _function.Name?.Name ?? "<anonymous>");
                        }

                        return ExecuteClassifiedSyncFunctionIrResidue(
                            arguments,
                            thisValue,
                            newTarget,
                            plan,
                            context,
                            callingContext,
                            constructErrorRealm);
                    }

                    if (!IsClassConstructor && plan is null)
                    {
                        throw new NotSupportedException(
                            $"IR plan generation failed for function: {failureReason}");
                    }
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

                        if (_hasParameterExpressions)
                        {
                            foreach (var blockedName in CollectAnnexBBlockFunctionNames(_function.Body))
                            {
                                blockedNames.Add(blockedName);
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
                    var boundThis = thisValue;

                    if (IsClassConstructor &&
                        boundThis.IsUndefined &&
                        !newTarget.IsUndefined)
                    {
                        var constructedThis = CreateConstructedThis(newTarget, RealmState);

                        RealmState.Logger?.LogInformation(
                            "ctor: synthesized receiver func={Function} receiver={Receiver} proto={Proto} newTargetKind={NewTargetKind}",
                            _function.Name?.Name ?? "<anonymous>",
                            DescribeValue(constructedThis),
                            DescribePrototype(constructedThis.PrototypeAccessor ?? constructedThis.Prototype),
                            newTarget.Kind);

                        boundThis = JsValue.FromObjectUnsafe(constructedThis);
                    }

                    if (!_isStrict)
                    {
                        if (thisValue.IsNullish)
                        {
                            boundThis = RealmState.Engine is { GlobalObject: { } globalObj }
                                ? JsValue.FromObjectUnsafe(globalObj)
                                : JsValue.Undefined;
                        }

                        if (!boundThis.IsUndefined &&
                            !boundThis.IsNull &&
                            boundThis.ObjectValue is not IJsPropertyAccessor &&
                            boundThis.ObjectValue is not IIsHtmlDda)
                        {
                            boundThis = JsValue.FromObjectUnsafe(ToObjectForDestructuringJsValue(boundThis, context));
                        }
                    }

                    JsValue initialThisValue;
                    bool initialThisInitialized;
                    if (_isDerivedClassConstructor)
                    {
                        context.MarkThisUninitialized();
                        initialThisInitialized = false;
                        initialThisValue = JsValue.Uninitialized;
                    }
                    else
                    {
                        context.MarkThisInitialized();
                        initialThisInitialized = true;
                        initialThisValue = boundThis;
                        if (!_isStrict && initialThisValue.IsNull)
                        {
                            initialThisValue = JsValue.FromObjectUnsafe(new JsObject { RealmState = RealmState });
                        }

                        boundThis = initialThisValue;
                    }

                    functionEnvironment.SetThisInitializationStatus(initialThisInitialized);
                    functionEnvironment._thisValue = initialThisValue;
                    functionEnvironment._hasThisValue = true;
                    functionEnvironment.DefineJsValue(Symbol.This, initialThisValue);
                    if (_isDerivedClassConstructor)
                    {
                        functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                            JsValue.FromObjectUnsafe(functionEnvironment));
                    }

                    if (IsClassConstructor && initialThisValue.TryGetObject<JsObject>(out var ctorThis))
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
                                           !boundThis.IsUndefined &&
                                           !boundThis.IsUninitialized
                            ? boundThis
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
                        else if (boundThis.TryGetObject<JsObject>(out var thisInstance))
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

                IReadOnlyList<JsValue> currentArguments = arguments;
                var isLegacyTailRestart = false;
                try
                {
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
                    LegacyTailCallRestart:
                        context.ClearReturn();
                        if (isLegacyTailRestart)
                        {
                            ResetLegacyTailRestartActivation(varEnvironment, executionEnvironment);
                        }

                        // Create the `arguments` binding before parameter defaults can observe it.
                        // Legacy tail restarts update currentArguments, so the observable object must be refreshed here.
                        if (_argumentsObjectNeeded)
                        {
                            var argumentsObject = _function.CreateArgumentsObject(currentArguments, executionEnvironment,
                                RealmState,
                                this,
                                _isStrict);
                            executionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                                isLexicalBinding: false);
                            parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                                isLexicalBinding: false);
                            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
                            {
                                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                                    isLexicalBinding: false);
                            }
                        }

                        // Bind parameters
                        _function.BindFunctionParameters(currentArguments, parameterEnvironment, context);
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

                        if (context.TryConsumeLegacyTailCallRestart(
                                this,
                                out var restartArguments,
                                out var restartThisValue,
                                out var restartNewTargetValue))
                        {
                            currentArguments = restartArguments;
                            isLegacyTailRestart = true;
                            if (_isStrict && !IsArrowFunction)
                            {
                                functionEnvironment._thisValue = restartThisValue;
                                functionEnvironment._hasThisValue = true;
                                functionEnvironment.DefineJsValue(Symbol.This, restartThisValue);
                                functionEnvironment.DefineJsValue(Symbol.NewTarget, restartNewTargetValue, true,
                                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
                            }

                            goto LegacyTailCallRestart;
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
                                        if (_isDerivedClassConstructor &&
                                            (currentThis.IsUninitialized ||
                                             ReferenceEquals(currentThis.ObjectValue, JsEnvironment.Uninitialized)))
                                        {
                                            var errorObject = StandardLibrary.CreateReferenceError(
                                                "ReferenceError: this is not defined - must call super() in derived class constructor",
                                                context,
                                                constructErrorRealm);
                                            throw new ThrowSignal(errorObject);
                                        }

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
                global::Asynkron.JsEngine.Runtime.RealmState.Current = previousRealm;
                _currentCaller = previousCaller;
                t_currentlyExecuting = previouslyExecuting;
            }
        }

        private JsValue ExecuteClassifiedSyncFunctionIrResidue<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            JsValue newTarget,
            ExecutionPlan plan,
            EvaluationContext context,
            EvaluationContext? callingContext,
            RealmState constructErrorRealm)
            where TArgs : IReadOnlyList<JsValue>
        {
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
                if (effectiveNewTarget.IsUndefined && !_lexicalNewTarget.IsUndefined)
                {
                    effectiveNewTarget = _lexicalNewTarget;
                }
            }

            RealmState.Logger?.LogInformation(
                "classified-sync-function-ir-residue reason=production-unified-bytecode-declined func={Function} argc={ArgumentCount} classConstructor={ClassConstructor} privateNameResidue={PrivateNameResidue}",
                _function.Name?.Name ?? "<anonymous>",
                arguments.Count,
                IsClassConstructor,
                PrivateNameScope is not null || !_capturedPrivateNameScopes.IsDefaultOrEmpty);

            try
            {
                return ExecutionPlanRunner.ExecuteClassifiedSyncFunctionIrResidue(
                    _function,
                    _closure,
                    arguments,
                    effectiveThisValue,
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
                    constructErrorRealm,
                    plan,
                    _planSeed.Failure);
            }
            catch (ThrowSignal signal) when (callingContext is not null)
            {
                var thrownValue = signal.ThrownValue;
                if (_isDerivedClassConstructor)
                {
                    var normalized = NormalizeDerivedClassRealmError(signal, callingContext);
                    if (!normalized.IsUndefined)
                    {
                        thrownValue = normalized;
                    }
                }

                callingContext.SetThrow(thrownValue);
                return thrownValue;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool TryInvokeIrFast<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            EvaluationContext? callingContext,
            JsValue newTarget,
            ExecutionPlan plan,
            EvaluationContext context,
            out JsValue result)
            where TArgs : IReadOnlyList<JsValue>
        {
            result = JsValue.Undefined;

            if (CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath(plan, newTarget) &&
                TryInvokeProductionUnifiedBytecode(arguments, thisValue, newTarget, plan, context, callingContext, out result))
            {
                return true;
            }

            var canUseSimpleIrActivationFastPath = CanUseSimpleIrActivationFastPath(plan, newTarget);
            if (canUseSimpleIrActivationFastPath &&
                plan.SimpleReturnParameterBinary is { } parameterBinary)
            {
                RealmState.Logger?.LogInformation(
                    "simple-ir-parameter-binary-fast-path func={Function} argc={ArgumentCount}",
                    _function.Name?.Name ?? "<anonymous>",
                    arguments.Count);
                RealmState.Logger?.LogInformation(
                    "simple-ir-return-fast-path func={Function} argc={ArgumentCount}",
                    _function.Name?.Name ?? "<anonymous>",
                    arguments.Count);

                result = EvaluateSimpleReturnParameterBinary(arguments, parameterBinary, context);
                return TryCompleteIrFastExpressionResult(context, callingContext, ref result);
            }

            if (plan.SimpleReturnParameterBinaryChain is { } parameterBinaryChain &&
                SyncIrCallTrampoline.CanUseDirectReturnFastPath(this, plan, newTarget))
            {
                RealmState.Logger?.LogInformation(
                    "simple-ir-parameter-binary-chain-fast-path func={Function} argc={ArgumentCount}",
                    _function.Name?.Name ?? "<anonymous>",
                    arguments.Count);
                RealmState.Logger?.LogInformation(
                    "simple-ir-return-fast-path func={Function} argc={ArgumentCount}",
                    _function.Name?.Name ?? "<anonymous>",
                    arguments.Count);

                result = EvaluateSimpleReturnParameterBinaryChain(arguments, parameterBinaryChain, context);
                return TryCompleteIrFastExpressionResult(context, callingContext, ref result);
            }

            if (SyncIrCallTrampoline.TryInvoke(
                    this,
                    arguments,
                    thisValue,
                    context,
                    newTarget,
                    plan,
                    out result))
            {
                return TryCompleteIrFastExpressionResult(context, callingContext, ref result);
            }

            if (!canUseSimpleIrActivationFastPath)
            {
                return false;
            }

            RealmState.Logger?.LogInformation(
                "simple-ir-activation-runner-declined func={Function} argc={ArgumentCount}",
                _function.Name?.Name ?? "<anonymous>",
                arguments.Count);
            return false;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool ShouldDeferSimpleIrFastPathToProductionUnifiedBytecode(JsValue newTarget)
        {
            if (_planSeed.Plan is not { } plan ||
                plan.IsProductionEligibilityPermanentDecline)
            {
                return false;
            }

            if (ReferenceEquals(_unifiedBytecodeProductionEligibilityPlan, plan) &&
                _unifiedBytecodeProductionEligibility != UnifiedBytecodeEligibilityUnknown)
            {
                return _unifiedBytecodeProductionEligibility == UnifiedBytecodeEligibilityAccepted;
            }

            return CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath(plan, newTarget);
        }

        private bool TryInvokeProductionUnifiedBytecode<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            JsValue newTarget,
            ExecutionPlan plan,
            EvaluationContext context,
            EvaluationContext? callingContext,
            out JsValue result,
            RealmState? derivedClassErrorRealm = null)
            where TArgs : IReadOnlyList<JsValue>
        {
            result = JsValue.Undefined;
            if (!TryGetProductionUnifiedBytecodeProgram(plan, newTarget, out var program))
            {
                return false;
            }

            var slotStorage = RentProductionUnifiedBytecodeSlots(program.SlotCount, out var returnToPool);
            JsEnvironment? executionEnvironment = null;
            var hasPendingFieldInitialization = false;
            try
            {
                var vmThisValue = thisValue;
                var vmNewTarget = newTarget;
                if (IsArrowFunction)
                {
                    var lexicalThis = _lexicalThis;
                    if (_lexicalThisEnvironment is not null &&
                        _lexicalThisEnvironment.TryFindBindingJsValue(Symbol.This, true, out _, out var envThis))
                    {
                        lexicalThis = envThis;
                    }

                    vmThisValue = lexicalThis.IsUninitialized ? JsValue.Undefined : lexicalThis;
                    if (vmNewTarget.IsUndefined && !_lexicalNewTarget.IsUndefined)
                    {
                        vmNewTarget = _lexicalNewTarget;
                    }
                }

                if (IsClassConstructor && !_isDerivedClassConstructor)
                {
                    if (vmThisValue.IsUndefined)
                    {
                        vmThisValue = JsValue.FromObjectUnsafe(CreateConstructedThis(vmNewTarget, RealmState));
                    }
                    else if (!vmThisValue.IsObject)
                    {
                        return false;
                    }

                    context.MarkThisInitialized();
                }

                using var capturedPrivateScopes = !_capturedPrivateNameScopes.IsDefaultOrEmpty
                    ? context.EnterPrivateNameScopes(_capturedPrivateNameScopes)
                    : null;
                using var privateScope = PrivateNameScope is not null
                    ? context.EnterPrivateNameScope(PrivateNameScope)
                    : null;

                if (IsClassConstructor &&
                    !_isDerivedClassConstructor &&
                    (PrivateNameScope is not null || !_instanceFields.IsDefaultOrEmpty))
                {
                    if (!vmThisValue.TryGetObject<IJsObjectLike>(out var constructedInstance))
                    {
                        return false;
                    }

                    var initEnvironment = JsEnvironment.CreateInstance(_closure, isStrict: _isStrict);
                    InitializeInstance(constructedInstance, initEnvironment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        result = context.FlowValue;
                        return TryCompleteIrFastExpressionResult(context, callingContext, ref result);
                    }
                }

                var slots = slotStorage.AsSpan(0, program.SlotCount);
                slots.Fill(JsValue.Undefined);
                InitializeProductionUnifiedBytecodeLexicalSlots(slots, program);
                var defaultDerivedRestArguments = _function.IsDefaultDerivedConstructor
                    ? CreateDefaultDerivedConstructorRestArguments(arguments)
                    : (JsValue?)null;
                PopulateProductionUnifiedBytecodeParameterSlots(
                    arguments,
                    slots,
                    program,
                    defaultDerivedRestArguments);
                var boundThis = IsArrowFunction || _isStrict
                    ? vmThisValue
                    : CoerceThisValueForNonStrict(vmThisValue);
                if (IsClassConstructor && !_isDerivedClassConstructor)
                {
                    executionEnvironment = CreateSimpleBaseClassConstructorEnvironment(
                        arguments,
                        vmThisValue,
                        vmNewTarget,
                        plan);
                }
                else if (_hasFunctionDeclarations || RequiresProductionUnifiedBytecodeCallEnvironment(program))
                {
                    executionEnvironment = IsClassConstructor && _isDerivedClassConstructor
                        ? CreateSimpleDerivedClassConstructorEnvironment(
                            arguments,
                            vmNewTarget,
                            plan,
                            defaultDerivedRestArguments)
                        : CreateSimpleIrActivationEnvironment(arguments, vmThisValue, plan, context, vmNewTarget);
                }

                if (IsClassConstructor &&
                    _isDerivedClassConstructor &&
                    (PrivateNameScope is not null || !_instanceFields.IsDefaultOrEmpty) &&
                    executionEnvironment?.Enclosing is { } functionEnvironment)
                {
                    context.PushClassFieldInitializer(
                        new PendingClassFieldInitialization(this, functionEnvironment));
                    hasPendingFieldInitialization = true;
                }

                RealmState.Logger?.LogInformation(
                    "unified-bytecode-production-fast-path func={Function} argc={ArgumentCount}",
                    _function.Name?.Name ?? "<anonymous>",
                    arguments.Count);

                result = UnifiedBytecodeVirtualMachine.Execute(
                    program,
                    slots,
                    context,
                    executionEnvironment,
                    boundThis,
                    vmNewTarget,
                    _isStrict);
                CompleteProductionUnifiedBytecodeClassConstructorResult(
                    executionEnvironment,
                    context,
                    derivedClassErrorRealm ?? RealmState,
                    ref result);
                return TryCompleteIrFastExpressionResult(context, callingContext, ref result);
            }
            finally
            {
                if (hasPendingFieldInitialization)
                {
                    context.RemovePendingClassFieldInitializer(this);
                }

                ReturnSimpleIrActivationEnvironment(executionEnvironment);
                ReturnProductionUnifiedBytecodeSlots(slotStorage, program.SlotCount, returnToPool);
            }
        }

        private JsValue[] RentProductionUnifiedBytecodeSlots(int slotCount, out bool returnToPool)
        {
            if (IsClassConstructor &&
                slotCount <= MaxCachedProductionUnifiedBytecodeSlotCount &&
                Interlocked.Exchange(ref _productionUnifiedBytecodeSlotStorageInUse, 1) == 0)
            {
                var slotStorage = _productionUnifiedBytecodeSlotStorage;
                if (slotStorage is null || slotStorage.Length < slotCount)
                {
                    slotStorage = new JsValue[slotCount];
                    _productionUnifiedBytecodeSlotStorage = slotStorage;
                }

                returnToPool = false;
                return slotStorage;
            }

            returnToPool = true;
            return ArrayPool<JsValue>.Shared.Rent(slotCount);
        }

        private void ReturnProductionUnifiedBytecodeSlots(
            JsValue[] slotStorage,
            int slotCount,
            bool returnToPool)
        {
            if (returnToPool)
            {
                ArrayPool<JsValue>.Shared.Return(slotStorage, clearArray: true);
                return;
            }

            slotStorage.AsSpan(0, slotCount).Clear();
            Volatile.Write(ref _productionUnifiedBytecodeSlotStorageInUse, 0);
        }

        private void CompleteProductionUnifiedBytecodeClassConstructorResult(
            JsEnvironment? executionEnvironment,
            EvaluationContext context,
            RealmState derivedClassErrorRealm,
            ref JsValue result)
        {
            if (!IsClassConstructor)
            {
                return;
            }

            if (context.IsThrow)
            {
                result = context.FlowValue;
                return;
            }

            if (result.IsObject)
            {
                return;
            }

            if (_isDerivedClassConstructor)
            {
                if (!result.IsUndefined)
                {
                    context.SetThrow(StandardLibrary.CreateTypeError(
                        "Derived constructors may only return object or undefined",
                        context,
                        derivedClassErrorRealm));
                    result = context.FlowValue;
                    return;
                }

                if (executionEnvironment is null ||
                    !executionEnvironment.TryGetJsValue(Symbol.This, out var derivedThis) ||
                    derivedThis.IsUninitialized ||
                    ReferenceEquals(derivedThis.ObjectValue, JsEnvironment.Uninitialized))
                {
                    context.SetThrow(StandardLibrary.CreateReferenceError(
                        "ReferenceError: this is not defined - must call super() in derived class constructor",
                        context,
                        derivedClassErrorRealm));
                    result = context.FlowValue;
                    return;
                }

                result = derivedThis;
                return;
            }

            if (executionEnvironment is not null &&
                executionEnvironment.TryGetJsValue(Symbol.This, out var thisValue))
            {
                result = thisValue;
            }
        }

        private static bool RequiresProductionUnifiedBytecodeCallEnvironment(UnifiedBytecodeProgram program)
        {
            var instructions = program.Instructions;
            for (var i = 0; i < instructions.Length; i++)
            {
                if (instructions[i].OpCode is
                    UnifiedBytecodeOpCode.CallInvocationBoundary or
                    UnifiedBytecodeOpCode.SuperConstructInvocationBoundary or
                    UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget or
                    UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget or
                    UnifiedBytecodeOpCode.EnsureSuperReference or
                    UnifiedBytecodeOpCode.GetNamedSuperProperty or
                    UnifiedBytecodeOpCode.GetComputedSuperProperty or
                    UnifiedBytecodeOpCode.SetNamedSuperProperty or
                    UnifiedBytecodeOpCode.SetComputedSuperProperty or
                    UnifiedBytecodeOpCode.UpdateNamedSuperProperty or
                    UnifiedBytecodeOpCode.UpdateComputedSuperProperty or
                    UnifiedBytecodeOpCode.DeclareDynamicVar or
                    UnifiedBytecodeOpCode.DeclareDynamicLexical or
                    UnifiedBytecodeOpCode.InitializeDynamicLexical or
                    UnifiedBytecodeOpCode.LoadImportMeta or
                    UnifiedBytecodeOpCode.LoadDynamicIdentifier or
                    UnifiedBytecodeOpCode.ResolveDynamicIdentifierReference or
                    UnifiedBytecodeOpCode.LoadDynamicIdentifierReference or
                    UnifiedBytecodeOpCode.StoreDynamicIdentifierReference or
                    UnifiedBytecodeOpCode.PopDynamicIdentifierReference or
                    UnifiedBytecodeOpCode.UpdateDynamicIdentifier or
                    UnifiedBytecodeOpCode.TypeOfDynamicIdentifier or
                    UnifiedBytecodeOpCode.DeleteDynamicIdentifier or
                    UnifiedBytecodeOpCode.PrepareDynamicIdentifierCallTarget or
                    UnifiedBytecodeOpCode.PrepareDynamicIdentifierOptionalCallTarget or
                    UnifiedBytecodeOpCode.ApplyBindingTarget or
                    UnifiedBytecodeOpCode.ApplyDeclarationBindingTarget or
                    UnifiedBytecodeOpCode.DeclareClass or
                    UnifiedBytecodeOpCode.DeclareFunction or
                    UnifiedBytecodeOpCode.LoadFunctionLiteral or
                    UnifiedBytecodeOpCode.EnterWith or
                    UnifiedBytecodeOpCode.LeaveWith or
                    UnifiedBytecodeOpCode.LoadFunctionLiteral or
                    UnifiedBytecodeOpCode.LoadClassLiteral)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetProductionUnifiedBytecodeProgram(
            ExecutionPlan plan,
            JsValue newTarget,
            out UnifiedBytecodeProgram program)
        {
            // Fast path: plan-level structural decline is permanent across all SyncFunctionInvoker
            // instances for the same FunctionExpression — skip the full eligibility re-evaluation.
            if (plan.IsProductionEligibilityPermanentDecline)
            {
                program = default!;
                return false;
            }

            if (ReferenceEquals(_unifiedBytecodeProductionEligibilityPlan, plan) &&
                _unifiedBytecodeProductionEligibility != UnifiedBytecodeEligibilityUnknown &&
                _unifiedBytecodeProductionEligibilityNewTargetIsUndefined == newTarget.IsUndefined)
            {
                program = _unifiedBytecodeProductionProgram!;
                return _unifiedBytecodeProductionEligibility == UnifiedBytecodeEligibilityAccepted;
            }

            var canUseImplicitArgumentsObjectDependencyPath =
                CanUseProductionUnifiedBytecodeImplicitArgumentsObjectDependencyPath(plan);
            var canUseFinalRestParameterPath =
                CanUseProductionUnifiedBytecodeFinalRestParameterPath(
                    canUseImplicitArgumentsObjectDependencyPath);
            var canUseSimpleLiteralDefaultParameterPath =
                CanUseProductionUnifiedBytecodeSimpleLiteralDefaultParameterPath(
                    canUseImplicitArgumentsObjectDependencyPath);
            var result = UnifiedBytecodeProductionEligibility.Evaluate(
                plan,
                CreateProductionUnifiedBytecodeActivationDescriptor(
                    CanUseProductionUnifiedBytecodeDynamicNameFastPath(),
                    CanUseProductionUnifiedBytecodeOrdinaryDynamicNameFastPath(plan),
                    CanUseProductionUnifiedBytecodeArrowFunctionActivation(plan, newTarget),
                    CanUseProductionUnifiedBytecodeCapturedClosureActivation(plan, newTarget),
                    CanUseProductionUnifiedBytecodeDerivedClassConstructorActivation(plan, newTarget),
                    CanUseProductionUnifiedBytecodeBaseClassConstructorActivation(plan, newTarget),
                    canUseImplicitArgumentsObjectDependencyPath,
                    allowImplicitArgumentsObjectPropertyReadOperands:
                        canUseImplicitArgumentsObjectDependencyPath &&
                        (canUseSimpleLiteralDefaultParameterPath ||
                         canUseFinalRestParameterPath)));

            if (!result.IsEligible && IsPlanStructuralDecline(result.Code))
            {
                // Plan structure will never satisfy production unified-bytecode — mark permanently
                // so future SyncFunctionInvoker instances for this plan skip re-evaluation.
                plan.MarkProductionEligibilityPermanentDecline();
            }

            _unifiedBytecodeProductionEligibilityPlan = plan;
            _unifiedBytecodeProductionEligibilityNewTargetIsUndefined = newTarget.IsUndefined;
            _unifiedBytecodeProductionEligibility = result.IsEligible
                ? UnifiedBytecodeEligibilityAccepted
                : UnifiedBytecodeEligibilityRejected;
            _unifiedBytecodeProductionProgram = result.IsEligible ? result.Program : null;

            program = result.Program;
            return result.IsEligible;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private bool CanUseCachedOrEvaluateProductionUnifiedBytecodeFastPath(
            ExecutionPlan plan,
            JsValue newTarget)
        {
            if (ReferenceEquals(_unifiedBytecodeProductionEligibilityPlan, plan) &&
                _unifiedBytecodeProductionEligibility != UnifiedBytecodeEligibilityUnknown &&
                _unifiedBytecodeProductionEligibilityNewTargetIsUndefined == newTarget.IsUndefined)
            {
                return _unifiedBytecodeProductionEligibility == UnifiedBytecodeEligibilityAccepted;
            }

            return CanUseProductionUnifiedBytecodeFastPath(plan, newTarget);
        }

        private void InvalidateProductionUnifiedBytecodeEligibilityCache()
        {
            _unifiedBytecodeProductionEligibilityPlan = null;
            _unifiedBytecodeProductionEligibility = UnifiedBytecodeEligibilityUnknown;
            _unifiedBytecodeProductionProgram = null;
        }

        private static bool IsPlanStructuralDecline(UnifiedBytecodeProductionDeclineCode code)
        {
            // A decline is plan-structural when it is determined purely by the function's instruction
            // set and is the same for every invocation regardless of the calling closure's runtime state.
            // These declines can be cached on the ExecutionPlan to skip re-evaluation on every IIFE call.
            //
            // After CanUseProductionUnifiedBytecodeFastPath has passed:
            //   • DynamicLookupDependency: HasDynamicLookupDependency is gated out by fast-path guard
            //     (!_allowIdentifierCache && !canUseDynamic), so any DynamicLookupDependency from
            //     Evaluate is plan-structural (e.g. a global like Math not in activation slots).
            //   • CallDependency: unowned call shapes are declined from the plan scan.
            return code is not (
                UnifiedBytecodeProductionDeclineCode.AsyncLikeFunction or
                UnifiedBytecodeProductionDeclineCode.GeneratorFunction or
                UnifiedBytecodeProductionDeclineCode.CapturedOrDynamicActivation or
                UnifiedBytecodeProductionDeclineCode.ArgumentsObjectDependency or
                UnifiedBytecodeProductionDeclineCode.ArrowLexicalThisDependency or
                UnifiedBytecodeProductionDeclineCode.ClassConstructorActivation);
        }

        private bool CanUseProductionUnifiedBytecodeFastPath(ExecutionPlan plan, JsValue newTarget)
        {
            if (_function.IsDynamicFunctionConstructorBody)
            {
                return false;
            }

            if (ScriptFastPathBlockBindingLeakDetector.HasOutOfScopeForHeadBindingReference(_function.Body))
            {
                return false;
            }

            var canUseDynamicNamePath = CanUseProductionUnifiedBytecodeDynamicNameFastPath();
            var canUseOrdinaryDynamicNamePath = CanUseProductionUnifiedBytecodeOrdinaryDynamicNameFastPath(plan);
            var canUseArrowFunctionPath = CanUseProductionUnifiedBytecodeArrowFunctionActivation(plan, newTarget);
            var canUseCapturedClosurePath =
                CanUseProductionUnifiedBytecodeCapturedClosureActivation(plan, newTarget);
            var canUseDerivedClassConstructorPath =
                CanUseProductionUnifiedBytecodeDerivedClassConstructorActivation(plan, newTarget);
            var canUseBaseClassConstructorPath =
                CanUseProductionUnifiedBytecodeBaseClassConstructorActivation(plan, newTarget);
            var canUseClassConstructorCapturedClosurePath =
                (canUseDerivedClassConstructorPath || canUseBaseClassConstructorPath) &&
                _hasCapturedActivationInClosure &&
                !_hasClosureWithObject;
            var canUseImplicitArgumentsObjectDependencyPath =
                CanUseProductionUnifiedBytecodeImplicitArgumentsObjectDependencyPath(plan);
            var canUseFinalRestParameterPath =
                CanUseProductionUnifiedBytecodeFinalRestParameterPath(
                    canUseImplicitArgumentsObjectDependencyPath);
            var canUseSimpleLiteralDefaultParameterPath =
                CanUseProductionUnifiedBytecodeSimpleLiteralDefaultParameterPath(
                    canUseImplicitArgumentsObjectDependencyPath);
            var hasAdmittedParameterShape =
                _hasOnlySimpleIdentifierParameters ||
                canUseSimpleLiteralDefaultParameterPath ||
                canUseFinalRestParameterPath ||
                canUseDerivedClassConstructorPath ||
                canUseBaseClassConstructorPath;
            var activation = CreateProductionUnifiedBytecodeActivationDescriptor(
                canUseDynamicNamePath,
                canUseOrdinaryDynamicNamePath,
                canUseArrowFunctionPath,
                canUseCapturedClosurePath,
                canUseDerivedClassConstructorPath,
                canUseBaseClassConstructorPath,
                canUseImplicitArgumentsObjectDependencyPath);
            if (UnifiedBytecodeProductionEligibility.TryFindOrdinarySyncActivationDecline(
                    activation,
                    out _,
                    out _) ||
                _hasParameterExpressions &&
                !canUseSimpleLiteralDefaultParameterPath &&
                !canUseDerivedClassConstructorPath &&
                !canUseBaseClassConstructorPath ||
                !hasAdmittedParameterShape ||
                !_instanceFields.IsDefaultOrEmpty &&
                !canUseDerivedClassConstructorPath &&
                !canUseBaseClassConstructorPath)
            {
                return false;
            }

            return CanUseProductionUnifiedBytecodePlanShape(
                       plan,
                       canUseDynamicNamePath ||
                       canUseOrdinaryDynamicNamePath ||
                       canUseArrowFunctionPath ||
                       canUseCapturedClosurePath ||
                       canUseClassConstructorCapturedClosurePath ||
                       canUseImplicitArgumentsObjectDependencyPath) ||
                   (canUseDerivedClassConstructorPath || canUseBaseClassConstructorPath) &&
                   plan.ActivationSlots is not null;
        }

        private UnifiedBytecodeProductionActivationDescriptor CreateProductionUnifiedBytecodeActivationDescriptor()
        {
            var canUseDynamicNamePath = CanUseProductionUnifiedBytecodeDynamicNameFastPath();
            return CreateProductionUnifiedBytecodeActivationDescriptor(
                canUseDynamicNamePath,
                canUseOrdinaryDynamicNamePath: false,
                canUseArrowFunctionPath: false);
        }

        private UnifiedBytecodeProductionActivationDescriptor CreateProductionUnifiedBytecodeActivationDescriptor(
            bool canUseDynamicNamePath,
            bool canUseOrdinaryDynamicNamePath,
            bool canUseArrowFunctionPath = false,
            bool canUseCapturedClosurePath = false,
            bool canUseDerivedClassConstructorPath = false,
            bool canUseBaseClassConstructorPath = false,
            bool canUseImplicitArgumentsObjectDependencyPath = false,
            bool allowImplicitArgumentsObjectPropertyReadOperands = false)
        {
            var canUseClassConstructorActivationPath =
                canUseDerivedClassConstructorPath || canUseBaseClassConstructorPath;
            var canUseClassConstructorCapturedClosurePath =
                canUseClassConstructorActivationPath &&
                _hasCapturedActivationInClosure &&
                !_hasClosureWithObject;
            var hasUnprovenDynamicActivation = !_allowIdentifierCache &&
                                               !canUseDynamicNamePath &&
                                               !canUseOrdinaryDynamicNamePath &&
                                               !canUseImplicitArgumentsObjectDependencyPath &&
                                               !_hasDirectEvalInBodyOrParameters;
            return new UnifiedBytecodeProductionActivationDescriptor(
                IsAsyncLike: IsAsyncLike,
                IsGenerator: _function.IsGenerator,
                HasCapturedOrDynamicActivation:
                    _hasCapturedActivationInClosure &&
                    !canUseArrowFunctionPath &&
                    !canUseCapturedClosurePath &&
                    !canUseClassConstructorActivationPath ||
                    _hasClosureWithObject && !canUseDynamicNamePath ||
                    hasUnprovenDynamicActivation,
                HasArgumentsObjectDependency:
                    !_hasDirectEvalInBodyOrParameters &&
                    _argumentsObjectNeeded &&
                    !canUseImplicitArgumentsObjectDependencyPath &&
                    (_usesArguments || _needsArgumentsBinding && !canUseDynamicNamePath),
                HasArrowLexicalThisDependency:
                    IsArrowFunction && !canUseArrowFunctionPath || _lexicalThisEnvironment is not null,
                HasClassConstructorActivation:
                    IsClassConstructor && !canUseDerivedClassConstructorPath && !canUseBaseClassConstructorPath,
                HasDynamicLookupDependency: hasUnprovenDynamicActivation,
                AllowsOrdinaryDynamicIdentifierEnvironmentOperations:
                    canUseOrdinaryDynamicNamePath ||
                    canUseImplicitArgumentsObjectDependencyPath ||
                    canUseArrowFunctionPath ||
                    canUseCapturedClosurePath ||
                    canUseClassConstructorCapturedClosurePath,
                AllowsImplicitArgumentsObjectPropertyReadOperands:
                    allowImplicitArgumentsObjectPropertyReadOperands,
                AllowsMaterializedBodyEnvironmentFunctionLiterals:
                    canUseCapturedClosurePath ||
                    canUseClassConstructorCapturedClosurePath,
                IsStrict: _isStrict);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static void InitializeProductionUnifiedBytecodeLexicalSlots(
            Span<JsValue> slots,
            UnifiedBytecodeProgram program)
        {
            var lexicalSlotIndices = program.LexicalSlotIndices;
            if (lexicalSlotIndices.IsDefaultOrEmpty)
            {
                return;
            }

            for (var i = 0; i < lexicalSlotIndices.Length; i++)
            {
                slots[lexicalSlotIndices[i]] = JsValue.Uninitialized;
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private void PopulateProductionUnifiedBytecodeParameterSlots<TArgs>(
            TArgs arguments,
            Span<JsValue> slots,
            UnifiedBytecodeProgram program,
            JsValue? defaultDerivedRestArguments = null)
            where TArgs : IReadOnlyList<JsValue>
        {
            var parameterSlotIndices = program.ParameterSlotIndices;
            if (parameterSlotIndices.IsDefault)
            {
                return;
            }

            var hasFinalRestParameter =
                TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out var finalRestParameterIndex);
            for (var i = 0; i < _parameterNames.Length; i++)
            {
                var parameterSlotIndex = parameterSlotIndices[i];
                if (parameterSlotIndex >= 0)
                {
                    JsValue value;
                    if (defaultDerivedRestArguments is { } restArguments &&
                        TryGetDefaultDerivedConstructorRestParameter(out _) &&
                        i == 0)
                    {
                        value = restArguments;
                    }
                    else if (hasFinalRestParameter && i == finalRestParameterIndex)
                    {
                        value = CreateRestArguments(arguments, finalRestParameterIndex);
                    }
                    else
                    {
                        value = GetProductionUnifiedBytecodeParameterValue(
                            arguments,
                            i,
                            hasFinalRestParameter,
                            finalRestParameterIndex);
                    }

                    slots[parameterSlotIndex] = value;
                }
            }
        }

        private bool CanUseProductionUnifiedBytecodeSimpleLiteralDefaultParameterPath(
            bool allowArgumentsObjectDependency = false)
        {
            return !IsClassConstructor &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   !_function.IsDefaultDerivedConstructor &&
                   _hasParameterExpressions &&
                   (!_usesArguments && !_needsArgumentsBinding ||
                    allowArgumentsObjectDependency && !IsArrowFunction) &&
                   _allowIdentifierCache &&
                   _lexicalThisEnvironment is null &&
                   _homeObject is null &&
                   PrivateNameScope is null &&
                   _capturedPrivateNameScopes.IsDefaultOrEmpty &&
                   _superConstructor is null &&
                   _superPrototype is null &&
                   _instanceFields.IsDefaultOrEmpty &&
                   HasOnlySimpleIdentifierOrLiteralDefaultParameters();
        }

        private bool HasOnlySimpleIdentifierOrLiteralDefaultParameters()
        {
            var sawLiteralDefault = false;
            foreach (var parameter in _function.Parameters)
            {
                if (parameter is not { IsRest: false, Pattern: null, Name: not null })
                {
                    return false;
                }

                if (parameter.DefaultValue is null)
                {
                    continue;
                }

                if (parameter.DefaultValue is not LiteralExpression)
                {
                    return false;
                }

                sawLiteralDefault = true;
            }

            return sawLiteralDefault;
        }

        private bool CanUseProductionUnifiedBytecodeFinalRestParameterPath(
            bool allowArgumentsObjectDependency = false)
        {
            return !IsClassConstructor &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   !_function.IsDefaultDerivedConstructor &&
                   !_hasParameterExpressions &&
                   (!_usesArguments && !_needsArgumentsBinding ||
                    allowArgumentsObjectDependency && !IsArrowFunction) &&
                   _allowIdentifierCache &&
                   _lexicalThisEnvironment is null &&
                   _homeObject is null &&
                   PrivateNameScope is null &&
                   _capturedPrivateNameScopes.IsDefaultOrEmpty &&
                   _superConstructor is null &&
                   _superPrototype is null &&
                   _instanceFields.IsDefaultOrEmpty &&
                   TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out _);
        }

        private bool TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out int restIndex)
        {
            restIndex = -1;
            if (_function.Parameters.IsDefaultOrEmpty)
            {
                return false;
            }

            for (var i = 0; i < _function.Parameters.Length; i++)
            {
                var parameter = _function.Parameters[i];
                var isFinal = i == _function.Parameters.Length - 1;
                if (isFinal)
                {
                    if (parameter is { IsRest: true, Pattern: null, DefaultValue: null, Name: not null })
                    {
                        restIndex = i;
                        return true;
                    }

                    return false;
                }

                if (parameter is not { IsRest: false, Pattern: null, DefaultValue: null, Name: not null })
                {
                    return false;
                }
            }

            return false;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue EvaluateSimpleReturnParameterBinary<TArgs>(
            TArgs arguments,
            SimpleReturnParameterBinaryExpression expression,
            EvaluationContext context)
            where TArgs : IReadOnlyList<JsValue>
        {
            var left = GetSimpleReturnParameterArgument(arguments, expression.LeftParameterIndex);
            var right = GetSimpleReturnParameterArgument(arguments, expression.RightParameterIndex);

            return expression.Operator switch
            {
                BinaryOperator.Add => AddValue(left, right, context),
                BinaryOperator.Subtract => SubtractValue(left, right, context),
                BinaryOperator.Multiply => MultiplyValue(left, right, context),
                BinaryOperator.Divide => DivideValue(left, right, context),
                _ => JsValue.Undefined
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue EvaluateSimpleReturnParameterBinaryChain<TArgs>(
            TArgs arguments,
            SimpleReturnParameterBinaryChainExpression expression,
            EvaluationContext context)
            where TArgs : IReadOnlyList<JsValue>
        {
            var left = GetSimpleReturnParameterArgument(arguments, expression.LeftParameterIndex);
            var right = GetSimpleReturnParameterArgument(arguments, expression.RightParameterIndex);
            var firstResult = EvaluateSimpleReturnParameterBinaryOperator(
                expression.FirstOperator,
                left,
                right,
                context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var third = GetSimpleReturnParameterArgument(arguments, expression.ThirdParameterIndex);
            return EvaluateSimpleReturnParameterBinaryOperator(
                expression.SecondOperator,
                firstResult,
                third,
                context);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue EvaluateSimpleReturnParameterBinaryOperator(
            BinaryOperator op,
            in JsValue left,
            in JsValue right,
            EvaluationContext context)
        {
            return op switch
            {
                BinaryOperator.Add => AddValue(left, right, context),
                BinaryOperator.Subtract => SubtractValue(left, right, context),
                BinaryOperator.Multiply => MultiplyValue(left, right, context),
                BinaryOperator.Divide => DivideValue(left, right, context),
                BinaryOperator.BitwiseXor => BitwiseXorValue(left, right, context),
                _ => JsValue.Undefined
            };
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static JsValue GetSimpleReturnParameterArgument<TArgs>(TArgs arguments, int index)
            where TArgs : IReadOnlyList<JsValue>
        {
            return index < arguments.Count ? arguments[index] : JsValue.Undefined;
        }

        private static bool TryCompleteIrFastExpressionResult(
            EvaluationContext context,
            EvaluationContext? callingContext,
            ref JsValue result)
        {
            if (!context.IsThrow)
            {
                return true;
            }

            var thrownValue = context.FlowValue;
            context.Clear();
            if (callingContext is not null)
            {
                callingContext.SetThrow(thrownValue);
                result = thrownValue;
                return true;
            }

            throw new ThrowSignal(thrownValue);
        }

        private bool CanUseSimpleIrActivationFastPath(ExecutionPlan plan, JsValue newTarget)
        {
            if (!_canUseSimpleIrActivationFastBase ||
                !newTarget.IsUndefined ||
                IsClassConstructor ||
                IsArrowFunction && !CanUseSimpleIrActivationArrowFastPath(plan) ||
                IsAsyncLike ||
                _function.IsGenerator ||
                _function.IsDefaultDerivedConstructor ||
                _hasParameterExpressions ||
                !_hasOnlySimpleIdentifierParameters ||
                // _argumentsObjectNeeded is intentionally omitted: the fast path skips creating the
                // arguments object, which is safe when _usesArguments and _needsArgumentsBinding are
                // both false — the IR plan has no instructions that access the arguments binding.
                _usesArguments ||
                _needsArgumentsBinding ||
                !_allowIdentifierCache ||
                _lexicalThisEnvironment is not null ||
                !CanUseSimpleIrActivationHomeObjectPath(plan) ||
                PrivateNameScope is not null ||
                !_capturedPrivateNameScopes.IsDefaultOrEmpty ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                !_instanceFields.IsDefaultOrEmpty)
            {
                return false;
            }

            return CanUseSimpleIrActivationPlanShape(plan);
        }

        private bool CanUseProductionUnifiedBytecodeDerivedClassConstructorActivation(
            ExecutionPlan plan,
            JsValue newTarget)
        {
            var hasAdmittedParameterShape =
                !_hasParameterExpressions && _hasOnlySimpleIdentifierParameters ||
                _hasParameterExpressions && HasOnlySimpleIdentifierOrLiteralDefaultParameters() ||
                !_hasParameterExpressions && TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out _) ||
                _function.IsDefaultDerivedConstructor && TryGetDefaultDerivedConstructorRestParameter(out _);
            var hasSuperBinding = _superConstructor is not null || _superPrototype is not null;
            var hasAdmittedPlanShape = CanUseProductionUnifiedBytecodeDerivedClassConstructorPlanShape(plan);
            return IsClassConstructor &&
                   _isDerivedClassConstructor &&
                   !newTarget.IsUndefined &&
                   !IsArrowFunction &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   hasAdmittedParameterShape &&
                   !_usesArguments &&
                   !_needsArgumentsBinding &&
                   _lexicalThisEnvironment is null &&
                   _homeObject is null &&
                   hasSuperBinding &&
                   hasAdmittedPlanShape;
        }

        private bool CanUseProductionUnifiedBytecodeArrowFunctionActivation(
            ExecutionPlan plan,
            JsValue newTarget)
        {
            return IsArrowFunction &&
                   newTarget.IsUndefined &&
                   !IsClassConstructor &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   !_function.IsDefaultDerivedConstructor &&
                   (!_hasParameterExpressions && _hasOnlySimpleIdentifierParameters ||
                    CanUseProductionUnifiedBytecodeSimpleLiteralDefaultParameterPath() ||
                    CanUseProductionUnifiedBytecodeFinalRestParameterPath()) &&
                   !_usesArguments &&
                   !_needsArgumentsBinding &&
                   _allowIdentifierCache &&
                   _lexicalThisEnvironment is null &&
                   _homeObject is null &&
                   PrivateNameScope is null &&
                   _capturedPrivateNameScopes.IsDefaultOrEmpty &&
                   CanUseProductionUnifiedBytecodeSuperBindingPath(plan) &&
                   _instanceFields.IsDefaultOrEmpty &&
                   // Generalized past SimpleReturnProgram bodies (A6): a multi-statement arrow body is
                   // admitted over the FULL instruction stream, not just a single `return <expr>;`. The
                   // authoritative per-instruction validation is
                   // UnifiedBytecodeProductionEligibility.Evaluate(plan, activation), which receives this
                   // path via the activation descriptor and declines any opcode it cannot execute
                   // (e.g. a super-property read declines via SuperPropertyDependency). The old
                   // arrow-program-shape + arrow-activation-dependency gates required a single
                   // return-expression body and blocked every multi-statement arrow
                   // (e.g. `(a,b) => { const s = a+b; return s*2; }`). Lexical this/new.target threading
                   // is body-shape-agnostic (TryExecuteProductionUnifiedBytecode threads _lexicalThis /
                   // _lexicalThisEnvironment / _lexicalNewTarget before VM entry), so it continues to flow
                   // for multi-statement bodies unchanged.
                   CanUseSimpleIrActivationPlanShape(plan) &&
                   // Option B (Stage 5): the BLOCK-scope collision guard has been retired — a captured
                   // enclosing name shadowed by a nested { } block now routes AND computes correctly because
                   // SlotAssignmentRewriter no longer mis-stamps the captured read to the off-stack block
                   // slot. The residual guard declines ONLY a captured-name collision with a CATCH binding or
                   // a per-iteration LOOP binding: the rewriter cannot distinguish those captured reads from
                   // a legitimate local read without enclosing-scope knowledge, so they stay on the IR runner.
                   // See docs/plans/nested-scope-capture-resolution-design.md (Option B / Stage 5).
                   plan.HasNoCapturedNameShadowedByNonBlockNestedScope;
        }

        private bool CanUseProductionUnifiedBytecodeCapturedClosureActivation(
            ExecutionPlan plan,
            JsValue newTarget)
        {
            return !IsArrowFunction &&
                   newTarget.IsUndefined &&
                   !IsClassConstructor &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   !_function.IsDefaultDerivedConstructor &&
                   (!_hasParameterExpressions && _hasOnlySimpleIdentifierParameters ||
                    CanUseProductionUnifiedBytecodeSimpleLiteralDefaultParameterPath()) &&
                   !_usesArguments &&
                   !_needsArgumentsBinding &&
                   _allowIdentifierCache &&
                   _hasCapturedActivationInClosure &&
                   !_hasClosureWithObject &&
                   _lexicalThisEnvironment is null &&
                   PrivateNameScope is null &&
                   _capturedPrivateNameScopes.IsDefaultOrEmpty &&
                   _superConstructor is null &&
                   _superPrototype is null &&
                   _instanceFields.IsDefaultOrEmpty &&
                   // Generalized past SimpleReturnProgram bodies: a captured enclosing-function local is
                   // admitted as a dynamic-identifier op over the FULL instruction stream, not just a single
                   // `return <expr>;`. The authoritative per-instruction validation is
                   // UnifiedBytecodeProductionEligibility.Evaluate(plan, activation), which receives this
                   // path via the activation descriptor and admits captured identifiers as dynamic ops
                   // through the threaded closure environment, declining any opcode it cannot execute. Only
                   // the slot-metadata sanity check remains here; the old arrow-program-shape + dependency
                   // gates required a single return-expression body and blocked every multi-statement
                   // closure (e.g. `function inc(){ n++; return n; }`).
                   CanUseSimpleIrActivationPlanShape(plan) &&
                   // Option B (Stage 5): the BLOCK-scope collision guard has been retired — a captured
                   // enclosing name shadowed by a nested { } block now routes AND computes correctly because
                   // SlotAssignmentRewriter no longer mis-stamps the captured read to the off-stack block
                   // slot. The residual guard declines ONLY a captured-name collision with a CATCH binding or
                   // a per-iteration LOOP binding: the rewriter cannot distinguish those captured reads from
                   // a legitimate local read without enclosing-scope knowledge, so they stay on the IR runner.
                   // See docs/plans/nested-scope-capture-resolution-design.md (Option B / Stage 5).
                   plan.HasNoCapturedNameShadowedByNonBlockNestedScope;
        }

        private static bool CanUseProductionUnifiedBytecodeArrowProgramShape(ExecutionPlan plan)
        {
            return plan.SimpleReturnProgram is { } returnProgram &&
                   !ContainsSuperOperation(returnProgram);
        }

        private bool CanUseProductionUnifiedBytecodeSuperBindingPath(ExecutionPlan plan)
        {
            var instructions = plan.Instructions;
            for (var i = 0; i < instructions.Length; i++)
            {
                if (UnifiedBytecodeProductionEligibility.TryGetExpressionProgram(instructions[i], out var program) &&
                    ContainsSuperOperation(program))
                {
                    return _superConstructor is not null || _superPrototype is not null;
                }
            }

            return true;
        }

        private static bool CanUseProductionUnifiedBytecodeArrowActivationDependencyPath(ExecutionPlan plan)
        {
            if (plan.ActivationSlots is not { } activationSlots ||
                plan.SimpleReturnProgram is not { } returnProgram)
            {
                return false;
            }

            var identifierConstants = returnProgram.IdentifierConstants.AsSpan();
            for (var i = 0; i < returnProgram.OperationCount; i++)
            {
                var operation = returnProgram.GetOperation(i);
                if (operation.Kind is ExpressionOpKind.LoadFunctionLiteral or ExpressionOpKind.LoadClassLiteral)
                {
                    return false;
                }

                if (TryGetIdentifierDependency(operation, identifierConstants, out var identifier) &&
                    !ResolvesToOwnActivationOrFlatSlot(identifier, activationSlots) &&
                    !CanUseArrowDynamicIdentifierOperation(operation, identifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetIdentifierDependency(
            PackedExpressionOp operation,
            ReadOnlySpan<IdentifierOperand> identifierConstants,
            out IdentifierOperand identifier)
        {
            switch (operation.Kind)
            {
                case ExpressionOpKind.LoadIdentifier:
                case ExpressionOpKind.LoadIdentifierCallTarget:
                case ExpressionOpKind.ResolveIdentifierReference:
                case ExpressionOpKind.StoreResolvedIdentifier:
                case ExpressionOpKind.StoreIdentifier:
                case ExpressionOpKind.UpdateIdentifier:
                case ExpressionOpKind.TypeOfIdentifier:
                case ExpressionOpKind.DeleteIdentifier:
                    identifier = operation.GetIdentifier(identifierConstants);
                    return true;
                default:
                    identifier = default;
                    return false;
            }
        }

        private static bool ResolvesToOwnActivationSlot(
            IdentifierOperand identifier,
            ActivationSlotShape activationSlots)
        {
            if (identifier.ScopeId >= 0)
            {
                return identifier.ScopeId == activationSlots.ScopeId &&
                       identifier.SlotIndex >= 0;
            }

            return identifier.FlatSlotId < 0 &&
                   activationSlots.SlotMap.ContainsKey(identifier.Name);
        }

        private static bool ResolvesToOwnActivationOrFlatSlot(
            IdentifierOperand identifier,
            ActivationSlotShape activationSlots) =>
            ResolvesToOwnActivationSlot(identifier, activationSlots) ||
            identifier.FlatSlotId >= 0;

        private static bool CanUseArrowDynamicIdentifierOperation(
            PackedExpressionOp operation,
            IdentifierOperand identifier) =>
            identifier.FlatSlotId < 0 &&
            !operation.IsArguments &&
            operation.Kind is
                ExpressionOpKind.LoadIdentifier or
                ExpressionOpKind.LoadIdentifierCallTarget or
                ExpressionOpKind.StoreIdentifier or
                ExpressionOpKind.ResolveIdentifierReference or
                ExpressionOpKind.StoreResolvedIdentifier or
                ExpressionOpKind.UpdateIdentifier or
                ExpressionOpKind.TypeOfIdentifier or
                ExpressionOpKind.DeleteIdentifier;

        private bool CanUseProductionUnifiedBytecodeBaseClassConstructorActivation(
            ExecutionPlan plan,
            JsValue newTarget)
        {
            var hasAdmittedParameterShape =
                !_hasParameterExpressions && _hasOnlySimpleIdentifierParameters ||
                _hasParameterExpressions && HasOnlySimpleIdentifierOrLiteralDefaultParameters() ||
                !_hasParameterExpressions && TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out _);
            return IsClassConstructor &&
                   !_isDerivedClassConstructor &&
                   !newTarget.IsUndefined &&
                   !IsArrowFunction &&
                   !IsAsyncLike &&
                   !_function.IsGenerator &&
                   !_function.IsDefaultDerivedConstructor &&
                   hasAdmittedParameterShape &&
                   !_usesArguments &&
                   !_needsArgumentsBinding &&
                   _allowIdentifierCache &&
                   _lexicalThisEnvironment is null &&
                   _homeObject is null &&
                   _superConstructor is null &&
                   _superPrototype is null &&
                   CanUseSimpleIrActivationPlanShape(plan);
        }

        private static bool CanUseProductionUnifiedBytecodeDerivedClassConstructorPlanShape(ExecutionPlan plan)
        {
            var hasSuperConstruct = false;
            var instructions = plan.Instructions;
            for (var i = 0; i < instructions.Length; i++)
            {
                if (!UnifiedBytecodeProductionEligibility.TryGetExpressionProgram(instructions[i], out var program))
                {
                    continue;
                }

                if (!CanUseProductionUnifiedBytecodeDerivedClassConstructorProgram(program, ref hasSuperConstruct))
                {
                    return false;
                }
            }

            return hasSuperConstruct;
        }

        private static bool CanUseProductionUnifiedBytecodeDerivedClassConstructorProgram(
            ExpressionProgram program,
            ref bool hasSuperConstruct)
        {
            foreach (var operation in program.EnumerateOperations())
            {
                switch (operation.Kind)
                {
                    case ExpressionOpKind.LoadThis:
                        break;
                    case ExpressionOpKind.LoadNamedSuperCallTarget:
                    case ExpressionOpKind.LoadComputedSuperCallTarget:
                    case ExpressionOpKind.EnsureSuperReference:
                        break;
                    case ExpressionOpKind.GetNamedSuperProperty:
                    case ExpressionOpKind.GetComputedSuperProperty:
                    case ExpressionOpKind.SetNamedSuperProperty:
                    case ExpressionOpKind.SetComputedSuperProperty:
                    case ExpressionOpKind.UpdateNamedSuperProperty:
                    case ExpressionOpKind.UpdateComputedSuperProperty:
                        return false;
                    case ExpressionOpKind.SuperConstruct:
                        hasSuperConstruct = true;
                        break;
                }
            }

            return true;
        }

        private bool CanUseSimpleIrActivationHomeObjectPath(ExecutionPlan plan)
        {
            if (_homeObject is null)
            {
                return true;
            }

            return plan.SimpleReturnProgram is { } returnProgram &&
                   !ContainsSuperOperation(returnProgram);
        }

        private static bool CanUseSimpleIrActivationArrowFastPath(ExecutionPlan plan)
        {
            if (plan.SimpleReturnProgram is not { } returnProgram)
            {
                return false;
            }

            return !ContainsThisNewTargetOrSuperOperation(returnProgram);
        }

        private static bool ContainsThisNewTargetOrSuperOperation(ExpressionProgram program)
        {
            foreach (var operation in program.EnumerateOperations())
            {
                switch (operation.Kind)
                {
                    case ExpressionOpKind.LoadThis:
                    case ExpressionOpKind.LoadNewTarget:
                    case ExpressionOpKind.LoadNamedSuperCallTarget:
                    case ExpressionOpKind.LoadComputedSuperCallTarget:
                    case ExpressionOpKind.EnsureSuperReference:
                    case ExpressionOpKind.GetNamedSuperProperty:
                    case ExpressionOpKind.GetComputedSuperProperty:
                    case ExpressionOpKind.SetNamedSuperProperty:
                    case ExpressionOpKind.SetComputedSuperProperty:
                    case ExpressionOpKind.UpdateNamedSuperProperty:
                    case ExpressionOpKind.UpdateComputedSuperProperty:
                    case ExpressionOpKind.SuperConstruct:
                        return true;
                }
            }

            return false;
        }

        private static bool ContainsSuperOperation(ExpressionProgram program)
        {
            foreach (var operation in program.EnumerateOperations())
            {
                switch (operation.Kind)
                {
                    case ExpressionOpKind.LoadNamedSuperCallTarget:
                    case ExpressionOpKind.LoadComputedSuperCallTarget:
                    case ExpressionOpKind.EnsureSuperReference:
                    case ExpressionOpKind.GetNamedSuperProperty:
                    case ExpressionOpKind.GetComputedSuperProperty:
                    case ExpressionOpKind.SetNamedSuperProperty:
                    case ExpressionOpKind.SetComputedSuperProperty:
                    case ExpressionOpKind.UpdateNamedSuperProperty:
                    case ExpressionOpKind.UpdateComputedSuperProperty:
                    case ExpressionOpKind.SuperConstruct:
                        return true;
                }
            }

            return false;
        }

        private bool CanUseSimpleIrActivationPlanShape(ExecutionPlan plan)
        {
            return plan.ActivationSlots is { } activationSlots &&
                   activationSlots.ScopeId == plan.RootScopeId &&
                   activationSlots.LayoutId == plan.LayoutId &&
                   !activationSlots.ParameterSlotIndices.IsDefault &&
                   activationSlots.ParameterSlotIndices.Length == _parameterNames.Length;
        }

        private bool CanUseProductionUnifiedBytecodeDynamicNameFastPath()
        {
            return _hasBodyWithStatement &&
                   !_hasDirectEvalInBodyOrParameters &&
                   !_hasClosureWithObject &&
                   !_hasCapturedActivationInClosure &&
                   !_usesArguments;
        }

        private bool CanUseProductionUnifiedBytecodeOrdinaryDynamicNameFastPath(ExecutionPlan plan)
        {
            return !_hasClosureWithObject &&
                   !_hasCapturedActivationInClosure &&
                   !_usesArguments &&
                   UnifiedBytecodeProductionEligibility.ContainsOrdinaryDynamicIdentifierDependency(plan);
        }

        private bool CanUseProductionUnifiedBytecodeImplicitArgumentsObjectDependencyPath(ExecutionPlan plan)
        {
            return _argumentsObjectNeeded &&
                   !IsClassConstructor &&
                   !_hasDirectEvalInBodyOrParameters &&
                   !_hasClosureWithObject &&
                   !_hasCapturedActivationInClosure &&
                   UnifiedBytecodeProductionEligibility.ContainsOnlyImplicitArgumentsObjectDynamicIdentifierDependency(plan);
        }

        private bool CanUseProductionUnifiedBytecodePlanShape(
            ExecutionPlan plan,
            bool canUseDynamicNamePath)
        {
            if (plan.ActivationSlots is not { } activationSlots ||
                activationSlots.ScopeId != plan.RootScopeId ||
                activationSlots.LayoutId != plan.LayoutId)
            {
                return plan.ActivationSlots is not null &&
                       (_hasDirectEvalInBodyOrParameters || HasClassDeclarationInstruction(plan));
            }

            if (!activationSlots.ParameterSlotIndices.IsDefault)
            {
                return activationSlots.ParameterSlotIndices.Length == _parameterNames.Length;
            }

            return canUseDynamicNamePath || _hasDirectEvalInBodyOrParameters;
        }

        private static bool HasClassDeclarationInstruction(ExecutionPlan plan)
        {
            var instructions = plan.Instructions;
            for (var i = 0; i < instructions.Length; i++)
            {
                if (instructions[i] is ClassDeclarationInstruction)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasBlockScopedFunctionDeclarationInstruction(ExecutionPlan plan)
        {
            var instructions = plan.Instructions;
            for (var i = 0; i < instructions.Length; i++)
            {
                if (instructions[i] is FunctionDeclarationInstruction { Descriptor: not null })
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCapturedActivationInClosure(JsEnvironment closure)
        {
            var current = closure;
            while (current is not null)
            {
                if (current.IsBodyEnvironment ||
                    current.IsFunctionScope && !current.IsGlobalFunctionScope)
                {
                    return true;
                }

                current = current.Enclosing;
            }

            return false;
        }

        private JsEnvironment CreateSimpleIrActivationEnvironment<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            ExecutionPlan plan,
            EvaluationContext context,
            JsValue newTarget = default)
            where TArgs : IReadOnlyList<JsValue>
        {
            var functionEnvironment = JsEnvironmentPool.Rent(_closure, true, _isStrict, _function.Source,
                _functionDescription, logger: RealmState.Logger);
            var executionEnvironment = JsEnvironmentPool.Rent(functionEnvironment, false, _isStrict,
                _function.Source, _functionDescription, isBodyEnvironment: true, logger: RealmState.Logger);

            var activationSlots = plan.ActivationSlots!;
            var rootLexicals = plan.SafeRootLexicalBindings;
            if (rootLexicals.Count == 0 &&
                plan.SafeScopeLexicalBindings.TryGetValue(activationSlots.ScopeId, out var scopeLexicals))
            {
                rootLexicals = scopeLexicals;
            }

            executionEnvironment.ResetSlotLayoutForPlan(
                activationSlots.SlotCount,
                activationSlots.SlotMap,
                rootLexicals,
                plan.SlotSymbols,
                activationSlots.LayoutId,
                activationSlots.ScopeId,
                activationSlots.SlotNames,
                activationSlots.LexicalSlotIndices,
                activationSlots.ConstLexicalSlotIndices);

            var boundThis = _isStrict ? thisValue : CoerceThisValueForNonStrict(thisValue);
            functionEnvironment._thisValue = boundThis;
            functionEnvironment._hasThisValue = true;
            functionEnvironment.DefineJsValue(Symbol.This, boundThis);
            if (!IsArrowFunction)
            {
                var newTargetValue = newTarget.IsUndefined ? JsValue.Undefined : newTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                functionEnvironment.DefineJsValue(Symbol.ActiveFunction, _cachedJsValue, true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            functionEnvironment.SetThisInitializationStatus(true);

            IJsPropertyAccessor? prototypeForSuper;
            if (_homeObject is not null)
            {
                prototypeForSuper = (_homeObject as IPrototypeAccessorProvider)?.PrototypeAccessor ??
                                    _homeObject.Prototype;
                prototypeForSuper ??= _superPrototype;
            }
            else
            {
                prototypeForSuper = _superPrototype;
                if (prototypeForSuper is null && boundThis.TryGetObject<JsObject>(out var thisObj))
                {
                    prototypeForSuper = thisObj.Prototype;
                }
            }

            if (_homeObject is not null ||
                _superConstructor is not null ||
                prototypeForSuper is not null)
            {
                functionEnvironment.DefineJsValue(Symbol.Super,
                    JsValue.FromObjectUnsafe(new SuperBinding(
                        _superConstructor,
                        prototypeForSuper,
                        boundThis,
                        true)));
            }

            if (!_hasFunctionNameEnvironment && _function.Name is { } functionName)
            {
                functionEnvironment.DefineJsValue(functionName, _cachedJsValue, true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            SetAnnexBBlockedNamesForFastActivation(functionEnvironment);
            HoistFunctionScopedVarsForFastActivation(executionEnvironment);
            BindSimpleIrActivationParameters(arguments, executionEnvironment, activationSlots);
            HoistFunctionDeclarationsForFastActivation(executionEnvironment, context);
            if (_argumentsObjectNeeded)
            {
                var argumentsObject = _function.CreateArgumentsObject(arguments, executionEnvironment,
                    RealmState,
                    this,
                    _isStrict);
                var argumentsValue = JsValue.FromObjectUnsafe(argumentsObject);
                executionEnvironment.DefineJsValue(Symbol.Arguments, argumentsValue, isLexicalBinding: false);
                if (!ReferenceEquals(executionEnvironment, functionEnvironment))
                {
                    functionEnvironment.DefineJsValue(Symbol.Arguments, argumentsValue, isLexicalBinding: false);
                }
            }

            return executionEnvironment;
        }

        private JsEnvironment CreateSimpleDerivedClassConstructorEnvironment<TArgs>(
            TArgs arguments,
            JsValue newTarget,
            ExecutionPlan plan,
            JsValue? defaultDerivedRestArguments = null)
            where TArgs : IReadOnlyList<JsValue>
        {
            var functionEnvironment = JsEnvironmentPool.Rent(_closure, true, _isStrict, _function.Source,
                _functionDescription, logger: RealmState.Logger);
            var executionEnvironment = JsEnvironmentPool.Rent(functionEnvironment, false, _isStrict,
                _function.Source, _functionDescription, isBodyEnvironment: true, logger: RealmState.Logger);

            var activationSlots = plan.ActivationSlots!;
            var rootLexicals = plan.SafeRootLexicalBindings;
            if (rootLexicals.Count == 0 &&
                plan.SafeScopeLexicalBindings.TryGetValue(activationSlots.ScopeId, out var scopeLexicals))
            {
                rootLexicals = scopeLexicals;
            }

            executionEnvironment.ResetSlotLayoutForPlan(
                activationSlots.SlotCount,
                activationSlots.SlotMap,
                rootLexicals,
                plan.SlotSymbols,
                activationSlots.LayoutId,
                activationSlots.ScopeId,
                activationSlots.SlotNames,
                activationSlots.LexicalSlotIndices,
                activationSlots.ConstLexicalSlotIndices);

            functionEnvironment.SetThisInitializationStatus(false);
            functionEnvironment.DefineJsValue(Symbol.This, JsValue.Uninitialized);
            if (_function.IsDefaultDerivedConstructor)
            {
                functionEnvironment.IsDefaultDerivedConstructor = true;
                executionEnvironment.IsDefaultDerivedConstructor = true;
            }

            functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                JsValue.FromObjectUnsafe(functionEnvironment));
            functionEnvironment.DefineJsValue(Symbol.NewTarget, newTarget, true, isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
            functionEnvironment.DefineJsValue(Symbol.ActiveFunction, _cachedJsValue, true,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);
            functionEnvironment.DefineJsValue(Symbol.Super,
                JsValue.FromObjectUnsafe(new SuperBinding(
                    _superConstructor,
                    _superPrototype,
                    JsValue.Undefined,
                    false)));

            if (!_hasFunctionNameEnvironment && _function.Name is { } functionName)
            {
                functionEnvironment.DefineJsValue(functionName, _cachedJsValue, true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            SetAnnexBBlockedNamesForFastActivation(functionEnvironment);
            HoistFunctionScopedVarsForFastActivation(executionEnvironment);
            if (TryGetDefaultDerivedConstructorRestParameter(out var defaultDerivedRestName))
            {
                BindDefaultDerivedConstructorRestParameter(
                    arguments,
                    executionEnvironment,
                    activationSlots,
                    defaultDerivedRestName,
                    defaultDerivedRestArguments);
            }
            else
            {
                BindSimpleIrActivationParameters(arguments, executionEnvironment, activationSlots);
            }

            return executionEnvironment;
        }

        private JsEnvironment CreateSimpleBaseClassConstructorEnvironment<TArgs>(
            TArgs arguments,
            JsValue thisValue,
            JsValue newTarget,
            ExecutionPlan plan)
            where TArgs : IReadOnlyList<JsValue>
        {
            var functionEnvironment = JsEnvironmentPool.Rent(_closure, true, _isStrict, _function.Source,
                _functionDescription, logger: RealmState.Logger);
            var executionEnvironment = JsEnvironmentPool.Rent(functionEnvironment, false, _isStrict,
                _function.Source, _functionDescription, isBodyEnvironment: true, logger: RealmState.Logger);

            var activationSlots = plan.ActivationSlots!;
            var rootLexicals = plan.SafeRootLexicalBindings;
            if (rootLexicals.Count == 0 &&
                plan.SafeScopeLexicalBindings.TryGetValue(activationSlots.ScopeId, out var scopeLexicals))
            {
                rootLexicals = scopeLexicals;
            }

            executionEnvironment.ResetSlotLayoutForPlan(
                activationSlots.SlotCount,
                activationSlots.SlotMap,
                rootLexicals,
                plan.SlotSymbols,
                activationSlots.LayoutId,
                activationSlots.ScopeId,
                activationSlots.SlotNames,
                activationSlots.LexicalSlotIndices,
                activationSlots.ConstLexicalSlotIndices);

            functionEnvironment.SetThisInitializationStatus(true);
            functionEnvironment._thisValue = thisValue;
            functionEnvironment._hasThisValue = true;
            functionEnvironment.DefineJsValue(Symbol.This, thisValue);
            functionEnvironment.DefineJsValue(Symbol.NewTarget, newTarget, true, isLexicalBinding: true,
                blocksFunctionScopeOverride: true);
            functionEnvironment.DefineJsValue(Symbol.ActiveFunction, _cachedJsValue, true,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);

            if (!_hasFunctionNameEnvironment && _function.Name is { } functionName)
            {
                functionEnvironment.DefineJsValue(functionName, _cachedJsValue, true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            SetAnnexBBlockedNamesForFastActivation(functionEnvironment);
            HoistFunctionScopedVarsForFastActivation(executionEnvironment);
            BindSimpleIrActivationParameters(arguments, executionEnvironment, activationSlots);
            return executionEnvironment;
        }

        private void SetAnnexBBlockedNamesForFastActivation(JsEnvironment varEnvironment)
        {
            if (_isStrict || !_hasFunctionDeclarations)
            {
                return;
            }

            if (_bodyLexicalTemplate.Length == 0 &&
                _parameterNames.Length == 0 &&
                _catchParameterTemplate.Length == 0 &&
                !_hasParameterExpressions &&
                !_argumentsObjectNeeded)
            {
                return;
            }

            var blockedNames = _bodyLexicalTemplate.Length == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(_bodyLexicalTemplate, ReferenceEqualityComparer<Symbol>.Instance);

            foreach (var parameterName in _parameterNames)
            {
                blockedNames.Add(parameterName);
            }

            foreach (var catchParameterName in _catchParameterTemplate)
            {
                if (!_simpleCatchParameterTemplate.Contains(catchParameterName))
                {
                    blockedNames.Add(catchParameterName);
                }
            }

            if (_hasParameterExpressions)
            {
                foreach (var blockedName in CollectAnnexBBlockFunctionNames(_function.Body))
                {
                    blockedNames.Add(blockedName);
                }
            }

            if (_argumentsObjectNeeded)
            {
                blockedNames.Add(Symbol.Arguments);
            }

            if (blockedNames.Count > 0)
            {
                varEnvironment.SetAnnexBBlockedNames(blockedNames);
            }
        }

        private void HoistFunctionScopedVarsForFastActivation(JsEnvironment executionEnvironment)
        {
            for (var i = 0; i < _legacyTailRestartResetVarNames.Length; i++)
            {
                executionEnvironment.DefineFunctionScoped(_legacyTailRestartResetVarNames[i], JsValue.Undefined, false);
            }

            if (_hasFunctionNameEnvironment &&
                _function.Name is { } hoistedName &&
                ContainsVarDeclaration(_function, hoistedName) &&
                !executionEnvironment.HasBinding(hoistedName))
            {
                executionEnvironment.DefineFunctionScoped(hoistedName, JsValue.Undefined, false);
            }
        }

        private void HoistFunctionDeclarationsForFastActivation(
            JsEnvironment executionEnvironment,
            EvaluationContext context)
        {
            if (!_hasFunctionDeclarations)
            {
                return;
            }

            var lexicalNames = RentSymbolSet(_lexicalTemplate);
            var simpleCatchParameterNames = RentSymbolSet(_simpleCatchParameterTemplate);
            var catchParameterNames = RentSymbolSet();
            try
            {
                simpleCatchParameterNames.Clear();
                var functionMode = _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
                using var functionScopeFrame = context.PushScope(ScopeKind.Function, functionMode);
                _function.Body.HoistVarDeclarations(
                    executionEnvironment,
                    context,
                    lexicalNames: lexicalNames,
                    catchParameterNames: catchParameterNames,
                    simpleCatchParameterNames: simpleCatchParameterNames);
            }
            finally
            {
                ReturnSymbolSet(catchParameterNames);
                ReturnSymbolSet(simpleCatchParameterNames);
                ReturnSymbolSet(lexicalNames);
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private void ReturnSimpleIrActivationEnvironment(JsEnvironment? executionEnvironment)
        {
            if (executionEnvironment is null)
            {
                return;
            }

            if (ShouldPreserveMappedArgumentsObjectEnvironment())
            {
                return;
            }

            var functionEnvironment = executionEnvironment.Enclosing;
            JsEnvironmentPool.Return(executionEnvironment, RealmState.Logger);
            JsEnvironmentPool.Return(functionEnvironment, RealmState.Logger);
        }

        private bool ShouldPreserveMappedArgumentsObjectEnvironment()
        {
            return _argumentsObjectNeeded &&
                   (_usesArguments || _needsArgumentsBinding) &&
                   !_isStrict &&
                   _function.IsSimpleParameterList();
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private void BindSimpleIrActivationParameters<TArgs>(
            TArgs arguments,
            JsEnvironment executionEnvironment,
            ActivationSlotShape activationSlots)
            where TArgs : IReadOnlyList<JsValue>
        {
            var parameterSlotIndices = activationSlots.ParameterSlotIndices;
            var hasFinalRestParameter =
                TryGetProductionUnifiedBytecodeFinalRestParameterIndex(out var finalRestParameterIndex);
            if (parameterSlotIndices.IsDefault)
            {
                for (var i = 0; i < _parameterNames.Length; i++)
                {
                    var value = GetProductionUnifiedBytecodeParameterValue(
                        arguments,
                        i,
                        hasFinalRestParameter,
                        finalRestParameterIndex);
                    executionEnvironment.DefineParameterFast(_parameterNames[i], value);
                }

                return;
            }

            for (var i = 0; i < _parameterNames.Length; i++)
            {
                var value = GetProductionUnifiedBytecodeParameterValue(
                    arguments,
                    i,
                    hasFinalRestParameter,
                    finalRestParameterIndex);
                executionEnvironment.SetSlotDirect(parameterSlotIndices[i], value);
            }
        }

        private JsValue GetProductionUnifiedBytecodeParameterValue<TArgs>(
            TArgs arguments,
            int parameterIndex,
            bool hasFinalRestParameter,
            int finalRestParameterIndex)
            where TArgs : IReadOnlyList<JsValue>
        {
            if (hasFinalRestParameter && parameterIndex == finalRestParameterIndex)
            {
                return CreateRestArguments(arguments, finalRestParameterIndex);
            }

            var value = parameterIndex < arguments.Count ? arguments[parameterIndex] : JsValue.Undefined;
            if (value.IsUndefined &&
                TryGetSimpleLiteralDefaultParameterValue(parameterIndex, out var defaultValue))
            {
                return defaultValue;
            }

            return value;
        }

        private bool TryGetSimpleLiteralDefaultParameterValue(int parameterIndex, out JsValue value)
        {
            if ((uint)parameterIndex < (uint)_function.Parameters.Length &&
                _function.Parameters[parameterIndex].DefaultValue is LiteralExpression literal)
            {
                value = literal.Value;
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        private void BindDefaultDerivedConstructorRestParameter<TArgs>(
            TArgs arguments,
            JsEnvironment executionEnvironment,
            ActivationSlotShape activationSlots,
            Symbol restName,
            JsValue? defaultDerivedRestArguments)
            where TArgs : IReadOnlyList<JsValue>
        {
            var restValue = defaultDerivedRestArguments ?? CreateDefaultDerivedConstructorRestArguments(arguments);
            var parameterSlotIndices = activationSlots.ParameterSlotIndices;
            if (parameterSlotIndices.IsDefault || parameterSlotIndices.Length == 0)
            {
                executionEnvironment.DefineParameterFast(restName, restValue);
                return;
            }

            var parameterSlotIndex = parameterSlotIndices[0];
            if (parameterSlotIndex >= 0)
            {
                executionEnvironment.SetSlotDirect(parameterSlotIndex, restValue);
            }
        }

        private JsValue CreateDefaultDerivedConstructorRestArguments<TArgs>(TArgs arguments)
            where TArgs : IReadOnlyList<JsValue>
        {
            return JsValue.FromJsArray(new JsArray(arguments, RealmState));
        }

        private JsValue CreateRestArguments<TArgs>(TArgs arguments, int startIndex)
            where TArgs : IReadOnlyList<JsValue>
        {
            var restArray = new JsArray(RealmState);
            for (var i = startIndex; i < arguments.Count; i++)
            {
                restArray.Push(arguments[i]);
            }

            return JsValue.FromJsArray(restArray);
        }

        private bool TryGetDefaultDerivedConstructorRestParameter(out Symbol restName)
        {
            if (_function.IsDefaultDerivedConstructor &&
                _function.Parameters is
                [
                    {
                        Name: { } name,
                        Pattern: null,
                        DefaultValue: null,
                        IsRest: true
                    }
                ] &&
                _parameterNames.Length == 1)
            {
                restName = name;
                return true;
            }

            restName = default!;
            return false;
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
            InvalidateProductionUnifiedBytecodeEligibilityCache();
            if (scope is not null)
            {
                _canUseFastPathBase = false;
                _canUseSimpleIrActivationFastBase = false;
            }
        }

        public void SetCapturedPrivateNameScopes(ImmutableArray<PrivateNameScope> scopes)
        {
            _capturedPrivateNameScopes = scopes;
            InvalidateProductionUnifiedBytecodeEligibilityCache();
            if (!scopes.IsDefaultOrEmpty)
            {
                _canUseFastPathBase = false;
                _canUseSimpleIrActivationFastBase = false;
            }
        }

        public void SetSuperBinding(IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype)
        {
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
            InvalidateProductionUnifiedBytecodeEligibilityCache();
            if (superConstructor is not null || superPrototype is not null)
            {
                _canUseFastPathBase = false;
                _canUseSimpleIrActivationFastBase = false;
            }
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
            InvalidateProductionUnifiedBytecodeEligibilityCache();
            _canUseFastPathBase = false;
            _canUseSimpleIrActivationFastBase = false;
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
            InvalidateProductionUnifiedBytecodeEligibilityCache();
        }

        public void SetPrototypeObject(JsObject prototype)
        {
            _prototypeObject = prototype;
        }

        public void SetIsClassConstructor(bool isDerived)
        {
            IsClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
            InvalidateProductionUnifiedBytecodeEligibilityCache();
            _canUseFastPathBase = false;
            _canUseSimpleIrActivationFastBase = false;
        }

        internal void SetInstanceFields(ImmutableArray<ResolvedClassField> fields)
        {
            _instanceFields = fields;
            InvalidateProductionUnifiedBytecodeEligibilityCache();
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
                    valueJs = UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(
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

        private static HashSet<Symbol> CollectAnnexBBlockFunctionNames(BlockStatement body) =>
            AnnexBFunctionCollector.CollectBlockFunctionNames(body);

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
            if (_activationSlots is { } activationSlots)
            {
                functionEnvironment.ScopeId = activationSlots.ScopeId;
                functionEnvironment.InitializeSlotsWithCapacity(
                    GetNonNegativeSlotCount(activationSlots.SlotCount),
                    _activationMinimumCapacity);
                functionEnvironment.SetSlotNames(activationSlots.SlotNames);
            }
            else
            {
                functionEnvironment.ScopeId = _functionScopeId;
                functionEnvironment.SetSlotMap(_function.SlotMap);
                functionEnvironment.InitializeSlotsWithCapacity(
                    GetNonNegativeSlotCount(_function.SlotCount),
                    _activationMinimumCapacity);
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
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static int GetNonNegativeSlotCount(int slotCount)
        {
            return slotCount > 0 ? slotCount : 0;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private int ComputeActivationMinimumCapacity()
        {
            var baseSlots = GetNonNegativeSlotCount(_activationSlots?.SlotCount ?? _function.SlotCount);
            var extras = 0; // 'this' uses dedicated _thisValue/_hasThisValue storage in this fast invocation path.

            if ((_argumentsObjectNeeded && _needsArgumentsBinding) || _usesArguments)
            {
                extras++; // Symbol.Arguments
            }

            if (!IsArrowFunction && _function.Name is not null && !_hasFunctionNameEnvironment)
            {
                extras++; // Named function expression body binding.
            }

            return baseSlots + extras;
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

                // Bind remaining parameters to undefined (when function has more params than args)
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    slots[i].Value = JsValue.Undefined;
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
            if (_activationSlots is { } activationSlots)
            {
                reuseEnvironment.ScopeId = activationSlots.ScopeId;
                reuseEnvironment.InitializeSlotsWithCapacity(
                    GetNonNegativeSlotCount(activationSlots.SlotCount),
                    _activationMinimumCapacity);
                reuseEnvironment.SetSlotNames(activationSlots.SlotNames);
            }
            else
            {
                reuseEnvironment.ScopeId = _function.ScopeId;
                reuseEnvironment.SetSlotMap(_function.SlotMap);
                reuseEnvironment.InitializeSlotsWithCapacity(
                    GetNonNegativeSlotCount(_function.SlotCount),
                    _activationMinimumCapacity);
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

            reuseEnvironment._thisValue = boundThisValue;
            reuseEnvironment._hasThisValue = true;

            // Bind first parameter directly - no array allocation, no Array.Fill needed
            var slots = reuseEnvironment._slots;
            if (slots is not null && reuseEnvironment._slotCount > 0 && _parameterNames.Length > 0)
            {
                slots[0].Value = arg0;

                // Bind remaining parameters to undefined (when function has more params than args)
                for (var i = 1; i < _parameterNames.Length; i++)
                {
                    slots[i].Value = JsValue.Undefined;
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
