using Asynkron.JsEngine.Tests.Helpers;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests. Always uses fast paths ().
/// </summary>
public class InternalTestBase(ITestOutputHelper output)
{
    protected readonly ITestOutputHelper Output = output;

    /// <summary>
    /// The logger attached to the most recently created engine via CreateEngine().
    /// </summary>
    protected TestLogger? CurrentLogger { get; private set; }

    /// <summary>
    /// Creates a JsEngine with a TestLogger attached.
    /// The logger is accessible via the CurrentLogger property.
    /// </summary>
    protected JsEngine CreateEngine()
    {
        CurrentLogger = new TestLogger(output, maxLogCount: 2000);
        return new JsEngine(new JsEngineOptions { Logger = CurrentLogger });
    }

    /// <summary>
    /// Creates a JsEngine with the provided options factory.
    /// Note: This does not attach a TestLogger - use for custom configurations only.
    /// </summary>
    protected JsEngine CreateEngine(Func<JsEngineOptions> optionsFactory)
    {
        return new JsEngine(optionsFactory());
    }
}
