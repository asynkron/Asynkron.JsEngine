using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;
using Asynkron.JsEngine;
using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor, IJsObjectLike,
        ICallableMetadata, ICallerInfo, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost,
        IExtensibilityControl, IPrototypeAccessorProvider
    {
        private readonly Symbol[] _bodyLexicalNames;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _hasHoistableDeclarations;
        private readonly JsEnvironment? _lexicalThisEnvironment;
        private readonly object? _lexicalThis;
        private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private readonly bool _isStrict;
        private readonly bool _wasAsyncFunction;
        private readonly bool _hasFunctionNameEnvironment;
        private bool _isConstructorEnabled;
        private ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private IJsCallable? _caller;
        private JsObject? _prototypeObject;
        private IJsObjectLike? _homeObject;
        private ImmutableArray<ClassField> _instanceFields = ImmutableArray<ClassField>.Empty;
        private bool _isClassConstructor;
        private bool _isDerivedClassConstructor;
        private IJsEnvironmentAwareCallable? _superConstructor;
        private IJsPropertyAccessor? _superPrototype;

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
            if (IsArrowFunction)
            {
                try
                {
                    if (_closure.TryGet(Symbol.This, out var capturedThis))
                    {
                        _lexicalThis = capturedThis;
                    }
                    else
                    {
                        _lexicalThis = Symbol.Undefined;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                             StringComparison.Ordinal))
                {
                    _lexicalThis = JsEnvironment.Uninitialized;
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

        public object? Invoke(IReadOnlyList<object?> arguments, object? thisValue)
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

        public bool TryGetProperty(string name, object? receiver, out object? value)
        {
            if (_properties.TryGetProperty(name, receiver ?? this, out value))
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
                        var targetIsConstructor = JsOps.IsConstructor(callable);
                        return HostFunction.CreateBoundFunction(callable, boundThis, boundArgs, targetIsConstructor,
                            _realmState);
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

        public object? InvokeWithContext(
            IReadOnlyList<object?> arguments,
            object? thisValue,
            EvaluationContext? callingContext,
            object? newTarget = null)
        {
            var context = _realmState.CreateContext(pushScope: false);
            var callerRestore = _caller;
            using var callerFrame = context.PushCaller(this, out var previousCaller);
            if (!_isStrict && !IsArrowFunction)
            {
                _caller = previousCaller;
            }
            if (_realmState.Logger is { } entryLogger && _isClassConstructor)
            {
                entryLogger.LogInformation(
                    "ctor entry func={Function} receiver={Receiver} newTarget={NewTarget}",
                    _function.Name?.Name ?? "<anonymous>",
                    DescribeValue(thisValue),
                    DescribeValue(newTarget));
            }
            if (_realmState.Logger is { } logger && _isStrict && !ReferenceEquals(thisValue, Symbol.Undefined))
            {
                logger.LogInformation("TypedFunction strict received thisValue type={Type}",
                    thisValue?.GetType().Name ?? "null");
            }
            if (callingContext is not null)
            {
                context.CallDepth = callingContext.CallDepth;
                context.MaxCallDepth = callingContext.MaxCallDepth;
            }

            if (_isClassConstructor && (newTarget is null || ReferenceEquals(newTarget, Symbol.Undefined)))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    callingContext ?? context,
                    _realmState);
                throw new ThrowSignal(error);
            }

            var description = _function.Name is { } name ? $"function {name.Name}" : "anonymous function";
            var hasParameterExpressions = HasParameterExpressions(_function);
            var parameterNames = new List<Symbol>();
            CollectParameterNamesFromFunction(_function, parameterNames);
            var lexicalNames = _bodyLexicalNames.Length == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(_bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            var catchParameterNames = CollectCatchParameterNames(_function.Body);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(_function.Body);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);
            var blockedFunctionVarNames = bodyLexicalNames.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var parameterName in parameterNames)
            {
                blockedFunctionVarNames.Add(parameterName);
            }

            var functionMode = _isStrict
                ? ScopeMode.Strict
                : _realmState.Options.EnableAnnexBFunctionExtensions
                    ? ScopeMode.SloppyAnnexB
                    : ScopeMode.Sloppy;
            using var functionScopeFrame = context.PushScope(ScopeKind.Function, functionMode);

            if (!_isStrict && !IsArrowFunction)
            {
                blockedFunctionVarNames.Add(Symbol.Arguments);
            }

            context.BlockedFunctionVarNames = blockedFunctionVarNames;

            // When parameter expressions are present, the parameter environment must sit
            // *outside* the var environment so defaults cannot observe var bindings from
            // the body (per FunctionDeclarationInstantiation step 27). Keep the var
            // environment as the function scope so hoisted vars/arguments/this live
            // there, with the parameter scope as its outer environment.
            JsEnvironment parameterEnvironment;
            JsEnvironment functionEnvironment;
            if (hasParameterExpressions)
            {
                parameterEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);
                parameterEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                functionEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict,
                    _function.Source, description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                // Parameter expressions (including direct eval) should use the
                // dedicated parameter environment as their variable environment
                // (ES 10.2.11, FunctionDeclarationInstantiation step 14 when
                // HasParameterExpressions is true). This keeps var bindings created
                // during parameter evaluation (e.g. `eval("var arguments = ...")`)
                // separate from the function body’s var environment so later body
                // declarations do not overwrite them.
            }
            else
            {
                functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                    description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                parameterEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
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
                var newTargetValue = newTarget ?? Symbol.Undefined;
                functionEnvironment.Define(Symbol.NewTarget, newTargetValue, true, isLexical: true,
                    blocksFunctionScopeOverride: true);
            }

            // Bind `this`.
            var boundThis = thisValue;
            if (IsArrowFunction)
            {
                var lexicalThis = _lexicalThis;
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
                functionEnvironment.Define(Symbol.This, boundThis);

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
                    lexicalSuperBinding = new SuperBinding(_superConstructor, _superPrototype, boundThis, true);
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
                    functionEnvironment.Define(Symbol.Super, lexicalSuperBinding, false, isLexical: true,
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
                    newTarget is not null)
                {
                    var constructedThis = new JsObject();
                    constructedThis.RealmState = _realmState;
                    if (newTarget is IJsPropertyAccessor prototypeSource &&
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
                        newTarget.GetType().Name);

                    boundThis = constructedThis;
                }

                if (!_isStrict)
                {
                    if (thisValue is null || ReferenceEquals(thisValue, Symbol.Undefined))
                    {
                        boundThis = CallingJsEnvironment?.Get(Symbol.This);
                        if (boundThis is null || ReferenceEquals(boundThis, Symbol.Undefined))
                        {
                            boundThis = _realmState.Engine?.GlobalObject;
                            if (boundThis is null)
                            {
                                boundThis = Symbol.Undefined;
                            }
                        }
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
                if (_isDerivedClassConstructor && _superConstructor is not null)
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
                functionEnvironment.Define(Symbol.This, initialThisValue);

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
                    if (prototypeForSuper is null && thisValue is JsObject thisObj)
                    {
                        prototypeForSuper = thisObj.Prototype;
                    }
                }

                if (_homeObject is not null || _superConstructor is not null || prototypeForSuper is not null)
                {
                    var binding = new SuperBinding(_superConstructor, prototypeForSuper, boundThis,
                        initialThisInitialized);
                    functionEnvironment.RealmState?.Logger?.LogInformation(
                        "SuperBinding: define in function env env={Env} isCtor={IsCtor} isDerivedCtor={IsDerivedCtor} protoNull={ProtoNull} thisInit={ThisInit}",
                        functionEnvironment.GetHashCode(),
                        _isClassConstructor,
                        _isDerivedClassConstructor,
                        prototypeForSuper is null,
                        initialThisInitialized);
                    functionEnvironment.Define(Symbol.Super, binding);
                }

                if (_isClassConstructor && boundThis is JsObject thisInstance)
                {
                    if (_isDerivedClassConstructor && _superConstructor is not null)
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

                            return Symbol.Undefined;
                        }
                    }
                }
            }

            try
            {
                if (!IsArrowFunction)
                {
                    // Create the `arguments` binding up front so parameter default expressions can reference it.
                    var argumentsObject =
                        CreateArgumentsObject(_function, arguments, parameterEnvironment, _realmState, this,
                            _isStrict);
                    parameterEnvironment.Define(Symbol.Arguments, argumentsObject, isLexical: false);
                    if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
                    {
                        functionEnvironment.Define(Symbol.Arguments, argumentsObject, isLexical: false);
                    }
                }

                // Named function expressions should see their name inside the body.
                if (!IsArrowFunction && _function.Name is { } functionName && !_hasFunctionNameEnvironment)
                {
                    parameterEnvironment.Define(functionName, this, isConst: true, isLexical: true, blocksFunctionScopeOverride: true);
                }

                BindFunctionParameters(_function, arguments, parameterEnvironment, context);
                if (context.ShouldStopEvaluation)
                {
                    if (context.IsThrow)
                    {
                        var thrownDuringBinding = context.FlowValue;
                        if (IsAsyncFunction || _wasAsyncFunction)
                        {
                            // Async functions must reject instead of throwing synchronously.
                            callingContext?.Clear();

                            return CreateRejectedPromise(thrownDuringBinding, parameterEnvironment);
                        }

                        if (callingContext is not null)
                        {
                            callingContext.SetThrow(thrownDuringBinding);
                            return thrownDuringBinding;
                        }

                        throw new ThrowSignal(thrownDuringBinding);
                    }

                    return Symbol.Undefined;
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

                try
                {
                    var result = EvaluateBlock(
                        _function.Body,
                        executionEnvironment,
                        context,
                        true);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    _realmState.Logger?.LogInformation(
                        "InvokeWithContext propagating throw type={ThrowType} callerHasContext={HasCaller} func={FunctionName}",
                        thrown?.GetType().Name ?? "null",
                        callingContext is not null,
                        _function.Name?.Name ?? "<anonymous>");

                    if (IsAsyncFunction || _wasAsyncFunction)
                    {
                        return CreateRejectedPromise(thrown, executionEnvironment);
                    }

                    if (callingContext is not null)
                    {
                        callingContext.SetThrow(thrown);
                        return thrown;
                    }

                    throw new ThrowSignal(thrown);
                }

                if (!IsAsyncFunction)
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
                                return currentThis;
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                   "ReferenceError: this",
                                   StringComparison.Ordinal))
                        {
                            // If `this` is uninitialized (e.g., derived ctor without super()), surface a JS ReferenceError.
                            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                            throw new ThrowSignal(errorObject);
                        }
                    }

                        return Symbol.Undefined;
                    }

                    var value = context.FlowValue;
                    context.ClearReturn();
                    if (_isClassConstructor &&
                        value is not JsObject &&
                        value is not IJsObjectLike)
                    {
                        // Per ES spec 9.2.2 [[Construct]] step 13c:
                        // For derived class constructors, if return value is not undefined,
                        // throw TypeError. For base class constructors, fall back to `this`.
                        if (_isDerivedClassConstructor && !ReferenceEquals(value, Symbol.Undefined))
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
                                return currentThis;
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                                     "ReferenceError: this",
                                     StringComparison.Ordinal))
                        {
                            _realmState.Logger?.LogInformation(
                                "Class constructor missing initialized this; falling back to return value reason={Reason}",
                                ex.Message);
                        }
                    }

                    return value;
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

                return CreateResolvedPromise(completionValue, executionEnvironment);
            }
            catch (ThrowSignal signal) when (IsAsyncFunction || _wasAsyncFunction)
            {
                return CreateRejectedPromise(signal.ThrownValue, executionEnvironment);
            }
            finally
            {
                if (!_isStrict && !IsArrowFunction)
                {
                    _caller = callerRestore;
                }
            }
        }
        finally
        {
            if (hasPendingFieldInitialization)
            {
                context.RemovePendingClassFieldInitializer(this);
            }
        }
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
        }

        public void SetCapturedPrivateNameScopes(ImmutableArray<PrivateNameScope> scopes)
        {
            _capturedPrivateNameScopes = scopes;
        }

        public void SetSuperBinding(IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype)
        {
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
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

        IJsCallable? ICallerInfo.Caller
        {
            get => _caller;
            set => _caller = value;
        }

        bool ICallerInfo.IsStrictFunction => _isStrict;

        public void SetIsClassConstructor(bool isDerived)
        {
            _isClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
        }

        public void SetInstanceFields(ImmutableArray<ClassField> fields)
        {
            _instanceFields = fields;
        }

        internal JsObject GetOrCreatePrototypeObject()
        {
            if (_prototypeObject is not null)
            {
                return _prototypeObject;
            }

            if (_properties.TryGetProperty("prototype", this, out var value) && value is JsObject jsObj)
            {
                _prototypeObject = jsObj;
                return jsObj;
            }

            var created = new JsObject(_realmState.ObjectPrototype)
            {
                RealmState = _realmState,
                Origin = string.IsNullOrEmpty(_function.Name?.Name)
                    ? "anonymous function prototype (materialized)"
                    : $"prototype of {_function.Name!.Name} (materialized)"
            };
            _properties.SetProperty("prototype", created);
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

            return new SuperBinding(_superConstructor, prototypeForSuper, instance, true);
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
            using var instanceFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, true);

            foreach (var field in _instanceFields)
            {
                if (field.IsPrivate && PrivateNameScope is not null && instance is not IPrivateBrandHolder)
                {
                    throw StandardLibrary.ThrowTypeError("Invalid private field receiver", context, context.RealmState);
                }

                using var classFieldInitScope = context.EnterClassFieldInitializer();
                var initEnv = new JsEnvironment(environment, isStrict: true);
                initEnv.Define(EvalHostFunction.FieldInitializerEvalFlag, true, isConst: true, isLexical: true,
                    blocksFunctionScopeOverride: true);
                initEnv.Define(Symbol.This, instance);

                var fieldSuperBinding = ResolveInstanceFieldSuperBinding(environment, instance);
                if (fieldSuperBinding is not null)
                {
                    initEnv.Define(Symbol.Super, fieldSuperBinding, true, isLexical: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGet(Symbol.NewTarget, out var newTargetValue))
                {
                    // Class field initializers execute outside of any function body; shadow new.target with undefined.
                    initEnv.Define(Symbol.NewTarget, Symbol.Undefined, true, isLexical: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGet(Symbol.Arguments, out var argumentsValue))
                {
                    initEnv.Define(Symbol.Arguments, argumentsValue, isLexical: false);
                }

                var propertyName = field.Name;
                if (field.IsComputed)
                {
                    if (field.ComputedName is null)
                    {
                        throw new InvalidOperationException("Computed class field is missing name expression.");
                    }

                    var nameValue = EvaluateExpression(field.ComputedName, initEnv, context);
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
                    value = EvaluateExpression(field.Initializer, initEnv, context);
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
                else if (instance is IJsObjectLike objectLike)
                {
                    objectLike.DefineProperty(propertyName, descriptor);
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
    }
}
