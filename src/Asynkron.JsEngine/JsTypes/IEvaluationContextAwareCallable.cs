namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Allows host callables to observe the current evaluation context (scope mode, call depth, etc.).
/// </summary>
public interface IEvaluationContextAwareCallable : IJsCallable
{
    EvaluationContext? CallingContext { get; set; }
}
