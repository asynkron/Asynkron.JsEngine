using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SequenceExpression expression)
    {
        private JsValue EvaluateSequence(JsEnvironment environment,
            EvaluationContext context)
        {
            _ = EvaluateExpression(expression.Left, environment, context);
            return context.ShouldStopEvaluation
                ? JsValue.Undefined
                : EvaluateExpression(expression.Right, environment, context);
        }
    }
}
