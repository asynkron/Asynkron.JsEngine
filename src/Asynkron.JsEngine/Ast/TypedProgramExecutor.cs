using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Coordinates conversion from S-expressions to the typed AST and feeds the
///     result to the typed evaluator. Execution no longer falls back to the legacy
///     cons interpreter; cons cells are retained solely for parsing and
///     transformation steps before the AST builder runs.
/// </summary>
internal sealed class TypedProgramExecutor
{
    public static object? Evaluate(
        ParsedProgram program,
        JsEnvironment environment,
        RealmState realmState,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script)
    {
        return program.Typed.EvaluateProgram(environment,
            realmState,
            cancellationToken,
            executionKind);
    }
}
