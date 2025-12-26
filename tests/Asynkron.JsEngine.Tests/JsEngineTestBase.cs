using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests that want to use the JsEngineTestFixture.
/// Inheriting from this class automatically opts the test class into the "JsEngine" collection,
/// which runs tests with EnableFastPaths controlled by the JSENGINE_DISABLE_FASTPATHS env var.
///
/// Usage:
/// 1. Inherit from this class instead of using [Fact] directly
/// 2. Use CreateEngine() instead of new JsEngine()
/// 3. In CI, set JSENGINE_DISABLE_FASTPATHS=true to test slow paths
/// </summary>
[Collection("JsEngine")]
public abstract class JsEngineTestBase
{
    protected JsEngineTestFixture Fixture { get; }

    protected JsEngineTestBase(JsEngineTestFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Creates a new JsEngine with EnableFastPaths controlled by the test fixture.
    /// </summary>
    protected JsEngine CreateEngine() => Fixture.CreateEngine();

    /// <summary>
    /// Creates a new JsEngine with additional options.
    /// EnableFastPaths is always controlled by the test fixture.
    /// </summary>
    protected JsEngine CreateEngine(bool debugMode = false, ILogger? logger = null, TimeZoneInfo? timeZone = null)
        => Fixture.CreateEngine(debugMode, logger, timeZone);
}
