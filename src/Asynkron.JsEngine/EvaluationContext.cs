namespace Asynkron.JsEngine;

/// <summary>
/// Tracks the current control flow state during evaluation using typed signals.
/// Used as an alternative to exception-based control flow.
/// </summary>
internal sealed class EvaluationContext
{
    /// <summary>
    /// The current control flow signal, if any.
    /// </summary>
    public ISignal? CurrentSignal { get; private set; }
    
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
    public void SetBreak()
    {
        CurrentSignal = new BreakSignal();
    }
    
    /// <summary>
    /// Sets the context to Continue state.
    /// </summary>
    public void SetContinue()
    {
        CurrentSignal = new ContinueSignal();
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
    /// Clears the Yield signal (used when a generator consumes it).
    /// </summary>
    public void ClearYield()
    {
        if (CurrentSignal is YieldSignal)
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
    /// Returns true if the current signal is Break.
    /// </summary>
    public bool IsBreak => CurrentSignal is BreakSignal;
    
    /// <summary>
    /// Returns true if the current signal is Continue.
    /// </summary>
    public bool IsContinue => CurrentSignal is ContinueSignal;
    
    /// <summary>
    /// Returns true if the current signal is Throw.
    /// </summary>
    public bool IsThrow => CurrentSignal is ThrowFlowSignal;
    
    /// <summary>
    /// Returns true if the current signal is Yield.
    /// </summary>
    public bool IsYield => CurrentSignal is YieldSignal;
    
    /// <summary>
    /// The possible control flow states during evaluation.
    /// </summary>
    [Obsolete("Use ISignal pattern matching instead")]
    public enum ControlFlow
    {
        /// <summary>Normal execution - no control flow interruption</summary>
        None,
        
        /// <summary>Return statement encountered</summary>
        Return,
        
        /// <summary>Break statement encountered</summary>
        Break,
        
        /// <summary>Continue statement encountered</summary>
        Continue,
        
        /// <summary>Throw statement encountered</summary>
        Throw,
        
        /// <summary>Yield expression encountered (in generator context)</summary>
        Yield
    }
    
    /// <summary>
    /// The current control flow state.
    /// </summary>
    [Obsolete("Use CurrentSignal with pattern matching instead")]
    public ControlFlow Flow => CurrentSignal switch
    {
        null => ControlFlow.None,
        ReturnSignal => ControlFlow.Return,
        BreakSignal => ControlFlow.Break,
        ContinueSignal => ControlFlow.Continue,
        ThrowFlowSignal => ControlFlow.Throw,
        YieldSignal => ControlFlow.Yield,
        _ => ControlFlow.None
    };
}
