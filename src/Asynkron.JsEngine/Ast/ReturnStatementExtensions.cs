using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ReturnStatement statement)
    {
        private object? EvaluateReturn(
            JsEnvironment environment,
            EvaluationContext context)
        {
            var jsValue = statement.Expression is null
                ? JsValue.Undefined
                : EvaluateExpression(statement.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return jsValue.ToObject();
            }

            context.SetReturn(jsValue);
            return jsValue.ToObject();
        }
    }
}
