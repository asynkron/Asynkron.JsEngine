using Asynkron.JsEngine;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// Shared helpers for constructing configured JsEngine instances in tests.
/// </summary>
public static class TestEngineFactory
{
    /// <summary>
    /// Creates a JsEngine with debug mode enabled. If JSENGINE_TRACE_REALM is set, attaches a console logger.
    /// </summary>
    public static JsEngine CreateDebugEngine(string? loggerName = null, ILogger? logger = null)
    {
        var options = new JsEngineOptions
        {
            DebugMode = true,
        };
        var engine = new JsEngine(options);
        AttachRealmLoggerIfEnabled(engine, loggerName, logger);
        return engine;
    }

    /// <summary>
    /// Attaches a realm logger when the JSENGINE_TRACE_REALM env var is present.
    /// </summary>
    public static void AttachRealmLoggerIfEnabled(JsEngine engine, string? loggerName = null, ILogger? logger = null)
    {
        if (engine.RealmState.Logger is not null)
        {
            return;
        }

        if (logger is not null)
        {
            engine.RealmState.Logger = logger;
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JSENGINE_TRACE_REALM")))
        {
            engine.RealmState.Logger = new ConsoleLogger(loggerName ?? "RealmLogger");
        }
    }
}
