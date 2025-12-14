using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

/// <summary>
/// Simple pool for JsEnvironment instances to reduce per-iteration allocations in hot loops.
/// Uses a lock-free fixed-size array with Interlocked operations for thread-safety.
/// </summary>
internal static class JsEnvironmentPool
{
    private const int PoolSize = 32;
    private static readonly JsEnvironment?[] Pool = new JsEnvironment?[PoolSize];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsEnvironment Rent(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        // Try to get from pool using lock-free compare-exchange
        for (var i = 0; i < PoolSize; i++)
        {
            var env = Pool[i];
            if (env is not null && Interlocked.CompareExchange(ref Pool[i], null, env) == env)
            {
                env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description,
                    isParameterEnvironment, isBodyEnvironment);
                return env;
            }
        }

        return new JsEnvironment(enclosing, isFunctionScope, isStrict, creatingSource, description, null,
            isParameterEnvironment, isBodyEnvironment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsEnvironment environment)
    {
        // Clear to a neutral state
        environment.Reset(null, false, false);

        // Try to return to pool using lock-free compare-exchange
        for (var i = 0; i < PoolSize; i++)
        {
            if (Pool[i] is null && Interlocked.CompareExchange(ref Pool[i], environment, null) is null)
            {
                return;
            }
        }
        // Pool full, environment will be GC'd
    }
}
