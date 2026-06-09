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
        return new JsEngine(new JsEngineOptions { Logger = CurrentLogger, DebugMode = true });
    }

    /// <summary>
    /// Creates a JsEngine with the provided options factory.
    /// Note: This does not attach a TestLogger - use for custom configurations only.
    /// </summary>
    protected JsEngine CreateEngine(Func<JsEngineOptions> optionsFactory)
    {
        return new JsEngine(optionsFactory());
    }

    protected static string AssertAsyncFunctionDeclined(
        object? result,
        string functionName,
        string? declineDetail = null)
    {
        var rawMessage = Assert.IsType<string>(result);
        Assert.DoesNotContain("fulfilled:", rawMessage, StringComparison.Ordinal);

        var messageStart = rawMessage.IndexOf("Async-function body '", StringComparison.Ordinal);
        Assert.True(messageStart >= 0, $"Expected async-function decline message, got: {rawMessage}");
        var message = rawMessage[messageStart..];

        Assert.StartsWith(
            $"Async-function body '{functionName}' is not eligible for unified bytecode execution:",
            message,
            StringComparison.Ordinal);
        Assert.Contains(" - ", message, StringComparison.Ordinal);
        if (declineDetail is not null)
        {
            Assert.Contains(declineDetail, message, StringComparison.Ordinal);
        }

        return message;
    }
}
