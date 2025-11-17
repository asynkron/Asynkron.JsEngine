using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

/// <summary>
/// Tracks the current control flow state during evaluation using typed signals.
/// Used as an alternative to exception-based control flow.
/// </summary>
public sealed class EvaluationContext
{
    /// <summary>
    /// The current control flow signal, if any.
    /// </summary>
    public ISignal? CurrentSignal { get; private set; }

    /// <summary>
    /// The current source reference for error reporting.
    /// </summary>
    public SourceReference? SourceReference { get; set; }

    /// <summary>
    /// Stack of enclosing labels (innermost first). Used to determine if a labeled
    /// break/continue should be handled by the current statement.
    /// </summary>
    private Stack<Symbol> _labelStack = new();

    /// <summary>
    /// Pushes a label onto the label stack.
    /// </summary>
    public void PushLabel(Symbol label)
    {
        _labelStack.Push(label);
    }

    /// <summary>
    /// Pops a label from the label stack.
    /// </summary>
    public void PopLabel()
    {
        if (_labelStack.Count > 0)
        {
            _labelStack.Pop();
        }
    }

    /// <summary>
    /// Returns the current innermost label, or null if not in a labeled context.
    /// </summary>
    public Symbol? CurrentLabel => _labelStack.Count > 0 ? _labelStack.Peek() : null;

    /// <summary>
    /// Checks if a label is in the current label stack.
    /// </summary>
    public bool IsLabelInScope(Symbol label)
    {
        return _labelStack.Contains(label);
    }

    /// <summary>
    /// The value associated with the control flow (for Return, Throw, and Yield signals).
    /// </summary>
    public object? FlowValue => CurrentSignal switch
    {
        ReturnSignal rs => rs.Value,
        ThrowFlowSignal ts => ts.Value,
        YieldSignal ys => ys.Value,
        _ => null
    };

    /// <summary>
    /// Sets the context to Return state with the given value.
    /// </summary>
    public void SetReturn(object? value)
    {
        CurrentSignal = new ReturnSignal(value);
    }

    /// <summary>
    /// Sets the context to Break state.
    /// </summary>
    public void SetBreak(Symbol? label = null)
    {
        CurrentSignal = new BreakSignal(label);
    }

    /// <summary>
    /// Sets the context to Continue state.
    /// </summary>
    public void SetContinue(Symbol? label = null)
    {
        CurrentSignal = new ContinueSignal(label);
    }

    /// <summary>
    /// Sets the context to Throw state with the given value.
    /// </summary>
    public void SetThrow(object? value)
    {
        CurrentSignal = new ThrowFlowSignal(value);
    }

    /// <summary>
    /// Sets the context to Yield state with the given value.
    /// </summary>
    public void SetYield(object? value)
    {
        CurrentSignal = new YieldSignal(value);
    }

    /// <summary>
    /// Clears the Continue signal (used when a loop consumes it).
    /// </summary>
    public void ClearContinue()
    {
        if (CurrentSignal is ContinueSignal)
        {
            CurrentSignal = null;
        }
    }

    /// <summary>
    /// Clears the Continue signal only if it matches the given label (or has no label).
    /// Returns true if the signal was cleared, false if it should propagate.
    /// </summary>
    public bool TryClearContinue(Symbol? label)
    {
        if (CurrentSignal is not ContinueSignal continueSignal)
        {
            return false;
        }

        // If the continue has no label, or if it matches the provided label, clear it
        if (continueSignal.Label is not null && (label is null || !ReferenceEquals(continueSignal.Label, label)))
        {
            return false;
        }

        CurrentSignal = null;
        return true;
        // Continue has a different label, let it propagate
    }

    /// <summary>
    /// Clears the Break signal (used when a loop or switch consumes it).
    /// </summary>
    public void ClearBreak()
    {
        if (CurrentSignal is BreakSignal)
        {
            CurrentSignal = null;
        }
    }

    /// <summary>
    /// Clears the Break signal only if it matches the given label (or has no label).
    /// Returns true if the signal was cleared, false if it should propagate.
    /// </summary>
    public bool TryClearBreak(Symbol? label)
    {
        if (CurrentSignal is not BreakSignal breakSignal)
        {
            return false;
        }

        // If the break has no label, or if it matches the provided label, clear it
        if (breakSignal.Label is not null && (label is null || !ReferenceEquals(breakSignal.Label, label)))
        {
            return false;
        }

        CurrentSignal = null;
        return true;
        // Break has a different label, let it propagate
    }

    /// <summary>
    /// Clears the Return signal (used when a function consumes it).
    /// </summary>
    public void ClearReturn()
    {
        if (CurrentSignal is ReturnSignal)
        {
            CurrentSignal = null;
        }
    }

    /// <summary>
    /// Clears any control flow signal.
    /// </summary>
    public void Clear()
    {
        CurrentSignal = null;
    }

    /// <summary>
    /// Returns true if evaluation should stop (any signal is present).
    /// </summary>
    public bool ShouldStopEvaluation => CurrentSignal is not null;

    /// <summary>
    /// Returns true if the current signal is Return.
    /// </summary>
    public bool IsReturn => CurrentSignal is ReturnSignal;

    /// <summary>
    /// Returns true if the current signal is Throw.
    /// </summary>
    public bool IsThrow => CurrentSignal is ThrowFlowSignal;

    /// <summary>
    /// Returns true if the current signal is Yield.
    /// </summary>
    public bool IsYield => CurrentSignal is YieldSignal;
}
