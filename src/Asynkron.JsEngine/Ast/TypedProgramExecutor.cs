using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Coordinates conversion from S-expressions to the typed AST and decides whether
/// the typed evaluator can handle the program. When unsupported constructs are
/// detected we fall back to the legacy cons-based interpreter to preserve
/// behaviour while we continue expanding the typed runtime.
/// </summary>
internal sealed class TypedProgramExecutor
{
    private readonly SExpressionAstBuilder _builder = new();

    public object? Evaluate(Cons program, JsEnvironment environment)
    {
        ProgramNode typedProgram;
        try
        {
            typedProgram = _builder.BuildProgram(program);
        }
        catch (Exception ex) when (IsRecoverableBuildFailure(ex))
        {
            // The typed AST builder currently lacks coverage for every construct that the
            // legacy cons-based interpreter can execute. Rather than crash the engine
            // whenever a transformation produces an unexpected S-expression (for example
            // CPS lowering of async functions with complex object literals), we fall back
            // to the battle-tested JsEvaluator. This keeps the public behaviour aligned
            // with the legacy runtime while we continue expanding typed support.
            return JsEvaluator.EvaluateProgram(program, environment);
        }

        if (!TypedAstSupportAnalyzer.Supports(typedProgram, out _))
        {
            return JsEvaluator.EvaluateProgram(program, environment);
        }

        return TypedAstEvaluator.EvaluateProgram(typedProgram, environment);
    }

    private static bool IsRecoverableBuildFailure(Exception ex)
    {
        // Only swallow exceptions that indicate the builder could not translate the
        // source S-expression. Anything else should bubble so we do not hide bugs that
        // would also impact the legacy interpreter.
        return ex is InvalidOperationException or ArgumentException;
    }
}
