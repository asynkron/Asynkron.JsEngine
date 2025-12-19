using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SequenceExpression expression)
    {
        private JsValue EvaluateSequence(JsEnvironment environment,
            EvaluationContext context)
        {
            _ = expression.Left.EvaluateExpression(environment, context);
            return context.ShouldStopEvaluation
                ? JsValue.Undefined
                : expression.Right.EvaluateExpression(environment, context);
        }
    }
}
