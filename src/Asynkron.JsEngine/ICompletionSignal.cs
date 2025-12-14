using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

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
internal sealed class ReturnCompletionSignal : ICompletionSignal
{
    public object? Value { get; }
    public JsValue JsValue { get; }

    public ReturnCompletionSignal(object? value)
    {
        Value = value;
        JsValue = JsValue.FromObject(value);
    }

    public ReturnCompletionSignal(JsValue jsValue)
    {
        JsValue = jsValue;
        Value = jsValue.ToObject();
    }
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
///     Signal indicating a yield expression was encountered (in generator context).
/// </summary>
internal sealed class YieldCompletionSignal : ICompletionSignal
{
    public object? Value { get; }
    public JsValue JsValue { get; }
    public IJsObjectLike? IteratorResultObject { get; }

    public YieldCompletionSignal(object? value, IJsObjectLike? iteratorResultObject = null)
    {
        Value = value;
        JsValue = JsValue.FromObject(value);
        IteratorResultObject = iteratorResultObject;
    }

    public YieldCompletionSignal(JsValue jsValue, IJsObjectLike? iteratorResultObject = null)
    {
        JsValue = jsValue;
        Value = jsValue.ToObject();
        IteratorResultObject = iteratorResultObject;
    }
}

/// <summary>
///     Signal indicating a throw statement was encountered.
/// </summary>
internal sealed class ThrowFlowCompletionSignal : ICompletionSignal
{
    public object? Value { get; }
    public JsValue JsValue { get; }

    public ThrowFlowCompletionSignal(object? value)
    {
        Value = value;
        JsValue = JsValue.FromObject(value);
    }

    public ThrowFlowCompletionSignal(JsValue jsValue)
    {
        JsValue = jsValue;
        Value = jsValue.ToObject();
    }
}
