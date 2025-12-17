using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
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
                TypedFunction typedFunction => typedFunction.InvokeWithContext(arguments, thisValue, callingContext),
                HostFunction hostFunction => hostFunction.InvokeWithContext(arguments, thisValue, callingContext),
                _ => callable.Invoke(arguments, thisValue),
            };
        }
        finally
        {
            envAware?.CallingJsEnvironment = previousEnvironment;

            contextAware?.CallingContext = null;
        }
    }

    /// <summary>
    /// Invokes a callable and returns the result as object?.
    /// This is for backward compatibility - prefer InvokeCallableJsValue when possible.
    /// </summary>
    internal static object? InvokeCallable(
        IJsCallable callable,
        IReadOnlyList<JsValue> arguments,
        JsValue thisValue,
        EvaluationContext? callingContext,
        JsEnvironment? callingEnvironment = null)
    {
        return InvokeCallableJsValue(callable, arguments, thisValue, callingContext, callingEnvironment).ToObject();
    }
}
