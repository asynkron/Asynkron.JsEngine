using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    internal static object? InvokeCallable(
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
                TypedFunction typedFunction => typedFunction.InvokeWithContext(arguments, thisValue, callingContext).ToObject(),
                HostFunction hostFunction => hostFunction.InvokeWithContext(arguments, thisValue, callingContext).ToObject(),
                _ => callable.Invoke(arguments, thisValue).ToObject()
            };
        }
        finally
        {
            if (envAware is not null)
            {
                envAware.CallingJsEnvironment = previousEnvironment;
            }

            if (contextAware is not null)
            {
                contextAware.CallingContext = null;
            }
        }
    }
}
