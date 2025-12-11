using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine;

/// <summary>
///     Represents a control flow signal used to manage JavaScript control flow statements
///     (return, break, continue, yield, throw) as typed result values instead of state machine.
/// </summary>
public interface ICompletionSignal
{
}

/// <summary>
///     Signal indicating a return statement was encountered.
/// </summary>
internal sealed record ReturnCompletionSignal(object? Value) : ICompletionSignal;

/// <summary>
///     Signal indicating a break statement was encountered.
/// </summary>
internal sealed record BreakCompletionSignal(Symbol? Label = null) : ICompletionSignal;

/// <summary>
///     Signal indicating a continue statement was encountered.
/// </summary>
internal sealed record ContinueCompletionSignal(Symbol? Label = null) : ICompletionSignal;

/// <summary>
///     Signal indicating a yield expression was encountered (in generator context).
/// </summary>
/// <param name="Value">The yielded value.</param>
/// <param name="IteratorResultObject">Optional original iterator result object for yield* to preserve done property.</param>
internal sealed record YieldCompletionSignal(object? Value, JsTypes.IJsObjectLike? IteratorResultObject = null) : ICompletionSignal;

/// <summary>
///     Signal indicating a throw statement was encountered.
/// </summary>
internal sealed record ThrowFlowCompletionSignal(object? Value) : ICompletionSignal;
