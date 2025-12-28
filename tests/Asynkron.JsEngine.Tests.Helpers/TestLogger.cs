using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

// Minimal stand-in for Microsoft.Extensions.Logging.Testing.FakeLogger so we can
// assert on captured log messages without pulling an extra package.
namespace Asynkron.JsEngine.Tests.Helpers;

/// <summary>
/// ILogger implementation that captures logs and optionally writes to xUnit output.
/// Optionally throws if too many log entries are recorded (to detect infinite loops).
/// </summary>
public sealed class TestLogger(
    ITestOutputHelper? xUnitOutput = null,
    string name = "RealmLogger",
    int maxLogCount = 0,
    LogLevel minLogLevel = LogLevel.Information)
    : ILogger
{
    private readonly Lock _lock = new();
    private int _logCount;

    public LogCollector Collector { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minLogLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        lock (_lock)
        {
            _logCount++;
            if (maxLogCount > 0 && _logCount > maxLogCount)
            {
                throw new InvalidOperationException(
                    $"TestLogger exceeded max log count ({maxLogCount}). Likely infinite loop detected. Last message: {message}");
            }

            // Always collect all logs for test inspection
            Collector.Add(new LogRecord(logLevel, eventId, exception, message));

            // Only output to console/xUnit if log level meets minimum threshold
            if (logLevel < minLogLevel)
            {
                return;
            }

            var formattedMessage = $"[{name}] {logLevel}: {message}";

            Console.WriteLine(formattedMessage);
            switch (_logCount)
            {
                case < 100:
                    xUnitOutput?.WriteLine(message);
                    break;
                case 101:
                    xUnitOutput?.WriteLine("... (more than 100 log messages, suppressing further output)");
                    break;
            }
        }
    }

    public sealed record LogRecord(LogLevel Level, EventId EventId, Exception? Exception, string Message);

    public sealed class LogCollector
    {
        private readonly ConcurrentQueue<LogRecord> _records = new();

        public void Add(LogRecord record) => _records.Enqueue(record);

        public LogRecord? LatestRecord => _records.LastOrDefault();

        public LogRecord[] Snapshot() => _records.ToArray();
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
