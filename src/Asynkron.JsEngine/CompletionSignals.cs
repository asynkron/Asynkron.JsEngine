#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Represents a control flow signal used to manage JavaScript control flow statements
///     (return, break, continue, yield, throw, pending await) as typed result values instead of state machine.
/// </summary>
public interface ICompletionSignal
{
}

/// <summary>
///     Signal indicating a break statement was encountered.
/// </summary>
internal sealed record BreakCompletionSignal(Symbol? Label = null) : ICompletionSignal;

/// <summary>
///     Signal indicating a continue statement was encountered.
/// </summary>
internal sealed record ContinueCompletionSignal(Symbol? Label = null) : ICompletionSignal;

/// <summary>
///     Signal indicating a return statement was encountered.
/// </summary>
internal sealed class ReturnCompletionSignal(JsValue jsValue) : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
}

/// <summary>
///     Signal indicating a throw statement was encountered.
/// </summary>
internal sealed class ThrowFlowCompletionSignal(JsValue jsValue) : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
}

/// <summary>
///     Signal indicating a yield expression was encountered (in generator context).
/// </summary>
internal sealed class YieldCompletionSignal(JsValue jsValue, IJsObjectLike? iteratorResultObject = null)
    : ICompletionSignal
{
    public JsValue JsValue { get; } = jsValue;
    public IJsObjectLike? IteratorResultObject { get; } = iteratorResultObject;
}

/// <summary>
///     Signal indicating evaluation should suspend due to a pending await.
/// </summary>
internal sealed class PendingAwaitCompletionSignal : ICompletionSignal
{
    internal static readonly PendingAwaitCompletionSignal Instance = new();

    private PendingAwaitCompletionSignal()
    {
    }
}
