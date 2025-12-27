using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests. Always uses fast paths (EnableFastPaths = true).
/// </summary>
public class FastPathTestBase(ITestOutputHelper output)
{
    protected readonly ITestOutputHelper Output = output;

    /// <summary>
    /// Creates a JsEngine with fast paths enabled.
    /// </summary>
    protected JsEngine CreateEngine()
    {
        return new JsEngine(new JsEngineOptions { EnableFastPaths = true });
    }

    /// <summary>
    /// Creates a JsEngine with custom options and fast paths enabled.
    /// </summary>
    protected JsEngine CreateEngine(Action<JsEngineOptions> configure)
    {
        var options = new JsEngineOptions { EnableFastPaths = true };
        configure(options);
        return new JsEngine(options);
    }

    /// <summary>
    /// Creates a JsEngine with the provided options factory.
    /// </summary>
    protected JsEngine CreateEngineWithOptions(Func<bool, JsEngineOptions> optionsFactory)
    {
        return new JsEngine(optionsFactory(true));
    }
}
