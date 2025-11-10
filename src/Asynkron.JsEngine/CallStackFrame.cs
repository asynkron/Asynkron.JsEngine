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
    /// Gets the parent frame that created this frame, forming a linked list of execution context.
    /// Null for the root frame.
    /// </summary>
    public CallStackFrame? Parent { get; }

    /// <summary>
    /// Initializes a new instance of the CallStackFrame class.
    /// </summary>
    /// <param name="operationType">The type of operation (e.g., "call", "function", "for", "while")</param>
    /// <param name="description">Human-readable description of what's happening</param>
    /// <param name="expression">The S-expression being evaluated</param>
    /// <param name="parent">The parent frame, if any</param>
    internal CallStackFrame(string operationType, string description, Cons? expression = null, CallStackFrame? parent = null)
    {
        OperationType = operationType;
        Description = description;
        Expression = expression;
        Parent = parent;
    }

    /// <summary>
    /// Gets the depth of this frame in the call stack (0 for root).
    /// </summary>
    public int Depth
    {
        get
        {
            int depth = 0;
            var current = Parent;
            while (current != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }
    }

    /// <summary>
    /// Converts the call stack to a list, from innermost (this frame) to outermost (root).
    /// </summary>
    public List<CallStackFrame> ToList()
    {
        var frames = new List<CallStackFrame>();
        CallStackFrame? current = this;
        while (current != null)
        {
            frames.Add(current);
            current = current.Parent;
        }
        return frames;
    }

    /// <summary>
    /// Returns a string representation of this frame.
    /// </summary>
    public override string ToString()
    {
        return $"[{OperationType}] {Description}";
    }
}
