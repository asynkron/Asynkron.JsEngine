using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// ILogger implementation that writes realm logs to xUnit output.
/// Optionally throws if too many log entries are recorded (to detect infinite loops).
/// </summary>
public sealed class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly string _name;
    private readonly int _maxLogCount;
    private readonly object _lock = new();
    private int _logCount;

    public XunitLogger(ITestOutputHelper outputHelper, string name = "RealmLogger", int maxLogCount = 0)
    {
        _outputHelper = outputHelper ?? throw new ArgumentNullException(nameof(outputHelper));
        _name = name;
        _maxLogCount = maxLogCount;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_lock)
        {
            _logCount++;
            if (_maxLogCount > 0 && _logCount > _maxLogCount)
            {
                throw new InvalidOperationException(
                    $"XunitLogger exceeded max log count ({_maxLogCount}). Likely infinite loop detected. Last message: {message}");
            }

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
