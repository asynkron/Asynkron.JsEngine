namespace Asynkron.JsEngine;

/// <summary>
/// Interface for callables that need access to the calling environment for debug/introspection purposes.
/// </summary>
internal interface IEnvironmentAwareCallable : IJsCallable
{
    /// <summary>
    /// Sets the calling environment before invoking the function.
    /// This is used to build proper call stacks for debugging.
    /// </summary>
    Environment? CallingEnvironment { get; set; }
}
