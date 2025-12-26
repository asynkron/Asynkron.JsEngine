using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests that should run in both FastPath and Reference modes.
/// Inherit from this and implement the abstract EnableFastPaths property.
/// </summary>
public abstract class FastPathTestBase(ITestOutputHelper output)
{
    protected readonly ITestOutputHelper Output = output;

    /// <summary>
    /// When true, enables optimized fast paths. When false, uses reference implementation.
    /// </summary>
    protected abstract bool EnableFastPaths { get; }

    /// <summary>
    /// Creates a JsEngine with the appropriate EnableFastPaths setting.
    /// </summary>
    protected JsEngine CreateEngine()
    {
        return new JsEngine(new JsEngineOptions { EnableFastPaths = EnableFastPaths });
    }

    /// <summary>
    /// Creates a JsEngine with custom options, but always sets EnableFastPaths from the test mode.
    /// </summary>
    protected JsEngine CreateEngine(Action<JsEngineOptions> configure)
    {
        var options = new JsEngineOptions { EnableFastPaths = EnableFastPaths };
        configure(options);
        return new JsEngine(options);
    }

    /// <summary>
    /// Creates a JsEngine with the provided options factory. Use this for options with init-only properties.
    /// The factory receives EnableFastPaths and should include it in the options.
    /// </summary>
    protected JsEngine CreateEngineWithOptions(Func<bool, JsEngineOptions> optionsFactory)
    {
        return new JsEngine(optionsFactory(EnableFastPaths));
    }
}
