using Asynkron.JsEngine.Evaluation;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Coordinates conversion from S-expressions to the typed AST and decides whether
/// the typed evaluator can handle the program. When unsupported constructs are
/// detected we fall back to the legacy cons-based interpreter to preserve
/// behaviour while we continue expanding the typed runtime.
/// </summary>
internal sealed class TypedProgramExecutor
{
    public object? Evaluate(ParsedProgram program, JsEnvironment environment)
    {
        return TypedAstEvaluator.EvaluateProgram(program.Typed, environment);
    }
}
