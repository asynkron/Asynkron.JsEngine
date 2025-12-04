using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor, IJsObjectLike,
        ICallableMetadata, ICallerInfo, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost,
        IExtensibilityControl
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
        private IJsCallable? _caller;
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
            bool hasFunctionNameEnvironment = false)
        {
            if (function.IsGenerator)
            {
                throw new NotSupportedException(
                    "Generator functions should be created via the generator factory.");
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            _isStrict = function.Body.IsStrict || closure.IsStrict || isLexicallyStrict;
            IsAsyncFunction = function.IsAsync;
            _wasAsyncFunction = function.WasAsync;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            IsArrowFunction = function.IsArrow;
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
            if (_realmState.FunctionPrototype is not null)
            {
                _properties.SetPrototype(_realmState.FunctionPrototype);
            }

            // Functions expose a prototype object so instances created via `new` can inherit from it.
            if (!IsArrowFunction)
            {
                var functionPrototype = new JsObject();
                functionPrototype.SetPrototype(_realmState.ObjectPrototype);
                functionPrototype.DefineProperty("constructor",
                    new PropertyDescriptor { Value = this, Writable = true, Enumerable = false, Configurable = true });
                _properties.SetProperty("prototype", functionPrototype);
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

        public bool IsAsyncFunction { get; }

        internal bool IsClassConstructor => _isClassConstructor;

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
                        initialThisValue = new JsObject();
                    }

                    boundThis = initialThisValue;
                }

                SetThisInitializationStatus(functionEnvironment, initialThisInitialized);
                functionEnvironment.Define(Symbol.This, initialThisValue);

                IJsPropertyAccessor? prototypeForSuper = null;
                if (_homeObject is not null)
                {
                    // Super property resolution is based on the current [[Prototype]] of the home object,
                    // even if it has been mutated after class definition (e.g. Object.setPrototypeOf).
                    prototypeForSuper = _homeObject.Prototype;
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
                                context.Clear();

                                callingContext?.SetThrow(thrownDuringInitialization);

                                throw new ThrowSignal(thrownDuringInitialization);
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
                    parameterEnvironment.Define(functionName, this);
                }

                BindFunctionParameters(_function, arguments, parameterEnvironment, context);
                if (context.ShouldStopEvaluation)
                {
                    if (context.IsThrow)
                    {
                        var thrownDuringBinding = context.FlowValue;
                        context.Clear();

                        if (IsAsyncFunction || _wasAsyncFunction)
                        {
                            // Async functions must reject instead of throwing synchronously.
                            callingContext?.Clear();

                            return CreateRejectedPromise(thrownDuringBinding, parameterEnvironment);
                        }

                        callingContext?.SetThrow(thrownDuringBinding);

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
                    context.Clear();

                    if (IsAsyncFunction || _wasAsyncFunction)
                    {
                        return CreateRejectedPromise(thrown, executionEnvironment);
                    }

                    callingContext?.SetThrow(thrown);

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
                                        "Class constructor returning this type={Type}",
                                        currentThis?.GetType().Name ?? "null");
                                    return currentThis;
                                }
                            }
                            catch (InvalidOperationException ex) when (context.IsThisInitialized &&
                                                                       ex.Message.StartsWith(
                                                                           "ReferenceError: this",
                                                                           StringComparison.Ordinal))
                            {
                                // If the `this` binding was marked initialized but is still
                                // uninitialized in the environment, fall back to the original
                                // construction receiver instead of surfacing a spurious TDZ error.
                                _realmState.Logger?.LogInformation(
                                    "Class constructor fell back to receiver type={Type} reason={Reason}",
                                    thisValue?.GetType().Name ?? "null",
                                    ex.Message);
                                if (context.LastConstructedThis is not null &&
                                    !ReferenceEquals(context.LastConstructedThis, JsEnvironment.Uninitialized))
                                {
                                    return context.LastConstructedThis;
                                }

                                return thisValue;
                            }
                        }

                        return Symbol.Undefined;
                    }

                    var value = context.FlowValue;
                    context.ClearReturn();
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

        public void SetSuperBinding(IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype)
        {
            _superConstructor = superConstructor;
            _superPrototype = superPrototype;
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
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

        private SuperBinding? ResolveInstanceFieldSuperBinding(JsEnvironment constructorEnvironment, JsObject instance)
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

        public void InitializeInstance(JsObject instance, JsEnvironment environment, EvaluationContext context)
        {
            if (PrivateNameScope is not null && instance is IPrivateBrandHolder brandHolder)
            {
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
                var initEnv = new JsEnvironment(environment, isStrict: true);
                initEnv.Define(Symbol.This, instance);

                var fieldSuperBinding = ResolveInstanceFieldSuperBinding(environment, instance);
                if (fieldSuperBinding is not null)
                {
                    initEnv.Define(Symbol.Super, fieldSuperBinding, true, isLexical: true,
                        blocksFunctionScopeOverride: true);
                }

                if (environment.TryGet(Symbol.NewTarget, out var newTargetValue))
                {
                    initEnv.Define(Symbol.NewTarget, newTargetValue, true, isLexical: true,
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
                else if (field.IsPrivate && PrivateNameScope is not null)
                {
                    propertyName = PrivateNameScope.GetKey(propertyName);
                }

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
                }

                instance.SetProperty(propertyName, value);
            }
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
                        return Symbol.Equals(id.Name, name);
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
