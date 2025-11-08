namespace Asynkron.JsEngine;

/// <summary>
/// Tracks the current control flow state during evaluation.
/// Used as an alternative to exception-based control flow signals.
/// </summary>
internal sealed class EvaluationContext
{
    /// <summary>
    /// The possible control flow states during evaluation.
    /// </summary>
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
    public ControlFlow Flow { get; private set; } = ControlFlow.None;
    
    /// <summary>
    /// The value associated with the control flow (for Return and Throw).
    /// </summary>
    public object? FlowValue { get; private set; }
    
    /// <summary>
    /// Sets the context to Return state with the given value.
    /// </summary>
    public void SetReturn(object? value)
    {
        Flow = ControlFlow.Return;
        FlowValue = value;
    }
    
    /// <summary>
    /// Sets the context to Break state.
    /// </summary>
    public void SetBreak()
    {
        Flow = ControlFlow.Break;
        FlowValue = null;
    }
    
    /// <summary>
    /// Sets the context to Continue state.
    /// </summary>
    public void SetContinue()
    {
        Flow = ControlFlow.Continue;
        FlowValue = null;
    }
    
    /// <summary>
    /// Sets the context to Throw state with the given value.
    /// </summary>
    public void SetThrow(object? value)
    {
        Flow = ControlFlow.Throw;
        FlowValue = value;
    }
    
    /// <summary>
    /// Sets the context to Yield state with the given value.
    /// </summary>
    public void SetYield(object? value)
    {
        Flow = ControlFlow.Yield;
        FlowValue = value;
    }
    
    /// <summary>
    /// Clears the Continue state (used when a loop consumes it).
    /// </summary>
    public void ClearContinue()
    {
        if (Flow == ControlFlow.Continue)
        {
            Flow = ControlFlow.None;
        }
    }
    
    /// <summary>
    /// Clears the Break state (used when a loop or switch consumes it).
    /// </summary>
    public void ClearBreak()
    {
        if (Flow == ControlFlow.Break)
        {
            Flow = ControlFlow.None;
        }
    }
    
    /// <summary>
    /// Clears the Return state (used when a function consumes it).
    /// </summary>
    public void ClearReturn()
    {
        if (Flow == ControlFlow.Return)
        {
            Flow = ControlFlow.None;
        }
    }
    
    /// <summary>
    /// Clears the Yield state (used when a generator consumes it).
    /// </summary>
    public void ClearYield()
    {
        if (Flow == ControlFlow.Yield)
        {
            Flow = ControlFlow.None;
        }
    }
    
    /// <summary>
    /// Clears any control flow state (resets to None).
    /// </summary>
    public void Clear()
    {
        Flow = ControlFlow.None;
        FlowValue = null;
    }
    
    /// <summary>
    /// Returns true if evaluation should stop (any control flow except None).
    /// </summary>
    public bool ShouldStopEvaluation => Flow != ControlFlow.None;
    
    /// <summary>
    /// Returns true if the current state is Return.
    /// </summary>
    public bool IsReturn => Flow == ControlFlow.Return;
    
    /// <summary>
    /// Returns true if the current state is Break.
    /// </summary>
    public bool IsBreak => Flow == ControlFlow.Break;
    
    /// <summary>
    /// Returns true if the current state is Continue.
    /// </summary>
    public bool IsContinue => Flow == ControlFlow.Continue;
    
    /// <summary>
    /// Returns true if the current state is Throw.
    /// </summary>
    public bool IsThrow => Flow == ControlFlow.Throw;
    
    /// <summary>
    /// Returns true if the current state is Yield.
    /// </summary>
    public bool IsYield => Flow == ControlFlow.Yield;
}
