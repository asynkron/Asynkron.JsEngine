using System.Collections.Immutable;
using System.Diagnostics;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(CallExpression expression)
    {
        private object? EvaluateCall(JsEnvironment environment, EvaluationContext context)
        {
            using var callActivity = Activity.Current?.StartEvaluatorActivity("CallExpression", context, expression.Source);
            callActivity?.SetTag("js.call.arguments", expression.Arguments.Length);
            callActivity?.SetTag("js.call.optional", expression.IsOptional);
            callActivity?.SetTag("js.call.calleeType", expression.Callee.GetType().Name);

            var (callee, thisValue, skippedOptional) = EvaluateCallTarget(expression.Callee, environment, context);
            if (context.ShouldStopEvaluation || skippedOptional)
            {
                return Symbol.Undefined;
            }

            if (++context.CallDepth > context.MaxCallDepth)
            {
                throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
            }

            if (expression.IsOptional && IsNullish(callee))
            {
                context.CallDepth--;
                return Symbol.Undefined;
            }

            if (callee is not IJsCallable callable)
            {
                // Special-case Function.prototype.apply / call patterns such as
                // Object.prototype.hasOwnProperty.apply(target, args).
                if (expression.Callee is MemberExpression member)
                {
                    if (thisValue is IJsCallable targetFunction &&
                        member.Property is LiteralExpression { Value: string propertyName })
                    {
                        if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                        {
                            return InvokeWithApply(targetFunction, expression.Arguments, environment, context);
                        }

                        if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                        {
                            return InvokeWithCall(targetFunction, expression.Arguments, environment, context);
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
                        var target = EvaluateExpression(inner.Target, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        if (TryGetPropertyValue(target, "formatArgs", out var innerValue) &&
                            innerValue is IJsCallable innerFunction)
                        {
                            return InvokeWithCall(innerFunction, expression.Arguments, environment, context);
                        }
                    }
                }

                var typeName = callee?.GetType().Name ?? "null";
                var sourceInfo = GetSourceInfo(context, expression.Source);
                var symbolName = callee is Symbol sym ? sym.Name : null;
                var symbolSuffix = symbolName is null ? string.Empty : $" (symbol '{symbolName}')";
                var calleeDescription = DescribeCallee(expression.Callee);
                Console.Error.WriteLine(
                    $"[EvaluateCall] Non-callable callee={calleeDescription}, type={typeName}, thisValueType={thisValue?.GetType().Name ?? "null"}{symbolSuffix}{sourceInfo}");
                var error = StandardLibrary.CreateTypeError(
                    $"Attempted to call a non-callable value '{calleeDescription}' of type '{typeName}'{symbolSuffix}.",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                context.CallDepth--;
                return Symbol.Undefined;
            }

            var arguments = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
            foreach (var argument in expression.Arguments)
            {
                if (argument.IsSpread)
                {
                    var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    foreach (var item in EnumerateSpread(spreadValue, context))
                    {
                        arguments.Add(item);
                    }

                    continue;
                }

                arguments.Add(EvaluateExpression(argument.Expression, environment, context));
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
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

            var frozenArguments = FreezeArguments(arguments);

            object? callResult = Symbol.Undefined;
            object? newTargetForCall = null;
            if (expression.Callee is SuperExpression &&
                environment.TryGet(Symbol.NewTarget, out var inheritedNewTarget))
            {
                newTargetForCall = inheritedNewTarget;
            }

            SuperBinding? superBindingForCall = null;
            if (expression.Callee is SuperExpression)
            {
                superBindingForCall = ExpectSuperBinding(environment, context);
            }

            JsEnvironment? thisInitializationEnvironment = null;
            object? thisInitializationValue = null;
            if (expression.Callee is SuperExpression &&
                environment.TryFindBinding(Symbol.ThisInitialized, out var foundEnv, out var foundValue))
            {
                thisInitializationEnvironment = foundEnv;
                thisInitializationValue = foundValue;
            }

            try
            {
                if (callable is TypedFunction typedFunction)
                {
                    callResult = typedFunction.InvokeWithContext(frozenArguments, thisValue, context,
                        newTargetForCall);
                }
                else
                {
                    callResult = callable.Invoke(frozenArguments, thisValue);
                }

                if (expression.Callee is SuperExpression)
                {
                    var thisAfterSuper = callResult;
                    if (callResult is not JsObject && callResult is not IJsObjectLike)
                    {
                        thisAfterSuper = thisValue;
                    }

                    if (thisInitializationEnvironment is not null &&
                        thisInitializationEnvironment.TryGet(Symbol.ThisInitialized, out var alreadyInitialized) &&
                        JsOps.ToBoolean(alreadyInitialized))
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            "Super constructor may only be called once.", context, context.RealmState);
                    }

                    environment.Assign(Symbol.This, thisAfterSuper);

                    if (environment.TryGet(Symbol.Super, out var superBinding) && superBinding is SuperBinding binding)
                    {
                        var constructorForSuper = superBindingForCall?.Constructor ?? binding.Constructor;
                        var prototypeForSuper = superBindingForCall?.Prototype ?? binding.Prototype;
                        environment.Assign(Symbol.Super,
                            new SuperBinding(constructorForSuper, prototypeForSuper, thisAfterSuper, true));
                    }

                    context.MarkThisInitialized();
                    SetThisInitializationStatus(thisInitializationEnvironment ?? environment,
                        context.IsThisInitialized);

                    if (thisAfterSuper is JsObject initializedThis &&
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
                if (isAsyncCallable)
                {
                    context.Clear();
                    callResult = CreateRejectedPromise(signal.ThrownValue, environment);
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
                callResult = CreateRejectedPromise(ex, environment);
            }
            finally
            {
                context.CallDepth--;

                debugFunction?.CurrentJsEnvironment = null;
                debugFunction?.CurrentContext = null;

                envAwareHandle?.CallingJsEnvironment = null;
                contextAwareHandle?.CallingContext = null;
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
                    return CreateRejectedPromise(reason, environment);
                }
                case true:
                    // Async functions should never propagate a throw signal; ensure the
                    // calling context stays clear.
                    context.Clear();
                    break;
            }

            return callResult;
        }
    }
}
