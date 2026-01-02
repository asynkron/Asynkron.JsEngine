#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Invokes a callable with a single argument and returns the result as JsValue.
    /// This overload avoids array allocation for the common single-argument case.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsValue InvokeCallableSingleArg(
        IJsCallable callable,
        JsValue argument,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        var args = new SingleValueArgs(argument);
        return InvokeCallableJsValueGeneric(callable, args, thisValue, callingContext, callingEnvironment);
    }

    /// <summary>
    /// Generic overload that avoids boxing for struct argument lists like SingleValueArgs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                SyncFunctionInvoker typedFunction => typedFunction.InvokeWithContext(arguments, thisValue,
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
    internal static JsValue InvokeCallableJsValue(
        IJsCallable callable,
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
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
                SyncFunctionInvoker typedFunction => typedFunction.InvokeWithContext(arguments, thisValue,
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
}
