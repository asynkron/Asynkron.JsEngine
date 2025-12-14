using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
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
        private JsValue EvaluateCall(JsEnvironment environment, EvaluationContext context)
        {
            // Fast-path for plain Map/Set method calls - bypasses prototype lookup and host function machinery
            if (TryFastPathMapSetCall(expression, environment, context, out var fastResult))
            {
                return JsValue.FromObject(fastResult);
            }

            using var callActivity = Activity.Current?.StartEvaluatorActivity("CallExpression", context, expression.Source);
            callActivity?.SetTag("js.call.arguments", expression.Arguments.Length);
            callActivity?.SetTag("js.call.optional", expression.IsOptional);
            callActivity?.SetTag("js.call.calleeType", expression.Callee.GetType().Name);

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

            if (expression.IsOptional && IsNullish(callee))
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
            object?[]? pooledArgsArray = null; // Track if we used a pooled array
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

                        var arr = JsValueCache.CreateArgs(v0.ToObject());
                        pooledArgsArray = arr;
                        frozenArguments = WrapArgumentsAsJsValues(arr);
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

                        var arr = JsValueCache.CreateArgs(v0.ToObject(), v1.ToObject());
                        pooledArgsArray = arr;
                        frozenArguments = WrapArgumentsAsJsValues(arr);
                        break;
                    }
                    default:
                    {
                        // Use pooled arrays for small argument counts (3-4)
                        var argsArray = argCount <= 4
                            ? JsValueCache.RentArgumentArray(argCount)
                            : new object?[argCount];

                        if (argCount <= 4)
                        {
                            pooledArgsArray = argsArray; // Remember to return it
                        }

                        for (var i = 0; i < argCount; i++)
                        {
                            argsArray[i] = EvaluateExpression(expression.Arguments[i].Expression, environment, context).ToObject();
                            if (context.ShouldStopEvaluation)
                            {
                                if (pooledArgsArray is not null)
                                {
                                    JsValueCache.ReturnArgumentArray(pooledArgsArray);
                                }
                                context.CallDepth--;
                                return JsValue.Undefined;
                            }
                        }

                        frozenArguments = WrapArgumentsAsJsValues(argsArray);
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
                                argsBuilder.Add(JsValue.FromObject(item));
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

            if (callee is not IJsCallable callable)
            {
                // Special-case Function.prototype.apply / call patterns such as
                // Object.prototype.hasOwnProperty.apply(target, args).
                if (expression.Callee is MemberExpression member)
                {
                    if (thisValue.TryGetObject<IJsCallable>(out var targetFunction) &&
                        member.Property is LiteralExpression { Value: string propertyName })
                    {
                        if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                        {
                            return JsValue.FromObject(InvokeWithApply(targetFunction, expression.Arguments, environment, context));
                        }

                        if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                        {
                            return JsValue.FromObject(InvokeWithCall(targetFunction, expression.Arguments, environment, context));
                        }
                    }

                    // Fallback for patterns like `obj.formatArgs.call(this, ...)`
                    // where `formatArgs` is a callable copied onto `obj` but the
                    // `.call` helper is missing or not modeled. In that case we
                    // invoke the underlying function directly with the provided
                    // `this` value and arguments instead of throwing.
                    if (member is
                        {
                            Property: LiteralExpression { Value: "call" }, Target: MemberExpression
                            {
                                Property: LiteralExpression { Value: "formatArgs" }
                            } inner
                        })
                    {
                        var targetJs = EvaluateExpression(inner.Target, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (TryGetPropertyValue(targetJs.ToObject(), "formatArgs", out var innerValue) &&
                            innerValue is IJsCallable innerFunction)
                        {
                            return JsValue.FromObject(InvokeWithCall(innerFunction, expression.Arguments, environment, context));
                        }
                    }
                }

                var typeName = callee?.GetType().Name ?? "null";
                var sourceInfo = GetSourceInfo(context, expression.Source);
                var symbolName = callee is Symbol sym ? sym.Name : null;
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
                context.SetThrow(error);
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
                context.SetThrow(error);
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

            JsValue callResult = JsValue.Undefined;
            JsValue newTargetForCall = JsValue.Undefined;
            if (expression.Callee is SuperExpression &&
                environment.TryGet(Symbol.NewTarget, out var inheritedNewTarget))
            {
                newTargetForCall = JsValue.FromObject(inheritedNewTarget);
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
                    context.SetThrow(error);
                    context.CallDepth--;
                    return JsValue.Undefined;
                }
            }

            EvalHostFunction? evalHost = null;
            if (callable is EvalHostFunction evalHostFunction)
            {
                evalHost = evalHostFunction;
                var isDirectEvalCall = expression is { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } } &&
                                       ReferenceEquals(thisValue, Symbol.Undefined) &&
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
                        thisInitializationValue = JsValue.FromObject(lexicalInitValue);
                    }
                }
                // Otherwise, prefer the environment that owns the current `this` binding; the [[ThisInitialized]]
                // marker is defined alongside it for derived constructors.
                else if (environment.TryFindBinding(Symbol.This, allowUninitialized: true, out var thisEnv, out _))
                {
                    thisInitializationEnvironment = thisEnv;
                    if (thisEnv.TryGet(Symbol.ThisInitialized, out var initValue))
                    {
                        thisInitializationValue = JsValue.FromObject(initValue);
                    }
                }

                if (thisInitializationEnvironment is null &&
                    environment.TryFindBinding(Symbol.ThisInitialized, allowUninitialized: true, out var foundEnv,
                        out var foundValue))
                {
                    thisInitializationEnvironment = foundEnv;
                    thisInitializationValue = JsValue.FromObject(foundValue);
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
                                ? JsValue.FromObject(initValue)
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
                            new SuperBinding(constructorForSuper, prototypeForSuper, JsValue.FromObject(thisAfterSuper), true));
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

                            return JsValue.FromObject(context.FlowValue);
                        }
                    }
                }
            }
            catch (ThrowSignal signal)
            {
                context.RealmState.Logger?.LogInformation(
                    "EvaluateCall caught ThrowSignal type={Type} calleeType={CalleeType}",
                    signal.ThrownValue?.GetType().Name ?? "null",
                    callable.GetType().Name);
                if (isAsyncCallable)
                {
                    context.Clear();
                    callResult = JsValue.FromObject(CreateRejectedPromise(signal.ThrownValue, environment));
                }
                else
                {
                    context.SetThrow(signal.ThrownValue);
                    return JsValue.FromObject(signal.ThrownValue);
                }
            }
            catch (Exception ex) when (isAsyncCallable)
            {
                // Any synchronous failure while invoking an async function should surface
                // as a rejected promise rather than throwing out of the call.
                context.Clear();
                callResult = JsValue.FromObject(CreateRejectedPromise(ex, environment));
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

                // Return pooled argument array
                if (pooledArgsArray is not null)
                {
                    JsValueCache.ReturnArgumentArray(pooledArgsArray);
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
                    return JsValue.FromObject(CreateRejectedPromise(reason, environment));
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
            out object? result)
        {
            result = null;

            // Only handle simple member expression calls like map.set(), map.get(), etc.
            if (callExpr.Callee is not MemberExpression { IsComputed: false, IsOptional: false } member)
                return false;

            // IMPORTANT: Only use fast path for simple identifier targets (e.g., `myMap.set(...)`)
            // If we evaluate a complex target expression (like `getMap().set(...)`) and it's NOT
            // a Map/Set, the normal path would evaluate it again, causing double execution!
            if (member.Target is not IdentifierExpression)
                return false;

            // Get the method name
            string? methodName = member.Property switch
            {
                IdentifierExpression id => id.Name.Name,
                LiteralExpression { Value: string s } => s,
                _ => null
            };

            if (methodName is null)
                return false;

            // Evaluate the target (map or set instance) - safe because it's just an identifier lookup
            var targetJs = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                result = Symbol.Undefined;
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
                        _ => null // Fall back to normal path for other methods (size is a property, not a method)
                    };

                    if (result is not null || methodName is "clear")
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
                        _ => null // Fall back to normal path for other methods (size is a property, not a method)
                    };

                    if (result is not null || methodName is "clear")
                        return true;
                }
            }

            return false;
        }

        // ---- Map fast-path helpers ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastMapSet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            var value = EvaluateExpression(args[1].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return map.Set(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastMapGet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return map.Get(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastMapHas(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return map.Has(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastMapDelete(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var key = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return map.Delete(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastMapClear(JsMap map)
        {
            map.Clear();
            return Symbol.Undefined;
        }

        // ---- Set fast-path helpers ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastSetAdd(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return set.Add(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastSetHas(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return set.Has(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastSetDelete(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env, EvaluationContext ctx)
        {
            var value = EvaluateExpression(args[0].Expression, env, ctx).ToObject();
            if (ctx.ShouldStopEvaluation) return Symbol.Undefined;
            return set.Delete(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? FastSetClear(JsSet set)
        {
            set.Clear();
            return Symbol.Undefined;
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
                result[i] = JsValue.FromObject(args[i]);
            }
            return result;
        }
    }
}
