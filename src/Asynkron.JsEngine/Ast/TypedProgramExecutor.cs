using System;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Coordinates conversion from S-expressions to the typed AST and decides whether
/// the typed evaluator can handle the program. When unsupported constructs are
/// detected we fall back to the legacy cons-based interpreter to preserve
/// behaviour while we continue expanding the typed runtime.
/// </summary>
internal sealed class TypedProgramExecutor
{
    /// <summary>
    /// Optional callback that receives the analyzer reason when the executor
    /// falls back to the legacy interpreter.
    /// </summary>
    internal Action<string>? UnsupportedCallback { get; set; }

    public object? Evaluate(ParsedProgram program, JsEnvironment environment)
    {
        if (TypedAstSupportAnalyzer.Supports(program.Typed, out var reason))
        {
            return TypedAstEvaluator.EvaluateProgram(program.Typed, environment);
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Typed program contains constructs that are not implemented yet."
            : reason;

        // Surface the reason so hosts can aggregate missing feature usage.
        Console.WriteLine($"[TypedProgramExecutor] Falling back to legacy interpreter: {normalizedReason}");
        UnsupportedCallback?.Invoke(normalizedReason);

        return ProgramEvaluator.Evaluate(program.SExpression, environment);
    }
}
