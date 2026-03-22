#region

using System.Collections.Immutable;
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
    private static readonly Dictionary<ExecutionPlanFailureCode, int> FailureCodeCounts = [];
    private static readonly Dictionary<ExpressionProgramFailureCode, int> ExpressionFailureCodeCounts = [];
    private static int TotalAttempts;
    private static int TotalSucceeded;
    private static int TotalFailed;
    private static int TotalFunctionCacheHits;
    private static int TotalScriptAttempts;
    private static int TotalScriptSucceeded;
    private static int TotalScriptFailed;
    private static int TotalScriptCacheHits;
    private static ExecutionPlanFailureCode? _lastFailureCode;
    private static ExpressionProgramFailureCode? _lastExpressionFailureCode;
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

    internal static ExecutionPlanFailureCode? LastFailureCode
    {
        get
        {
            lock (Sync)
            {
                return _lastFailureCode;
            }
        }
    }

    internal static ExpressionProgramFailureCode? LastExpressionFailureCode
    {
        get
        {
            lock (Sync)
            {
                return _lastExpressionFailureCode;
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
            TotalFunctionCacheHits = 0;
            TotalScriptAttempts = 0;
            TotalScriptSucceeded = 0;
            TotalScriptFailed = 0;
            TotalScriptCacheHits = 0;
            FailureCodeCounts.Clear();
            ExpressionFailureCodeCounts.Clear();
            _lastFailureCode = null;
            _lastExpressionFailureCode = null;
            _lastFailureReason = null;
            _lastFunctionDescription = null;
        }
    }

    internal static void ReportResult(FunctionExpression function, ExecutionPlanBuildResult result)
    {
        lock (Sync)
        {
            TotalAttempts++;
            if (result.Succeeded)
            {
                TotalSucceeded++;
            }
            else
            {
                TotalFailed++;
                RecordFailure(result.Failure, DescribeFunction(function));
            }
        }
    }

    internal static void ReportScriptResult(ProgramNode program, ExecutionPlanBuildResult result)
    {
        lock (Sync)
        {
            TotalScriptAttempts++;
            if (result.Succeeded)
            {
                TotalScriptSucceeded++;
            }
            else
            {
                TotalScriptFailed++;
                RecordFailure(result.Failure, DescribeProgram(program));
            }
        }
    }

    internal static void ReportFunctionCacheHit()
    {
        lock (Sync)
        {
            TotalFunctionCacheHits++;
        }
    }

    internal static void ReportScriptCacheHit()
    {
        lock (Sync)
        {
            TotalScriptCacheHits++;
        }
    }

    public static (int Attempts, int Succeeded, int Failed) Snapshot()
    {
        lock (Sync)
        {
            // Legacy snapshot semantics treat cached function plan reads as observed IR plan usage.
            // Use DetailedSnapshot() for truthful build-once accounting.
            return (
                TotalAttempts + TotalFunctionCacheHits,
                TotalSucceeded + TotalFunctionCacheHits,
                TotalFailed);
        }
    }

    internal static ExecutionPlanDiagnosticSnapshot DetailedSnapshot()
    {
        lock (Sync)
        {
            return new ExecutionPlanDiagnosticSnapshot(
                new ExecutionPlanDiagnosticCounters(TotalAttempts, TotalSucceeded, TotalFailed),
                new ExecutionPlanDiagnosticCounters(TotalScriptAttempts, TotalScriptSucceeded, TotalScriptFailed),
                FailureCodeCounts.ToImmutableDictionary(),
                ExpressionFailureCodeCounts.ToImmutableDictionary(),
                _lastFailureCode,
                _lastExpressionFailureCode,
                TotalFunctionCacheHits,
                TotalScriptCacheHits);
        }
    }

    internal static ExecutionPlanDiagnosticSnapshot SnapshotDetailed()
    {
        return DetailedSnapshot();
    }

    private static void RecordFailure(ExecutionPlanBuildFailure? failure, string description)
    {
        var failureCode = failure?.Code ?? ExecutionPlanFailureCode.UnsupportedConstruct;
        if (FailureCodeCounts.TryGetValue(failureCode, out var count))
        {
            FailureCodeCounts[failureCode] = count + 1;
        }
        else
        {
            FailureCodeCounts[failureCode] = 1;
        }

        _lastFailureCode = failureCode;
        _lastExpressionFailureCode = failure?.ExpressionFailureCode;
        _lastFailureReason = failure?.Detail;
        _lastFunctionDescription = description;

        if (failure?.ExpressionFailureCode is { } expressionFailureCode)
        {
            if (ExpressionFailureCodeCounts.TryGetValue(expressionFailureCode, out var expressionCount))
            {
                ExpressionFailureCodeCounts[expressionFailureCode] = expressionCount + 1;
            }
            else
            {
                ExpressionFailureCodeCounts[expressionFailureCode] = 1;
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

    private static string DescribeProgram(ProgramNode program)
    {
        return program.Source?.ToString() ?? "<script>";
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
