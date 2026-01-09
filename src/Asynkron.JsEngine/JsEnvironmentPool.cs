#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Parser;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Disposable wrapper for a rented JsEnvironment. Use with 'using' for automatic return.
/// </summary>
internal readonly struct RentedEnvironment : IDisposable
{
    public readonly JsEnvironment? Value;
    private readonly ILogger? _logger;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RentedEnvironment(JsEnvironment? value, ILogger? logger)
    {
        Value = value;
        _logger = logger;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Value is not null)
        {
            JsEnvironmentPool.Return(Value, _logger);
        }
    }

    // Allow implicit conversion to JsEnvironment for convenience
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator JsEnvironment?(RentedEnvironment rented) => rented.Value;
}

/// <summary>
/// Pool for JsEnvironment instances to reduce per-iteration allocations in hot loops.
/// Use Rent() to get an environment, Return() to release it back to the pool.
/// Or use RentScoped() with 'using' for automatic return.
/// </summary>
internal static class JsEnvironmentPool
{
    private static readonly ObjectPool<JsEnvironment> Pool = new(32,
        static () => new JsEnvironment(null, false, false));

    /// <summary>
    /// Rents a pooled environment wrapped in a disposable. Use with 'using' for automatic return.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RentedEnvironment RentScoped(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false,
        ILogger? logger = null)
    {
        var env = Rent(enclosing, isFunctionScope, isStrict, creatingSource, description,
            isParameterEnvironment, isBodyEnvironment, logger);
        return new RentedEnvironment(env, logger);
    }

    /// <summary>
    /// Rents a pooled environment. Caller MUST call Return() when done.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsEnvironment Rent(
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
        env.Initialize(enclosing, isFunctionScope, isStrict, creatingSource, description,
            isParameterEnvironment, isBodyEnvironment);
        if (PoolGuard.Enabled)
        {
            env.MarkLeased(PoolGuard.NextLeaseId());
        }

        return env;
    }

    /// <summary>
    /// Returns an environment to the pool. Safe to call on captured or null environments (no-op).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsEnvironment? environment, ILogger? logger = null)
    {
        if (environment is null)
        {
            return;
        }

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
