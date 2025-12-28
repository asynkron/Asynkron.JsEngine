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
public abstract class JsEngineTestBase(JsEngineTestFixture fixture)
{
    private JsEngineTestFixture Fixture { get; } = fixture;

    /// <summary>
    /// Creates a new JsEngine with EnableFastPaths controlled by the test fixture.
    /// </summary>
    protected JsEngine CreateEngine() => Fixture.CreateEngine();
}
