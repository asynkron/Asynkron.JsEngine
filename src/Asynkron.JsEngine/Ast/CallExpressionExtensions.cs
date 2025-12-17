using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(CallExpression expression)
    {
        /// <summary>
        /// Hot path for call expressions - handles simple TypedFunction calls without Activity overhead.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateCall(JsEnvironment environment, EvaluationContext context)
        {
            // Ultra-fast path for simple identifier calls to TypedFunctions (e.g., fib(n-1))
            // This is the most common case in recursive benchmarks
            if (!expression.IsOptional &&
                expression.Callee is IdentifierExpression calleeId &&
                expression.Arguments.Length <= 2)
            {
                // If the identifier was NOT statically resolved (SlotIndex < 0), it might be
                // in a dynamic scope (with/eval). Check for 'with' environment - if found, we need
                // to use the with-object as 'this', which the fast path doesn't handle.
                // When SlotIndex >= 0, the identifier was statically resolved, so we know
                // it's NOT coming from a 'with' binding and can skip this check.
                // IMPORTANT: Use HasWithObjectInChain() instead of TryResolveWithBinding() to avoid
                // triggering proxy 'has' and 'get' traps just to detect if we need the slow path.
                // The slow path's EvaluateCallTarget will do the actual with-binding resolution.
                if (calleeId.SlotIndex < 0 && environment.HasWithObjectInChain())
                {
                    // Fall through to slow path which handles 'with' bindings correctly
                    return expression.EvaluateCallSlow(environment, context);
                }

                // Check if all arguments are simple (no spread)
                var hasSpread = false;
                foreach (var arg in expression.Arguments)
                {
                    if (arg.IsSpread)
                    {
                        hasSpread = true;
                        break;
                    }
                }

                if (!hasSpread)
                {
                    // Fast slot-based lookup for the function
                    JsValue calleeValue;
                    if (calleeId.SlotIndex >= 0 && calleeId.ScopeId >= 0)
                    {
                        var targetEnv = environment.ScopeId == calleeId.ScopeId
                            ? environment
                            : environment.FindByScopeId(calleeId.ScopeId);

                        if (targetEnv?._slots is not null)
                        {
                            calleeValue = targetEnv._slots[calleeId.SlotIndex];
                        }
                        else
                        {
                            calleeValue = context.GetIdentifier(environment, calleeId.Name);
                        }
                    }
                    else
                    {
                        calleeValue = context.GetIdentifier(environment, calleeId.Name);
                    }

                    if (context.ShouldStopEvaluation)
                        return JsValue.Undefined;

                    // Fast path for TypedFunction only
                    if (calleeValue.TryGetObject<TypedFunction>(out var typedFunc) && !typedFunc.IsClassConstructor)
                    {
                        if (++context.CallDepth > context.MaxCallDepth)
                        {
                            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
                        }

                        // Evaluate arguments and call specialized invoke - avoids array allocation
                        JsValue result;
                        switch (expression.Arguments.Length)
                        {
                            case 0:
                                result = typedFunc.InvokeWithContext(Array.Empty<JsValue>(), JsValue.Undefined, context, JsValue.Undefined);
                                break;
                            case 1:
                                var arg0 = EvaluateExpression(expression.Arguments[0].Expression, environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    context.CallDepth--;
                                    return JsValue.Undefined;
                                }
                                // Use environment reuse optimization when eligible
                                result = expression.CanReuseCallerEnvironment
                                    ? typedFunc.InvokeWithContext1Reuse(arg0, JsValue.Undefined, context, environment)
                                    : typedFunc.InvokeWithContext1(arg0, JsValue.Undefined, context);
                                break;
                            default: // 2
                                var a0 = EvaluateExpression(expression.Arguments[0].Expression, environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    context.CallDepth--;
                                    return JsValue.Undefined;
                                }
                                var a1 = EvaluateExpression(expression.Arguments[1].Expression, environment, context);
                                if (context.ShouldStopEvaluation)
                                {
                                    context.CallDepth--;
                                    return JsValue.Undefined;
                                }
                                result = typedFunc.InvokeWithContext2(a0, a1, JsValue.Undefined, context);
                                break;
                        }

                        context.CallDepth--;
                        return result;
                    }
                }
            }

            // Fall through to slow path for complex cases
            return expression.EvaluateCallSlow(environment, context);
        }

        /// <summary>
        /// Slow path for complex call expressions - handles optional chaining, super calls, spread arguments, etc.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateCallSlow(JsEnvironment environment, EvaluationContext context)
        {
            // Fast-path for plain Map/Set method calls - bypasses prototype lookup and host function machinery
            if (TryFastPathMapSetCall(expression, environment, context, out var fastResult))
            {
                return fastResult;
            }

            var (callee, thisValue, skippedOptional) = EvaluateCallTarget(expression.Callee, environment, context);
            if (context.ShouldStopEvaluation || skippedOptional)
            {
                context.RealmState.Logger?.LogInformation(
                    "EvaluateCall short-circuit callee={CalleeType} stopped={Stopped} optionalSkipped={Skipped}",
                    expression.Callee.GetType().Name,
                    context.ShouldStopEvaluation,
                    skippedOptional);
                return JsValue.Undefined;
            }

            if (++context.CallDepth > context.MaxCallDepth)
            {
                throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
            }

            if (expression.IsOptional && callee.IsNullOrUndefined)
            {
                context.CallDepth--;
                return JsValue.Undefined;
            }

            // Per ES spec 15.7.14, default derived constructors forward arguments directly
            // without invoking the iterator protocol. Check if we're in such a context.
            var isDefaultDerivedConstructorSuperCall = expression.Callee is SuperExpression &&
                                                        environment.IsDefaultDerivedConstructor;

            var hasSpread = false;
            foreach (var argument in expression.Arguments)
            {
                if (argument.IsSpread)
                {
                    hasSpread = true;
                    break;
                }
            }

            IReadOnlyList<JsValue> frozenArguments;
            object?[]? pooledArgsArray = null; // Track if we used a pooled object array
            JsValue[]? pooledJsValueArray = null; // Track if we used a pooled JsValue array
            if (!hasSpread)
            {
                var argCount = expression.Arguments.Length;
                switch (argCount)
                {
                    case 0:
                        frozenArguments = Array.Empty<JsValue>();
                        break;
                    case 1:
                    {
                        var v0 = EvaluateExpression(expression.Arguments[0].Expression, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        // Use pooled JsValue[] directly - avoid boxing via ToObject()
                        var jsArr = JsValueCache.RentJsValueArray(1);
                        jsArr[0] = v0;
                        pooledJsValueArray = jsArr;
                        frozenArguments = jsArr;
                        break;
                    }
                    case 2:
                    {
                        var v0 = EvaluateExpression(expression.Arguments[0].Expression, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        var v1 = EvaluateExpression(expression.Arguments[1].Expression, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        // Use pooled JsValue[] directly - avoid boxing via ToObject()
                        var jsArr = JsValueCache.RentJsValueArray(2);
                        jsArr[0] = v0;
                        jsArr[1] = v1;
                        pooledJsValueArray = jsArr;
                        frozenArguments = jsArr;
                        break;
                    }
                    default:
                    {
                        // Use pooled JsValue[] for small argument counts (3-4)
                        var jsArgsArray = argCount <= 4
                            ? JsValueCache.RentJsValueArray(argCount)
                            : new JsValue[argCount];

                        if (argCount <= 4)
                        {
                            pooledJsValueArray = jsArgsArray;
                        }

                        for (var i = 0; i < argCount; i++)
                        {
                            jsArgsArray[i] = EvaluateExpression(expression.Arguments[i].Expression, environment, context);
                            if (context.ShouldStopEvaluation)
                            {
                                if (pooledJsValueArray is not null)
                                {
                                    JsValueCache.ReturnJsValueArray(pooledJsValueArray);
                                }
                                context.CallDepth--;
                                return JsValue.Undefined;
                            }
                        }

                        frozenArguments = jsArgsArray;
                        break;
                    }
                }
            }
            else
            {
                var argsBuilder = ImmutableArray.CreateBuilder<JsValue>(expression.Arguments.Length);
                foreach (var argument in expression.Arguments)
                {
                    if (argument.IsSpread)
                    {
                        var spreadValueJs = EvaluateExpression(argument.Expression, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        var spreadValue = spreadValueJs;

                        // For default derived constructor super calls, bypass the iterator protocol
                        // and directly iterate array items per ES spec 15.7.14.
                        if (isDefaultDerivedConstructorSuperCall && spreadValue.TryGetObject<JsArray>(out var jsArray))
                        {
                            foreach (var item in jsArray.Items)
                            {
                                // Items is already IReadOnlyList<JsValue>, no wrapping needed
                                argsBuilder.Add(item);
                            }
                        }
                        else
                        {
                            foreach (var item in EnumerateSpread(spreadValue, context))
                            {
                                argsBuilder.Add(item);
                            }
                        }

                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        continue;
                    }

                    argsBuilder.Add(EvaluateExpression(argument.Expression, environment, context));
                    if (context.ShouldStopEvaluation)
                    {
                        context.CallDepth--;
                        return JsValue.Undefined;
                    }
                }

                frozenArguments = FreezeArguments(argsBuilder);
            }

            if (!callee.TryGetObject<IJsCallable>(out var callable))
            {
                // Special-case Function.prototype.apply / call patterns such as
                // Object.prototype.hasOwnProperty.apply(target, args).
                if (expression.Callee is MemberExpression member)
                {
                    if (thisValue.TryGetObject<IJsCallable>(out var targetFunction) &&
                        member.Property is LiteralExpression { Value.IsString: true } propLit)
                    {
                        var propertyName = propLit.Value.AsString();
                        if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                        {
                            return JsValue.FromObjectUnsafe(InvokeWithApply(targetFunction, expression.Arguments, environment, context));
                        }

                        if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                        {
                            return JsValue.FromObjectUnsafe(InvokeWithCall(targetFunction, expression.Arguments, environment, context));
                        }
                    }

                    // Fallback for patterns like `obj.formatArgs.call(this, ...)`
                    // where `formatArgs` is a callable copied onto `obj` but the
                    // `.call` helper is missing or not modeled. In that case we
                    // invoke the underlying function directly with the provided
                    // `this` value and arguments instead of throwing.
                    if (member is
                        {
                            Property: LiteralExpression { Value.IsString: true } callLit, Target: MemberExpression
                            {
                                Property: LiteralExpression { Value.IsString: true } formatArgsLit
                            } inner
                        } && callLit.Value.AsString() == "call" && formatArgsLit.Value.AsString() == "formatArgs")
                    {
                        var targetJs = EvaluateExpression(inner.Target, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (TryGetPropertyValue(targetJs.ToObject(), "formatArgs", out var innerValue) &&
                            innerValue is IJsCallable innerFunction)
                        {
                            return JsValue.FromObjectUnsafe(InvokeWithCall(innerFunction, expression.Arguments, environment, context));
                        }
                    }
                }

                var calleeObj = callee.ToObject();
                var typeName = calleeObj?.GetType().Name ?? "null";
                var sourceInfo = GetSourceInfo(context, expression.Source);
                var symbolName = calleeObj is Symbol sym ? sym.Name : null;
                var symbolSuffix = symbolName is null ? string.Empty : $" (symbol '{symbolName}')";
                var calleeDescription = DescribeCallee(expression.Callee);
                context.RealmState.Logger?.LogInformation(
                    "[EvaluateCall] Non-callable callee={Callee} type={Type} thisValueType={ThisType}{SymbolSuffix}{SourceInfo}",
                    calleeDescription,
                    typeName,
                    thisValue.ToObject()?.GetType().Name ?? "null",
                    symbolSuffix,
                    sourceInfo);
                var error = StandardLibrary.CreateTypeError(
                    $"Attempted to call a non-callable value '{calleeDescription}' of type '{typeName}'{symbolSuffix}.",
                    context,
                    context.RealmState);
                context.SetThrow(JsValue.FromObjectUnsafe(error));
                context.CallDepth--;
                return JsValue.Undefined;
            }

            // Class constructors cannot be invoked without 'new' (except via super() call)
            if (callable is TypedFunction { IsClassConstructor: true } && expression.Callee is not SuperExpression)
            {
                var error = StandardLibrary.CreateTypeError(
                    "Class constructor cannot be invoked without 'new'",
                    context,
                    context.RealmState);
                context.SetThrow(JsValue.FromObjectUnsafe(error));
                context.CallDepth--;
                return JsValue.Undefined;
            }

            var isAsyncCallable = callable is TypedFunction { IsAsyncLike: true };

            IJsEnvironmentAwareCallable? envAwareHandle = null;
            if (callable is IJsEnvironmentAwareCallable envAware)
            {
                envAware.CallingJsEnvironment = environment;
                envAwareHandle = envAware;
            }

            IEvaluationContextAwareCallable? contextAwareHandle = null;
            if (callable is IEvaluationContextAwareCallable contextAware)
            {
                contextAware.CallingContext = context;
                contextAwareHandle = contextAware;
            }

            DebugAwareHostFunction? debugFunction = null;
            if (callable is DebugAwareHostFunction debugAware)
            {
                debugFunction = debugAware;
                debugFunction.CurrentJsEnvironment = environment;
                debugFunction.CurrentContext = context;
            }

            var callResult = JsValue.Undefined;
            var newTargetForCall = JsValue.Undefined;
            if (expression.Callee is SuperExpression &&
                environment.TryGet(Symbol.NewTarget, out var inheritedNewTarget))
            {
                newTargetForCall = JsValue.FromObjectUnsafe(inheritedNewTarget);
            }

            SuperBinding? superBindingForCall = null;
            if (expression.Callee is SuperExpression)
            {
                superBindingForCall = ExpectSuperBinding(environment, context);
                // Per ES spec 12.3.5.1 SuperCall:
                // After ArgumentListEvaluation, check if the super constructor is actually a constructor.
                // If IsConstructor(func) is false, throw a TypeError exception.
                if (!JsOps.IsConstructor(callable))
                {
                    var error = StandardLibrary.CreateTypeError(
                        "Super constructor is not a constructor",
                        context,
                        context.RealmState);
                    context.SetThrow(JsValue.FromObjectUnsafe(error));
                    context.CallDepth--;
                    return JsValue.Undefined;
                }
            }

            EvalHostFunction? evalHost = null;
            if (callable is EvalHostFunction evalHostFunction)
            {
                evalHost = evalHostFunction;
                var isDirectEvalCall = expression is { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } } &&
                                       thisValue.IsUndefined &&
                                       ReferenceEquals(evalHostFunction.Engine, environment.RealmState?.Engine);
                evalHost.IsDirectCall = isDirectEvalCall;
                evalHost.InClassFieldInitializer = context.InClassFieldInitializer;
            }

            JsEnvironment? thisInitializationEnvironment = null;
            var thisInitializationValue = JsValue.Undefined;
            if (expression.Callee is SuperExpression)
            {
                // First check if we're in an arrow function that has captured a lexical this environment.
                // Arrow functions store a reference to the original constructor's environment so super()
                // can update the correct `this` binding.
                if (environment.TryFindBinding(Symbol.LexicalThisEnvironment, allowUninitialized: true, out _, out var lexicalEnvValue) &&
                    lexicalEnvValue is JsEnvironment lexicalThisEnv)
                {
                    thisInitializationEnvironment = lexicalThisEnv;
                    if (lexicalThisEnv.TryGet(Symbol.ThisInitialized, out var lexicalInitValue))
                    {
                        thisInitializationValue = JsValue.FromObjectUnsafe(lexicalInitValue);
                    }
                }
                // Otherwise, prefer the environment that owns the current `this` binding; the [[ThisInitialized]]
                // marker is defined alongside it for derived constructors.
                else if (environment.TryFindBinding(Symbol.This, allowUninitialized: true, out var thisEnv, out _))
                {
                    thisInitializationEnvironment = thisEnv;
                    if (thisEnv.TryGet(Symbol.ThisInitialized, out var initValue))
                    {
                        thisInitializationValue = JsValue.FromObjectUnsafe(initValue);
                    }
                }

                if (thisInitializationEnvironment is null &&
                    environment.TryFindBinding(Symbol.ThisInitialized, allowUninitialized: true, out var foundEnv,
                        out var foundValue))
                {
                    thisInitializationEnvironment = foundEnv;
                    thisInitializationValue = JsValue.FromObjectUnsafe(foundValue);
                }
            }

            try
            {
                if (callable is TypedFunction typedFunction)
                {
                    callResult = typedFunction.InvokeWithContext(frozenArguments, thisValue, context,
                        newTargetForCall);
                }
                else if (callable is HostFunction hostFunction)
                {
                    callResult = hostFunction.InvokeWithContext(
                        frozenArguments,
                        thisValue,
                        context,
                        newTargetForCall);
                }
                else
                {
                    callResult = callable.Invoke(frozenArguments, thisValue);
                }

                if (expression.Callee is SuperExpression)
                {
                    var callResultObj = callResult.ToObject();
                    var thisAfterSuper = callResultObj;
                    if (callResultObj is not JsObject && callResultObj is not IJsObjectLike)
                    {
                        thisAfterSuper = thisValue.ToObject();
                    }

                    if (context is not null)
                    {
                        context.LastConstructedThis = thisAfterSuper;
                    }

                    context.RealmState?.Logger?.LogInformation(
                        "Super call produced this type={Type}",
                        thisAfterSuper?.GetType().Name ?? "null");

                    if (thisInitializationEnvironment is not null)
                    {
                        var alreadyInitialized = thisInitializationValue.IsUndefined
                            ? (thisInitializationEnvironment.TryGet(Symbol.ThisInitialized, out var initValue)
                                ? JsValue.FromObjectUnsafe(initValue)
                                : JsValue.Undefined)
                            : thisInitializationValue;

                        if (!alreadyInitialized.IsUndefined)
                        {
                            context.RealmState?.Logger?.LogInformation(
                                "Super call pre-check thisInit env={Env} value={Value}",
                                thisInitializationEnvironment.GetHashCode(),
                                alreadyInitialized.ToObject());
                            if (JsOps.ToBoolean(alreadyInitialized.ToObject()))
                            {
                                throw StandardLibrary.ThrowReferenceError(
                                    "Super constructor may only be called once.", context, context.RealmState);
                            }
                        }
                    }

                    var targetEnvironment = thisInitializationEnvironment ?? environment;
                    var hasThisBinding = targetEnvironment.HasBinding(Symbol.This);
                    string beforeType;
                    try
                    {
                        targetEnvironment.TryGet(Symbol.This, out var existingThis);
                        beforeType = existingThis?.GetType().Name ?? "null";
                    }
                    catch (Exception ex)
                    {
                        beforeType = ex.GetType().Name;
                    }
                    context.RealmState?.Logger?.LogInformation(
                        "Super assigning this (hasBinding={HasBinding}, beforeType={BeforeType})",
                        hasThisBinding,
                        beforeType);
                    targetEnvironment.Assign(Symbol.This, thisAfterSuper);
                    try
                    {
                        targetEnvironment.TryGet(Symbol.This, out var afterThis);
                        context.RealmState?.Logger?.LogInformation("Super assigned this now type={Type}",
                            afterThis?.GetType().Name ?? "null");
                    }
                    catch (Exception ex)
                    {
                        context.RealmState?.Logger?.LogInformation("Super assigned this lookup failed {ErrorType}",
                            ex.GetType().Name);
                    }

                    if (targetEnvironment.TryGet(Symbol.Super, out var superBinding) &&
                        superBinding is SuperBinding binding)
                    {
                        var constructorForSuper = superBindingForCall?.Constructor ?? binding.Constructor;
                        var prototypeForSuper = superBindingForCall?.Prototype ?? binding.Prototype;
                        targetEnvironment.Assign(Symbol.Super,
                            new SuperBinding(constructorForSuper, prototypeForSuper, JsValue.FromObjectUnsafe(thisAfterSuper), true));
                    }

                    context.MarkThisInitialized();
                    SetThisInitializationStatus(targetEnvironment,
                        context.IsThisInitialized);

                    if (thisAfterSuper is IJsObjectLike initializedThis &&
                        context.TryPopClassFieldInitializer(out var pendingInitializer) &&
                        pendingInitializer.Constructor is TypedFunction pendingConstructor)
                    {
                        pendingConstructor.InitializeInstance(
                            initializedThis,
                            pendingInitializer.Environment,
                            context);
                        if (context.ShouldStopEvaluation)
                        {
                            if (context.IsThrow)
                            {
                                var thrownDuringInitialization = context.FlowValue;
                                context.Clear();
                                throw new ThrowSignal(thrownDuringInitialization);
                            }

                            return context.FlowValue;
                        }
                    }
                }
            }
            catch (ThrowSignal signal)
            {
                context.RealmState.Logger?.LogInformation(
                    "EvaluateCall caught ThrowSignal type={Type} calleeType={CalleeType}",
                    signal.ThrownValue.ToObject()?.GetType().Name ?? "null",
                    callable.GetType().Name);
                if (isAsyncCallable)
                {
                    context.Clear();
                    callResult = JsValue.FromObjectUnsafe(CreateRejectedPromise(signal.ThrownValue.ToObject(), environment));
                }
                else
                {
                    context.SetThrow(signal.ThrownValue);
                    return signal.ThrownValue;
                }
            }
            catch (Exception ex) when (isAsyncCallable)
            {
                // Any synchronous failure while invoking an async function should surface
                // as a rejected promise rather than throwing out of the call.
                context.Clear();
                callResult = JsValue.FromObjectUnsafe(CreateRejectedPromise(ex, environment));
            }
            finally
            {
                if (evalHost is not null)
                {
                    evalHost.IsDirectCall = false;
                    evalHost.InClassFieldInitializer = false;
                }

                context.CallDepth--;

                debugFunction?.CurrentJsEnvironment = null;
                debugFunction?.CurrentContext = null;

                envAwareHandle?.CallingJsEnvironment = null;
                contextAwareHandle?.CallingContext = null;

                // Return pooled argument arrays
                if (pooledArgsArray is not null)
                {
                    JsValueCache.ReturnArgumentArray(pooledArgsArray);
                }
                if (pooledJsValueArray is not null)
                {
                    JsValueCache.ReturnJsValueArray(pooledJsValueArray);
                }
            }

            switch (isAsyncCallable)
            {
                // If an async callable left a pending throw signal (e.g., default parameter TDZ),
                // translate it into a rejected promise and clear the signal so it does not
                // escape to the caller's context.
                case true when context.IsThrow:
                {
                    var reason = context.FlowValue;
                    context.Clear();
                    return JsValue.FromObjectUnsafe(CreateRejectedPromise(reason, environment));
                }
                case true:
                    // Async functions should never propagate a throw signal; ensure the
                    // calling context stays clear.
                    context.Clear();
                    break;
            }

            return callResult;
        }

        /// <summary>
        ///     Fast-path for plain Map/Set method calls.
        ///     Bypasses prototype lookup, host function creation, and argument array allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryFastPathMapSetCall(
            CallExpression callExpr,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue result)
        {
            result = JsValue.Unit;

            // Only handle simple member expression calls like map.set(), map.get(), etc.
            if (callExpr.Callee is not MemberExpression { IsComputed: false, IsOptional: false } member)
                return false;

            // IMPORTANT: Only use fast path for simple identifier targets (e.g., `myMap.set(...)`)
            // If we evaluate a complex target expression (like `getMap().set(...)`) and it's NOT
            // a Map/Set, the normal path would evaluate it again, causing double execution!
            if (member.Target is not IdentifierExpression)
                return false;

            // Get the method name
            var methodName = member.Property switch
            {
                IdentifierExpression id => id.Name.Name,
                LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
                _ => null
            };

            if (methodName is null)
                return false;

            // Evaluate the target (map or set instance) - safe because it's just an identifier lookup
            var targetJs = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                result = JsValue.Undefined;
                return true;
            }

            var target = targetJs.ToObject();

            // Fast-path for JsMap
            if (target is JsMap { IsPlain: true } map)
            {
                // Check for spread arguments - fall back to normal path if any
                var hasSpread = false;
                foreach (var arg in callExpr.Arguments)
                {
                    if (arg.IsSpread)
                    {
                        hasSpread = true;
                        break;
                    }
                }
                if (!hasSpread)
                {
                    result = methodName switch
                    {
                        "set" when callExpr.Arguments.Length >= 2 =>
                            FastMapSet(map, callExpr.Arguments, environment, context),
                        "get" when callExpr.Arguments.Length >= 1 =>
                            FastMapGet(map, callExpr.Arguments, environment, context),
                        "has" when callExpr.Arguments.Length >= 1 =>
                            FastMapHas(map, callExpr.Arguments, environment, context),
                        "delete" when callExpr.Arguments.Length >= 1 =>
                            FastMapDelete(map, callExpr.Arguments, environment, context),
                        "clear" => FastMapClear(map),
                        _ => JsValue.Unit // Fall back to normal path for other methods (size is a property, not a method)
                    };

                    if (!result.IsUnit)
                        return true;
                }
            }

            // Fast-path for JsSet
            if (target is JsSet { IsPlain: true } set)
            {
                // Check for spread arguments - fall back to normal path if any
                var hasSpread = false;
                foreach (var arg in callExpr.Arguments)
                {
                    if (arg.IsSpread)
                    {
                        hasSpread = true;
                        break;
                    }
                }
                if (!hasSpread)
                {
                    result = methodName switch
                    {
                        "add" when callExpr.Arguments.Length >= 1 =>
                            FastSetAdd(set, callExpr.Arguments, environment, context),
                        "has" when callExpr.Arguments.Length >= 1 =>
                            FastSetHas(set, callExpr.Arguments, environment, context),
                        "delete" when callExpr.Arguments.Length >= 1 =>
                            FastSetDelete(set, callExpr.Arguments, environment, context),
                        "clear" => FastSetClear(set),
                        _ => JsValue.Unit // Fall back to normal path for other methods (size is a property, not a method)
                    };

                    if (!result.IsUnit)
                        return true;
                }
            }

            return false;
        }

        // ---- Map fast-path helpers ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastMapSet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            var value = EvaluateExpression(args[1].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return (JsValue)map.Set(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastMapGet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return JsValue.FromObjectUnsafe(map.Get(key));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastMapHas(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return map.Has(key) ? JsValue.True : JsValue.False;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastMapDelete(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return map.Delete(key) ? JsValue.True : JsValue.False;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastMapClear(JsMap map)
        {
            map.Clear();
            return JsValue.Undefined;
        }

        // ---- Set fast-path helpers ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastSetAdd(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return (JsValue)set.Add(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastSetHas(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return set.Has(value) ? JsValue.True : JsValue.False;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastSetDelete(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return JsValue.Undefined;
            return set.Delete(value) ? JsValue.True : JsValue.False;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static JsValue FastSetClear(JsSet set)
        {
            set.Clear();
            return JsValue.Undefined;
        }

        /// <summary>
        /// Wraps an object?[] array as IReadOnlyList&lt;JsValue&gt; for compatibility with IJsCallable.Invoke.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IReadOnlyList<JsValue> WrapArgumentsAsJsValues(object?[] args)
        {
            var result = new JsValue[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                result[i] = JsValue.FromObjectUnsafe(args[i]);
            }
            return result;
        }
    }
}
