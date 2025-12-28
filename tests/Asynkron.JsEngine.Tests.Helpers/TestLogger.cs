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
public sealed class TestLogger : ILogger
{
    private readonly ITestOutputHelper? _xUnitOutput;
    private readonly string _name;
    private readonly int _maxLogCount;
    private readonly LogLevel _minLogLevel;
    private readonly object _lock = new();
    private int _logCount;

    public TestLogger(
        ITestOutputHelper? xUnitOutput = null,
        string name = "RealmLogger",
        int maxLogCount = 0,
        LogLevel minLogLevel = LogLevel.Information)
    {
        _xUnitOutput = xUnitOutput;
        _name = name;
        _maxLogCount = maxLogCount;
        _minLogLevel = minLogLevel;
    }

    public LogCollector Collector { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLogLevel;

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
                    $"TestLogger exceeded max log count ({_maxLogCount}). Likely infinite loop detected. Last message: {message}");
            }

            // Always collect all logs for test inspection
            Collector.Add(new LogRecord(logLevel, eventId, exception, message));

            // Only output to console/xUnit if log level meets minimum threshold
            if (logLevel >= _minLogLevel)
            {
                var formattedMessage = $"[{_name}] {logLevel}: {message}";
                Console.WriteLine(formattedMessage);
                _xUnitOutput?.WriteLine(formattedMessage);
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
