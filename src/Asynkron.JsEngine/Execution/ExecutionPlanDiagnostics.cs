#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Lightweight diagnostics for execution plan building. Exposed primarily for tests so we can
///     assert that specific function bodies successfully produce execution plans instead of falling back
///     to the replay engine.
/// </summary>
public static class ExecutionPlanDiagnostics
{
    private static readonly Lock Sync = new();

    private static int TotalAttempts;
    private static int TotalSucceeded;
    private static int TotalFailed;
    private static string? _lastFailureReason;
    private static string? _lastFunctionDescription;

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

    public static void Reset()
    {
        lock (Sync)
        {
            TotalAttempts = 0;
            TotalSucceeded = 0;
            TotalFailed = 0;
            _lastFailureReason = null;
            _lastFunctionDescription = null;
        }
    }

    internal static void ReportResult(FunctionExpression function, bool succeeded, string? failureReason)
    {
        lock (Sync)
        {
            TotalAttempts++;
            if (succeeded)
            {
                TotalSucceeded++;
            }
            else
            {
                TotalFailed++;
                _lastFailureReason = failureReason;
                _lastFunctionDescription = DescribeFunction(function);
            }
        }
    }

    public static (int Attempts, int Succeeded, int Failed) Snapshot()
    {
        lock (Sync)
        {
            return (TotalAttempts, TotalSucceeded, TotalFailed);
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

    /// <summary>
    /// Pretty prints the execution plan for a function. Returns null if no plan is available.
    /// </summary>
    public static string? PrintPlan(FunctionExpression function)
    {
        var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
        if (cache.Plan is null)
        {
            return $"No execution plan available. Reason: {cache.FailureReason ?? "unknown"}";
        }

        return ExecutionPlanPrinter.Print(cache.Plan.Instructions, cache.Plan.EntryPoint);
    }

    /// <summary>
    /// Pretty prints a single instruction.
    /// </summary>
    internal static string FormatInstruction(ExecutionInstruction instruction)
    {
        return ExecutionPlanPrinter.FormatInstruction(instruction);
    }
}
