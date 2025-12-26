using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Base class for tests that should run in both FastPath and Reference modes.
/// Inherit from this and implement the abstract EnableFastPaths property.
/// </summary>
public abstract class FastPathTestBase
{
    protected readonly ITestOutputHelper Output;

    protected FastPathTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

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
}

/// <summary>
/// Marker interface to identify test classes that run with fast paths enabled.
/// </summary>
public abstract class FastPathTests : FastPathTestBase
{
    protected FastPathTests(ITestOutputHelper output) : base(output) { }
    protected sealed override bool EnableFastPaths => true;
}

/// <summary>
/// Marker interface to identify test classes that run with fast paths disabled (reference implementation).
/// </summary>
public abstract class ReferenceTests : FastPathTestBase
{
    protected ReferenceTests(ITestOutputHelper output) : base(output) { }
    protected sealed override bool EnableFastPaths => false;
}
