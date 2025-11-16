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
        var typedProgram = _builder.BuildProgram(program);

        //TODO: everything works if this is uncommented, but our goal is to make everything run using the TypedAstEvaluator now.
        // if (!TypedAstSupportAnalyzer.Supports(typedProgram, out _))
        // {
        //     return ProgramEvaluator.EvaluateProgram(program, environment);
        // }
        return TypedAstEvaluator.EvaluateProgram(typedProgram, environment);
    }
}
