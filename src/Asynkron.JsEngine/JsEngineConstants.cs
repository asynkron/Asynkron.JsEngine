namespace Asynkron.JsEngine;

/// <summary>
/// Global constants used throughout the JavaScript engine.
/// </summary>
public static class JsEngineConstants
{
    /// <summary>
    /// Maximum depth for prototype chain traversal to prevent infinite loops.
    /// Per ECMAScript specification, typical prototype chains are less than 10 deep.
    /// This limit ensures reasonable performance while preventing stack overflow
    /// from circular prototype references.
    /// </summary>
    public const int MaxPrototypeChainDepth = 100;

    /// <summary>
    /// Enable detailed IR execution tracing with environment depth indentation.
    /// WARNING: Very verbose output - only enable for debugging specific issues.
    /// </summary>
    public const bool TraceIrExecution = true;

    /// <summary>
    /// Disable all object pooling. When true:
    /// - Pool.Rent() always creates fresh instances
    /// - Pool.Return() is a no-op (objects go to GC)
    /// Use this to establish a clean baseline - all tests should pass with pooling disabled.
    /// </summary>
    public const bool DisablePooling = false;
}
