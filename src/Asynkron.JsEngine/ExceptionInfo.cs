namespace Asynkron.JsEngine;

/// <summary>
///     Represents exception information captured during JavaScript execution.
///     Contains details about exceptions that occurred, including context and call stack.
/// </summary>
public sealed class ExceptionInfo
{
    /// <summary>
    ///     Initializes a new instance of the ExceptionInfo class.
    /// </summary>
    /// <param name="exception">The exception that was thrown</param>
    /// <param name="context">The context in which the exception occurred</param>
    /// <param name="callStack">The JavaScript call stack</param>
    internal ExceptionInfo(Exception exception, string context, IReadOnlyList<CallStackFrame> callStack)
    {
        Exception = exception;
        Context = context;
        CallStack = callStack;
    }

    /// <summary>
    ///     Gets the exception that was thrown.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    ///     Gets the context in which the exception occurred (e.g., "function invocation", "property access").
    /// </summary>
    public string Context { get; }

    /// <summary>
    ///     Gets the JavaScript call stack at the time of the exception.
    /// </summary>
    public IReadOnlyList<CallStackFrame> CallStack { get; }

    /// <summary>
    ///     Gets the exception message.
    /// </summary>
    public string Message => Exception.Message;

    /// <summary>
    ///     Gets the exception type.
    /// </summary>
    public string ExceptionType => Exception.GetType().Name;
}
