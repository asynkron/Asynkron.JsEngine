using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Lightweight diagnostics for generator IR lowering. Exposed primarily for tests so we can
/// assert that specific generator bodies successfully produce IR plans instead of falling back
/// to the replay engine.
/// </summary>
public static class GeneratorIrDiagnostics
{
    private static readonly object Sync = new();

    private static int _totalAttempts;
    private static int _totalSucceeded;
    private static int _totalFailed;
    private static string? _lastFailureReason;
    private static string? _lastFunctionDescription;

    public static void Reset()
    {
        lock (Sync)
        {
            _totalAttempts = 0;
            _totalSucceeded = 0;
            _totalFailed = 0;
            _lastFailureReason = null;
            _lastFunctionDescription = null;
        }
    }

    internal static void ReportResult(FunctionExpression function, bool succeeded, string? failureReason)
    {
        lock (Sync)
        {
            _totalAttempts++;
            if (succeeded)
            {
                _totalSucceeded++;
            }
            else
            {
                _totalFailed++;
                _lastFailureReason = failureReason;
                _lastFunctionDescription = DescribeFunction(function);
            }
        }
    }

    public static (int Attempts, int Succeeded, int Failed) Snapshot()
    {
        lock (Sync)
        {
            return (_totalAttempts, _totalSucceeded, _totalFailed);
        }
    }

    public static string? LastFailureReason
    {
        get
        {
            lock (Sync)
            {
                return _lastFailureReason;
            }
        }
    }

    public static string? LastFunctionDescription
    {
        get
        {
            lock (Sync)
            {
                return _lastFunctionDescription;
            }
        }
    }

    private static string DescribeFunction(FunctionExpression function)
    {
        if (function.Name is { } name)
        {
            return name.Name;
        }

        return function.Source?.ToString() ?? "<anonymous>";
    }
}
