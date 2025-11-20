using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Pre-pass for generator functions that can normalize complex <c>yield</c> placements
/// into a generator-friendly AST surface before IR is built. For now this acts as a
/// no-op scaffold so that future yield-lowering logic can live in a single, testable
/// place instead of being interleaved with IR code generation.
/// </summary>
internal static class GeneratorYieldLowerer
{
    public static bool TryLowerToGeneratorFriendlyAst(
        FunctionExpression function,
        out FunctionExpression lowered,
        out string? failureReason)
    {
        lowered = function;
        failureReason = null;
        return true;
    }
}

