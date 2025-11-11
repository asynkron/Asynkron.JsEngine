namespace Asynkron.JsEngine;

/// <summary>
/// Exception used at boundaries to propagate JavaScript throw statements across C# call stacks.
/// Within the evaluator, throws are managed via EvaluationContext state machine.
/// This exception is thrown when a throw escapes a function boundary or reaches the top level.
/// </summary>
internal sealed class ThrowSignal(object? thrownValue = null) : Exception
{
    public object? ThrownValue { get; } = thrownValue;
}