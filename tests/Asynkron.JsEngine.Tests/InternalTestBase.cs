using Asynkron.JsEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests. Always uses fast paths ().
/// </summary>
public class InternalTestBase
{
    protected readonly ITestOutputHelper Output;

    protected InternalTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

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
        // Default to info-level logs and no cap to avoid test failures from noisy trace/debug output.
        CurrentLogger = new TestLogger(Output, maxLogCount: 0, minLogLevel: LogLevel.Information);
        return new JsEngine(new JsEngineOptions { Logger = CurrentLogger, DebugMode = true});
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
