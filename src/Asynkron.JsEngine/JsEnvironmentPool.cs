using System.Collections.Concurrent;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

/// <summary>
/// Simple pool for JsEnvironment instances to reduce per-iteration allocations in hot loops.
/// </summary>
internal static class JsEnvironmentPool
{
    private static readonly ConcurrentBag<JsEnvironment> Pool = new();

    public static JsEnvironment Rent(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        if (Pool.TryTake(out var env))
        {
            env.Reset(enclosing, isFunctionScope, isStrict, creatingSource, description, isParameterEnvironment,
                isBodyEnvironment);
            return env;
        }

        return new JsEnvironment(enclosing, isFunctionScope, isStrict, creatingSource, description, null,
            isParameterEnvironment, isBodyEnvironment);
    }

    public static void Return(JsEnvironment environment)
    {
        // Clear to a neutral state; Realm/ModulePath will be re-set on next rent.
        environment.Reset(null, false, false);
        Pool.Add(environment);
    }
}
