using Asynkron.JsEngine.Evaluation;
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

    public ProgramNode BuildProgram(Cons program)
    {
        return _builder.BuildProgram(program);
    }

    public object? Evaluate(Cons program, JsEnvironment environment)
    {
        var typedProgram = BuildProgram(program);
        return Evaluate(typedProgram, program, environment);
    }

    public object? Evaluate(ProgramNode typedProgram, Cons originalProgram, JsEnvironment environment)
    {
        if (!TypedAstSupportAnalyzer.Supports(typedProgram, out _))
        {
            return ProgramEvaluator.EvaluateProgram(originalProgram, environment);
        }

        return TypedAstEvaluator.EvaluateProgram(typedProgram, environment);
    }
}
