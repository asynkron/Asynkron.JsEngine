#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
/// Pool for JsEnvironment instances to reduce per-iteration allocations in hot loops.
/// </summary>
internal static class JsEnvironmentPool
{
    private static readonly ObjectPool<JsEnvironment> Pool = new(32,
        static () => new JsEnvironment(null, false, false));

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
        var env = Pool.Rent();
        env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description,
            isParameterEnvironment, isBodyEnvironment);
        return env;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JsEnvironment environment) => Pool.Return(environment);
}
