namespace Asynkron.JsEngine;

/// <summary>
///     Represents a debug message captured during JavaScript execution.
///     Contains information about the execution context and environment at the time __debug() was called.
/// </summary>
public sealed class DebugMessage
{
    /// <summary>
    ///     Initializes a new instance of the DebugMessage class.
    /// </summary>
    /// <param name="variables">Dictionary of variable names to their values</param>
    /// <param name="controlFlowState">The current control flow state</param>
    /// <param name="callStack">The call stack at the time of capture</param>
    /// <param name="environmentChain">The environment chain with scope information</param>
    internal DebugMessage(Dictionary<string, object?> variables, string controlFlowState,
        List<CallStackFrame> callStack, List<EnvironmentInfo> environmentChain)
    {
        Variables = variables;
        ControlFlowState = controlFlowState;
        CallStack = callStack;
        EnvironmentChain = environmentChain;
    }

    /// <summary>
    ///     Gets a dictionary of all variables and their values in the current scope and parent scopes.
    ///     The key is the variable name, and the value is the current value of that variable.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; }

    /// <summary>
    ///     Gets the control flow state at the time of the debug call.
    /// </summary>
    public string ControlFlowState { get; }

    /// <summary>
    ///     Gets the call stack at the time of the debug call.
    ///     The list is ordered from innermost frame (most recent) to outermost frame (oldest).
    /// </summary>
    public IReadOnlyList<CallStackFrame> CallStack { get; }

    /// <summary>
    ///     Gets the environment chain showing scope hierarchy with ScopeIds and variable storage.
    ///     The list is ordered from innermost scope (current) to outermost scope (global).
    /// </summary>
    public IReadOnlyList<EnvironmentInfo> EnvironmentChain { get; }
}

/// <summary>
///     Information about a single environment in the scope chain.
/// </summary>
public sealed class EnvironmentInfo
{
    internal EnvironmentInfo(int scopeId, bool hasSlots, int slotCount,
        Dictionary<string, object?> dictionaryVariables, Dictionary<int, object?> slotVariables,
        string? description)
    {
        ScopeId = scopeId;
        HasSlots = hasSlots;
        SlotCount = slotCount;
        DictionaryVariables = dictionaryVariables;
        SlotVariables = slotVariables;
        Description = description;
    }

    /// <summary>
    ///     The ScopeId of this environment (-1 if not set).
    /// </summary>
    public int ScopeId { get; }

    /// <summary>
    ///     Whether this environment has slots allocated.
    /// </summary>
    public bool HasSlots { get; }

    /// <summary>
    ///     Number of slots in this environment (0 if no slots).
    /// </summary>
    public int SlotCount { get; }

    /// <summary>
    ///     Variables stored in the dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, object?> DictionaryVariables { get; }

    /// <summary>
    ///     Variables stored in slots (slot index -> value).
    /// </summary>
    public IReadOnlyDictionary<int, object?> SlotVariables { get; }

    /// <summary>
    ///     Optional description of this environment.
    /// </summary>
    public string? Description { get; }
}
