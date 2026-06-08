using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;

namespace Asynkron.JsEngine.Ast;

internal sealed class LoweredExpressionProgramCache
{
    private LoweredExpressionProgramCache(
        bool succeeded,
        ExpressionProgram program,
        string? failureReason)
    {
        Succeeded = succeeded;
        Program = program;
        FailureReason = failureReason;
    }

    private bool Succeeded { get; }
    private ExpressionProgram Program { get; }
    private string? FailureReason { get; }

    public static LoweredExpressionProgramCache Build(ExpressionNode? expression)
    {
        if (expression is null)
        {
            return Failure("missing expression");
        }

        return ExpressionProgramCompiler.TryCompile(expression, out var program, out var failureReason)
            ? Success(program)
            : Failure(failureReason ?? "unknown failure");
    }

    public JsValue Execute(
        JsEnvironment environment,
        EvaluationContext context,
        string failureLabel)
    {
        if (!Succeeded)
        {
            throw new NotSupportedException(
                $"{failureLabel} could not be lowered to expression bytecode: {FailureReason}");
        }

        return UnifiedBytecodeExpressionProgramExecutor.ExecuteStandalone(Program, environment, context);
    }

    public static JsValue ExecuteCached(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        string failureLabel)
    {
        return ((IAstCacheable<LoweredExpressionProgramCache>)expression)
            .GetOrCreateCache()
            .Execute(environment, context, failureLabel);
    }

    private static LoweredExpressionProgramCache Success(ExpressionProgram program) =>
        new(true, program, failureReason: null);

    private static LoweredExpressionProgramCache Failure(string failureReason) =>
        new(false, default, failureReason);
}
