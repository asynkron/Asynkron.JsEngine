using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(WithStatement statement)
    {
        private JsValue EvaluateWithJsValue(JsEnvironment environment, EvaluationContext context)
        {
            var objValueJs = statement.Object.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return objValueJs;
            }

            // Extract object value - TryConvertToWithBindingObject will handle wrapping primitives
            // and throwing for null/undefined. Must properly box primitives for ToObjectForDestructuring.
            var objValue = objValueJs.Kind switch
            {
                JsValueKind.Boolean => (object?)(objValueJs.NumberValue != 0),
                JsValueKind.Number => objValueJs.NumberValue,
                JsValueKind.String => objValueJs.ObjectValue,
                JsValueKind.Symbol => objValueJs.ObjectValue,
                JsValueKind.BigInt => objValueJs.ObjectValue,
                JsValueKind.Object => objValueJs.ObjectValue,
                JsValueKind.Null => null,
                JsValueKind.Undefined => null,
                _ => objValueJs.ObjectValue
            };
            if (!TryConvertToWithBindingObject(objValue, context, out var withObject))
            {
                return JsValue.Undefined;
            }

            var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict, statement.Source, "with",
                withObject);
            var completion = statement.Body.EvaluateStatementJsValue(withEnv, context);

            // Per ES spec 14.11.2 step 8: Return Completion(UpdateEmpty(C, undefined))
            // If body completion is empty, return undefined instead
            return completion.IsUnit ? JsValue.Undefined : completion;
        }
    }
}
