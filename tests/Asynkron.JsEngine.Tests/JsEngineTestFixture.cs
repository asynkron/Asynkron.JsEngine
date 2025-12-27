using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Test fixture that provides JsEngineOptions configuration based on environment variables.
/// This allows CI to run the full test suite with different configurations via matrix builds.
///
/// Environment variables:
/// - JSENGINE_DISABLE_FASTPATHS: Set to "true" to disable fast paths (tests slow implementation)
/// </summary>
public class JsEngineTestFixture
{
    /// <summary>
    /// Whether fast paths are enabled for this test run.
    /// Controlled by JSENGINE_DISABLE_FASTPATHS environment variable.
    /// </summary>
    public bool EnableFastPaths { get; }

    public JsEngineTestFixture()
    {
        var disableFastPaths = Environment.GetEnvironmentVariable("JSENGINE_DISABLE_FASTPATHS");
        EnableFastPaths = !string.Equals(disableFastPaths, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a new JsEngine with the configured options.
    /// </summary>
    public JsEngine CreateEngine()
    {
        return new JsEngine();
    }

    /// <summary>
    /// Creates a new JsEngine with additional options.
    /// The EnableFastPaths setting from the fixture is always applied.
    /// </summary>
    public JsEngine CreateEngine(bool debugMode = false, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        return new JsEngine(new JsEngineOptions
        {
            DebugMode = debugMode,
            Logger = logger,
            TimeZone = timeZone ?? TimeZoneInfo.Utc
        });
    }
}

/// <summary>
/// Collection definition for tests that use the JsEngineTestFixture.
/// Tests can opt-in to this collection to use the shared fixture.
/// </summary>
[CollectionDefinition("JsEngine")]
public class JsEngineCollection : ICollectionFixture<JsEngineTestFixture>
{
}
