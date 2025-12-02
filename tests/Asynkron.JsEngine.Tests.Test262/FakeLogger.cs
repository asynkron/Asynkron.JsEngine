using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

// Minimal stand-in for Microsoft.Extensions.Logging.Testing.FakeLogger so we can
// assert on captured log messages without pulling an extra package.
namespace Microsoft.Extensions.Logging.Testing;

public sealed class FakeLogger : ILogger
{
    public LogCollector Collector { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Collector.Add(new LogRecord(logLevel, eventId, exception, message));
    }

    public sealed record LogRecord(LogLevel Level, EventId EventId, Exception? Exception, string Message);

    public sealed class LogCollector
    {
        private readonly ConcurrentQueue<LogRecord> _records = new();

        public void Add(LogRecord record) => _records.Enqueue(record);

        public LogRecord? LatestRecord => _records.LastOrDefault();
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
