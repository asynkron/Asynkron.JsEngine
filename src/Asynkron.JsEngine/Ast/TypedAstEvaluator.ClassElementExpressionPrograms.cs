#region

using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateClassElementExpressionProgram(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (!ExpressionProgramCompiler.TryCompile(expression, out var program, out var failureReason))
        {
            throw new NotSupportedException(
                $"Class element expression could not be lowered to expression bytecode: {failureReason ?? "unknown failure"}");
        }

        return ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(program, environment, context);
    }
}
