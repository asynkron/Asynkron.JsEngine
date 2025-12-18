using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// ILogger implementation that writes realm logs to xUnit output.
/// </summary>
public sealed class XunitLogger(ITestOutputHelper outputHelper, string name = "RealmLogger") : ILogger
{
    private readonly ITestOutputHelper _outputHelper = outputHelper ?? throw new ArgumentNullException(nameof(outputHelper));
    private readonly string _name = name;
    private readonly object _lock = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_lock)
        {
            _outputHelper.WriteLine($"[{_name}] {logLevel}: {message}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
