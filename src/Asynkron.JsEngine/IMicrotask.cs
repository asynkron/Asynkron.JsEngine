namespace Asynkron.JsEngine;

/// <summary>
///     Represents a microtask that can be queued for execution.
///     Implementers are typically pooled objects that execute promise reactions
///     or other deferred work.
/// </summary>
internal interface IMicrotask
{
    /// <summary>
    ///     The epoch in which this microtask was queued.
    ///     Used for selective draining during module loading.
    /// </summary>
    int Epoch { get; set; }

    /// <summary>
    ///     Executes the microtask. Implementations should handle their own
    ///     exception handling and return themselves to their pool after execution.
    /// </summary>
    void Execute();
}
