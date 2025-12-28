using Asynkron.JsEngine;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// Shared helpers for constructing configured JsEngine instances in tests.
/// </summary>
public static class TestEngineFactory
{
    /// <summary>
    /// Creates a JsEngine with debug mode enabled and the specified logger attached.
    /// </summary>
    public static JsEngine CreateDebugEngine(ILogger logger)
    {
        var options = new JsEngineOptions
        {
            DebugMode = true,
        };
        var engine = new JsEngine(options);
        engine.RealmState.Logger = logger;
        return engine;
    }

    /// <summary>
    /// Creates a JsEngine with debug mode enabled. If JSENGINE_TRACE_REALM is set, attaches a console logger.
    /// </summary>
    public static JsEngine CreateDebugEngine(string? loggerName = null)
    {
        var options = new JsEngineOptions
        {
            DebugMode = true,
        };
        var engine = new JsEngine(options);
        AttachRealmLoggerIfEnvVarSet(engine, loggerName);
        return engine;
    }

    /// <summary>
    /// Attaches a realm logger when the JSENGINE_TRACE_REALM env var is present.
    /// </summary>
    public static void AttachRealmLoggerIfEnvVarSet(JsEngine engine, string? loggerName = null)
    {
        if (engine.RealmState.Logger is not null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JSENGINE_TRACE_REALM")))
        {
            engine.RealmState.Logger = new TestLogger(name: loggerName ?? "RealmLogger");
        }
    }
}
