using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Placeholder IR builder for async generator functions. Async generator IR is
/// not implemented yet, so this builder always reports failure and forces the
/// engine to stay on the replay path.
/// </summary>
internal static class AsyncGeneratorIrBuilder
{
    public static bool TryBuild(FunctionExpression function, out GeneratorPlan plan, out string? failureReason)
    {
        plan = default!;
        failureReason = "Async generator IR not implemented; using replay.";
        return false;
    }
}

