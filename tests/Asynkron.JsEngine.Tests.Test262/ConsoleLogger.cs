using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests.Test262;

internal sealed class ConsoleLogger(string name = "RealmLogger") : ILogger
{
    private readonly string _name = name;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Console.Error.WriteLine($"[{_name}] {logLevel}: {message}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
