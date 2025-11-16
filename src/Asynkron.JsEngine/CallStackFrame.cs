using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine;

/// <summary>
/// Represents a single frame in the execution call stack.
/// Tracks the S-expression being evaluated and provides context for debugging.
/// </summary>
public sealed class CallStackFrame
{
    /// <summary>
    /// Gets the type of operation being performed (e.g., "call", "function", "block", etc.).
    /// </summary>
    public string OperationType { get; }

    /// <summary>
    /// Gets a human-readable description of this stack frame.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the S-expression being evaluated, if available.
    /// This is the actual code structure being executed.
    /// </summary>
    public Cons? Expression { get; }

    /// <summary>
    /// Gets the depth of this frame in the call stack (0 for outermost/root).
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Initializes a new instance of the CallStackFrame class.
    /// </summary>
    /// <param name="operationType">The type of operation (e.g., "call", "function", "for", "while")</param>
    /// <param name="description">Human-readable description of what's happening</param>
    /// <param name="expression">The S-expression being evaluated</param>
    /// <param name="depth">The depth in the call stack</param>
    internal CallStackFrame(string operationType, string description, Cons? expression, int depth)
    {
        OperationType = operationType;
        Description = description;
        Expression = expression;
        Depth = depth;
    }

    /// <summary>
    /// Returns a string representation of this frame.
    /// </summary>
    public override string ToString()
    {
        return $"[{OperationType}] {Description}";
    }
}