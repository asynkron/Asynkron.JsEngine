using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests. Always uses fast paths ().
/// </summary>
public class InternalTestBase(ITestOutputHelper output)
{
    protected readonly ITestOutputHelper Output = output;

    /// <summary>
    /// Creates a JsEngine with fast paths enabled.
    /// </summary>
    protected JsEngine CreateEngine()
    {
        return new JsEngine(new JsEngineOptions { });
    }

    protected (JsEngine, TestLogger) CreateEngineWithTestLogger()
    {
        var logger = new TestLogger(output);
        var engine = new JsEngine(new JsEngineOptions { Logger = logger });
        return (engine, logger);
    }

    /// <summary>
    /// Creates a JsEngine with the provided options factory.
    /// </summary>
    protected JsEngine CreateEngineWithOptions(Func<bool, JsEngineOptions> optionsFactory)
    {
        return new JsEngine(optionsFactory(true));
    }
}
