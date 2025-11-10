namespace Asynkron.JsEngine;

/// <summary>
/// Represents a debug message captured during JavaScript execution.
/// Contains information about the execution context and environment at the time __debug() was called.
/// </summary>
public sealed class DebugMessage
{
    /// <summary>
    /// Gets a dictionary of all variables and their values in the current scope and parent scopes.
    /// The key is the variable name, and the value is the current value of that variable.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }

    /// <summary>
    /// Gets the control flow state at the time of the debug call.
    /// </summary>
    public string ControlFlowState { get; }

    /// <summary>
    /// Initializes a new instance of the DebugMessage class.
    /// </summary>
    /// <param name="variables">Dictionary of variable names to their values</param>
    /// <param name="controlFlowState">The current control flow state</param>
    internal DebugMessage(Dictionary<string, object?> variables, string controlFlowState)
    {
        Variables = variables;
        ControlFlowState = controlFlowState;
    }
}
