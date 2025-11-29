using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ThrowStatement statement)
    {
        private object? EvaluateThrow(JsEnvironment environment, EvaluationContext context)
        {
            var value = EvaluateExpression(statement.Expression, environment, context);
            context.SetThrow(value);
            return value;
        }
    }
}
