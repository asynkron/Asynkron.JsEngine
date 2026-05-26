#region

using System.Runtime.CompilerServices;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Invokes a callable with no arguments and returns the result as JsValue.
    /// This overload avoids interface-based argument setup for the common zero-argument case.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue InvokeCallableNoArgs(
        IJsCallable callable,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        return InvokeCallableJsValueGeneric(callable, Array.Empty<JsValue>(), thisValue, callingContext, callingEnvironment);
    }

    /// <summary>
    /// Invokes a callable with two arguments and returns the result as JsValue.
    /// This overload avoids array allocation for common binary helper calls.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue InvokeCallableTwoArgs(
        IJsCallable callable,
        JsValue arg0,
        JsValue arg1,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        if (callable is SyncFunctionInvoker typedFunction && callingContext is not null)
        {
            IJsEnvironmentAwareCallable? envAware = null;
            JsEnvironment? previousEnvironment = null;
            if (callingEnvironment is not null && callable is IJsEnvironmentAwareCallable environmentAware)
            {
                envAware = environmentAware;
                previousEnvironment = envAware.CallingJsEnvironment;
                envAware.CallingJsEnvironment = callingEnvironment;
            }

            IEvaluationContextAwareCallable? contextAware = null;
            if (callable is IEvaluationContextAwareCallable evaluationContextAware)
            {
                contextAware = evaluationContextAware;
                contextAware.CallingContext = callingContext;
            }

            try
            {
                return typedFunction.InvokeWithContext2(arg0, arg1, thisValue, callingContext);
            }
            finally
            {
                envAware?.CallingJsEnvironment = previousEnvironment;
                contextAware?.CallingContext = null;
            }
        }

        var args = new TwoValueArgs(arg0, arg1);
        return InvokeCallableJsValueGeneric(callable, args, thisValue, callingContext, callingEnvironment);
    }

    /// <summary>
    /// Invokes a callable with three arguments and returns the result as JsValue.
    /// This overload avoids array allocation for common ternary helper calls.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue InvokeCallableThreeArgs(
        IJsCallable callable,
        JsValue arg0,
        JsValue arg1,
        JsValue arg2,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        if (callable is SyncFunctionInvoker typedFunction && callingContext is not null)
        {
            IJsEnvironmentAwareCallable? envAware = null;
            JsEnvironment? previousEnvironment = null;
            if (callingEnvironment is not null && callable is IJsEnvironmentAwareCallable environmentAware)
            {
                envAware = environmentAware;
                previousEnvironment = envAware.CallingJsEnvironment;
                envAware.CallingJsEnvironment = callingEnvironment;
            }

            IEvaluationContextAwareCallable? contextAware = null;
            if (callable is IEvaluationContextAwareCallable evaluationContextAware)
            {
                contextAware = evaluationContextAware;
                contextAware.CallingContext = callingContext;
            }

            try
            {
                return typedFunction.InvokeWithContext3(arg0, arg1, arg2, thisValue, callingContext);
            }
            finally
            {
                envAware?.CallingJsEnvironment = previousEnvironment;
                contextAware?.CallingContext = null;
            }
        }

        var args = new ThreeValueArgs(arg0, arg1, arg2);
        return InvokeCallableJsValueGeneric(callable, args, thisValue, callingContext, callingEnvironment);
    }

    /// <summary>
    /// Invokes a callable with a single argument and returns the result as JsValue.
    /// This overload avoids array allocation for the common single-argument case.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue InvokeCallableSingleArg(
        IJsCallable callable,
        JsValue argument,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        if (callable is SyncFunctionInvoker typedFunction && callingContext is not null)
        {
            return typedFunction.InvokeWithContext1(argument, thisValue, callingContext);
        }

        var args = new SingleValueArgs(argument);
        return InvokeCallableJsValueGeneric(callable, args, thisValue, callingContext, callingEnvironment);
    }

    /// <summary>
    /// Generic overload that avoids boxing for struct argument lists like SingleValueArgs.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue InvokeCallableJsValueGeneric<TArgs>(
        IJsCallable callable,
        TArgs arguments,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
        where TArgs : IReadOnlyList<JsValue>
    {
        IJsEnvironmentAwareCallable? envAware = null;
        JsEnvironment? previousEnvironment = null;
        if (callingEnvironment is not null && callable is IJsEnvironmentAwareCallable environmentAware)
        {
            envAware = environmentAware;
            previousEnvironment = envAware.CallingJsEnvironment;
            envAware.CallingJsEnvironment = callingEnvironment;
        }

        IEvaluationContextAwareCallable? contextAware = null;
        if (callingContext is not null && callable is IEvaluationContextAwareCallable evaluationContextAware)
        {
            contextAware = evaluationContextAware;
            contextAware.CallingContext = callingContext;
        }

        try
        {
            return callable switch
            {
                SyncFunctionInvoker typedFunction => typedFunction.InvokeWithContext<TArgs>(arguments, thisValue,
                    callingContext),
                HostFunction hostFunction => hostFunction.InvokeWithContext(arguments, thisValue, callingContext),
                _ => callable.Invoke(arguments, thisValue)
            };
        }
        finally
        {
            envAware?.CallingJsEnvironment = previousEnvironment;
            contextAware?.CallingContext = null;
        }
    }

    /// <summary>
    /// Invokes a callable and returns the result as JsValue.
    /// This is the preferred method to avoid boxing.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static JsValue InvokeCallableJsValue(
        IJsCallable callable,
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        return InvokeCallableJsValueGeneric(callable, arguments, thisValue, callingContext, callingEnvironment);
    }
}
