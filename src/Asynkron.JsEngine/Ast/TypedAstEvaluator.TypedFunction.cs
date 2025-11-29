using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class TypedFunction : IJsEnvironmentAwareCallable, IJsPropertyAccessor, IJsObjectLike,
        ICallableMetadata, IFunctionNameTarget, IPrivateBrandHolder, IPropertyDefinitionHost, IExtensibilityControl
    {
        private readonly Symbol[] _bodyLexicalNames;
        private readonly JsEnvironment _closure;
        private readonly FunctionExpression _function;
        private readonly bool _hasHoistableDeclarations;
        private readonly object? _lexicalThis;
        private readonly HashSet<object> _privateBrands = new(ReferenceEqualityComparer<object>.Instance);
        private readonly JsObject _properties = new();
        private readonly RealmState _realmState;
        private readonly bool _wasAsyncFunction;
        private IJsObjectLike? _homeObject;
        private ImmutableArray<ClassField> _instanceFields = ImmutableArray<ClassField>.Empty;
        private bool _isClassConstructor;
        private bool _isDerivedClassConstructor;
        private IJsEnvironmentAwareCallable? _superConstructor;
        private IJsPropertyAccessor? _superPrototype;

        public TypedFunction(FunctionExpression function, JsEnvironment closure, RealmState realmState)
        {
            if (function.IsGenerator)
            {
                throw new NotSupportedException(
                    "Generator functions should be created via the generator factory.");
            }

            _function = function;
            _closure = closure;
            _realmState = realmState;
            IsAsyncFunction = function.IsAsync;
            _wasAsyncFunction = function.WasAsync;
            IsArrowFunction = function.IsArrow;
            _bodyLexicalNames = CollectLexicalNames(function.Body).ToArray();
            _hasHoistableDeclarations = HasHoistableDeclarations(function.Body);
            if (IsArrowFunction && _closure.TryGet(Symbol.This, out var capturedThis))
            {
                _lexicalThis = capturedThis;
            }
            else if (IsArrowFunction)
            {
                _lexicalThis = Symbol.Undefined;
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

        public bool IsAsyncLike => IsAsyncFunction || _wasAsyncFunction;
        public PrivateNameScope? PrivateNameScope { get; private set; }

        public bool IsArrowFunction { get; }

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

            var functionMode = _function.Body.IsStrict
                ? ScopeMode.Strict
                : _realmState.Options.EnableAnnexBFunctionExtensions
                    ? ScopeMode.SloppyAnnexB
                    : ScopeMode.Sloppy;
            using var functionScopeFrame = context.PushScope(ScopeKind.Function, functionMode);

            if (!_function.Body.IsStrict && !IsArrowFunction)
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
                parameterEnvironment = new JsEnvironment(_closure, false, _function.Body.IsStrict, _function.Source,
                    description, isParameterEnvironment: true);
                parameterEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                functionEnvironment = new JsEnvironment(parameterEnvironment, true, _function.Body.IsStrict,
                    _function.Source, description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            }
            else
            {
                functionEnvironment = new JsEnvironment(_closure, true, _function.Body.IsStrict, _function.Source,
                    description);
                functionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
                parameterEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(functionEnvironment, false, _function.Body.IsStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            using var privateScope = PrivateNameScope is not null
                ? context.EnterPrivateNameScope(PrivateNameScope)
                : null;

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
                boundThis = _lexicalThis ?? Symbol.Undefined;
                context.MarkThisInitialized();
                functionEnvironment.Define(Symbol.This, boundThis);
                if (_closure.TryGet(Symbol.ThisInitialized, out var closureThisInitialized))
                {
                    SetThisInitializationStatus(functionEnvironment, JsOps.ToBoolean(closureThisInitialized));
                }
                else if (_closure.TryGet(Symbol.Super, out var closureSuper) &&
                         closureSuper is SuperBinding superBinding)
                {
                    SetThisInitializationStatus(functionEnvironment, superBinding.IsThisInitialized);
                }
            }
            else
            {
                if (!_function.Body.IsStrict && (thisValue is null || ReferenceEquals(thisValue, Symbol.Undefined)))
                {
                    boundThis = CallingJsEnvironment?.Get(Symbol.This) ?? Symbol.Undefined;
                }

                if (!_function.Body.IsStrict &&
                    boundThis is not JsObject &&
                    !IsNullish(boundThis) &&
                    boundThis is not IIsHtmlDda)
                {
                    boundThis = ToObjectForDestructuring(boundThis, context);
                }

                if (_isDerivedClassConstructor && _superConstructor is not null)
                {
                    context.MarkThisUninitialized();
                }
                else
                {
                    context.MarkThisInitialized();
                }

                SetThisInitializationStatus(functionEnvironment, context.IsThisInitialized);
                functionEnvironment.Define(Symbol.This, boundThis ?? new JsObject());

                var prototypeForSuper = _superPrototype;
                if (prototypeForSuper is null && _homeObject is not null)
                {
                    prototypeForSuper = _homeObject.Prototype;
                }

                if (prototypeForSuper is null && thisValue is JsObject thisObj)
                {
                    prototypeForSuper = thisObj.Prototype;
                }

                if (_superConstructor is not null || prototypeForSuper is not null)
                {
                    var binding = new SuperBinding(_superConstructor, prototypeForSuper, boundThis,
                        context.IsThisInitialized);
                    functionEnvironment.Define(Symbol.Super, binding);
                }
            }

            // Define `arguments` so non-arrow function bodies can observe the
            // arguments they were called with. Arrow functions are also
            // represented as FunctionExpression for now, so this behaves like a
            // per-call arguments array rather than a lexical binding.
            // Named function expressions should see their name inside the body.
            if (!IsArrowFunction && _function.Name is { } functionName)
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
                        if (callingContext is not null)
                        {
                            callingContext.Clear();
                        }

                        return CreateRejectedPromise(thrownDuringBinding, parameterEnvironment);
                    }

                    if (callingContext is not null)
                    {
                        callingContext.SetThrow(thrownDuringBinding);
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

            if (!IsArrowFunction)
            {
                var argumentsObject =
                    CreateArgumentsObject(_function, arguments, parameterEnvironment, _realmState, this);
                functionEnvironment.Define(Symbol.Arguments, argumentsObject, isLexical: false);
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

                    if (callingContext is not null)
                    {
                        callingContext.SetThrow(thrown);
                    }

                    throw new ThrowSignal(thrown);
                }

                if (!IsAsyncFunction)
                {
                    if (!context.IsReturn)
                    {
                        if (_isClassConstructor && executionEnvironment.TryGet(Symbol.This, out var currentThis))
                        {
                            return currentThis;
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

        public void SetIsClassConstructor(bool isDerived)
        {
            _isClassConstructor = true;
            _isDerivedClassConstructor = isDerived;
        }

        public void SetInstanceFields(ImmutableArray<ClassField> fields)
        {
            _instanceFields = fields;
        }

        public void InitializeInstance(JsObject instance, JsEnvironment environment, EvaluationContext context)
        {
            if (_superConstructor is TypedFunction typedFunction)
            {
                typedFunction.InitializeInstance(instance, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

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
                }

                instance.SetProperty(propertyName, value);
            }
        }
    }
}
