#region

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateWithJsValue(this WithStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var objValueJs = statement.Object.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return objValueJs;
        }

        // TryConvertToWithBindingObject will handle wrapping primitives and throwing for null/undefined.
        if (!TryConvertToWithBindingObject(objValueJs, context, out var withObject))
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
