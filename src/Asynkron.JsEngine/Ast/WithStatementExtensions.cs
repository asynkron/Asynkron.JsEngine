using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(WithStatement statement)
    {
        private JsValue EvaluateWithJsValue(JsEnvironment environment, EvaluationContext context)
        {
            var objValueJs = EvaluateExpression(statement.Object, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return objValueJs;
            }

            var objValue = objValueJs.ToObject();
            if (!TryConvertToWithBindingObject(objValue, context, out var withObject))
            {
                return JsValue.Undefined;
            }

            var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict, statement.Source, "with",
                withObject);
            var completion = EvaluateStatementJsValue(statement.Body, withEnv, context);

            // Per ES spec 14.11.2 step 8: Return Completion(UpdateEmpty(C, undefined))
            // If body completion is empty, return undefined instead
            return completion.IsUnit ? JsValue.Undefined : completion;
        }
    }
}
