using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ReturnStatement statement)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateReturnJsValue(
            JsEnvironment environment,
            EvaluationContext context)
        {
            var jsValue = statement.Expression is null
                ? JsValue.Undefined
                : EvaluateExpression(statement.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return jsValue;
            }

            context.SetReturn(jsValue);
            return jsValue;
        }
    }
}
