
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateSequence(this SequenceExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        _ = expression.Left.EvaluateExpression(environment, context);
        return context.ShouldStopEvaluation
            ? JsValue.Undefined
            : expression.Right.EvaluateExpression(environment, context);
    }
}
