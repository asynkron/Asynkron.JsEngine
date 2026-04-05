
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateArray(this ArrayExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray(context.RealmState);
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValueJs = EvaluateCachedExpressionProgram(
                    element.Expression!,
                    environment,
                    context,
                    "Dynamic array spread expression");
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValueJs, context))
                {
                    array.Push(item);
                }

                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                continue;
            }

            if (element.Expression is null)
            {
                array.PushHole();
            }
            else
            {
                array.Push(EvaluateCachedExpressionProgram(
                    element.Expression,
                    environment,
                    context,
                    "Dynamic array element expression"));
            }

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        return JsValue.FromJsArray(array);
    }
}
