using System.Threading;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Coordinates conversion from S-expressions to the typed AST and feeds the
/// result to the typed evaluator. Execution no longer falls back to the legacy
/// cons interpreter; cons cells are retained solely for parsing and
/// transformation steps before the AST builder runs.
/// </summary>
internal sealed class TypedProgramExecutor
{
    public object? Evaluate(ParsedProgram program, JsEnvironment environment, CancellationToken cancellationToken = default)
    {
        return TypedAstEvaluator.EvaluateProgram(program.Typed, environment, cancellationToken);
    }
}
