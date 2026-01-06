#region

using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Interface for poolable objects that can reset their state before being returned to the pool.
/// </summary>
internal interface IRentable
{
    /// <summary>
    /// Called when the object is rented from the pool.
    /// Use this to rent sub-objects or initialize transient state.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics during testing.</param>
    void Activate(ILogger? logger = null);

    /// <summary>
    /// Resets the object to a clean state for reuse.
    /// Called automatically when the object is returned to the pool.
    /// Use this to return sub-objects to their pools.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics during testing.</param>
    void Reset(ILogger? logger = null);
}
