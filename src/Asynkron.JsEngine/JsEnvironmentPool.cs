#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Parser;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Pool for JsEnvironment instances to reduce per-iteration allocations in hot loops.
/// </summary>
internal static class JsEnvironmentPool
{
    private static readonly ObjectPool<JsEnvironment> Pool = new(32,
        static () => new JsEnvironment(null, false, false));

    // Cached delegate for common case (no logger) - avoids closure allocation
    private static readonly Action<JsEnvironment> ReturnWithoutLogger = static e => Return(e, null);

    /// <summary>
    /// Rents a pooled environment wrapped in a disposable handle.
    /// Use with 'using' for automatic return: using var scope = JsEnvironmentPool.Rent(...);
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Pooled<JsEnvironment> Rent(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false,
        ILogger? logger = null)
    {
        var env = Pool.Rent(logger);
        env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description,
            isParameterEnvironment, isBodyEnvironment);
        if (PoolGuard.Enabled)
        {
            env.MarkLeased(PoolGuard.NextLeaseId());
        }

        // Use cached delegate when no logger to avoid closure allocation
        var returnAction = logger is null
            ? ReturnWithoutLogger
            : e => Return(e, logger);

        return new Pooled<JsEnvironment>(env, returnAction);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsEnvironment environment, ILogger? logger = null)
    {
        // Captured environments cannot be pooled - they're held by closures
        if (environment.IsCaptured)
        {
            logger?.LogDebug("JsEnvironment.Return skipped - environment is captured");
            return;
        }

        environment.MarkReturned();
        Pool.Return(environment, logger);
    }
}
