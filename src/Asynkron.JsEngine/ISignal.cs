namespace Asynkron.JsEngine;

/// <summary>
/// Represents a control flow signal used to manage JavaScript control flow statements
/// (return, break, continue, yield, throw) as typed result values instead of state machine.
/// </summary>
internal interface ISignal
{
}

/// <summary>
/// Signal indicating a return statement was encountered.
/// </summary>
internal sealed record ReturnSignal(object? Value) : ISignal;

/// <summary>
/// Signal indicating a break statement was encountered.
/// </summary>
internal sealed record BreakSignal() : ISignal;

/// <summary>
/// Signal indicating a continue statement was encountered.
/// </summary>
internal sealed record ContinueSignal() : ISignal;

/// <summary>
/// Signal indicating a yield expression was encountered (in generator context).
/// </summary>
internal sealed record YieldSignal(object? Value) : ISignal;

/// <summary>
/// Signal indicating a throw statement was encountered.
/// </summary>
internal sealed record ThrowFlowSignal(object? Value) : ISignal;