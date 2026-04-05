
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateWithJsValue(this WithStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var objValueJs = EvaluateCachedExpressionProgram(
            statement.Object,
            environment,
            context,
            "Dynamic with object");
        if (context.ShouldStopEvaluation)
        {
            return objValueJs;
        }

        // TryConvertToWithBindingObject will handle wrapping primitives and throwing for null/undefined.
        if (!TryConvertToWithBindingObject(objValueJs, context, out var withObject))
        {
            return JsValue.Undefined;
        }

        var withEnv = JsEnvironment.CreateInstance(environment, false, context.CurrentScope.IsStrict, statement.Source, "with",
            withObject);
        var previousAllowIdentifierCache = context.AllowIdentifierCache;
        context.AllowIdentifierCache = false;

        JsValue completion;
        try
        {
            completion = statement.Body.EvaluateStatementJsValue(withEnv, context);
        }
        finally
        {
            context.AllowIdentifierCache = previousAllowIdentifierCache;
        }

        // Per ES spec 14.11.2 step 8: Return Completion(UpdateEmpty(C, undefined))
        // If body completion is empty, return undefined instead
        return completion.IsUnit ? JsValue.Undefined : completion;
    }
}
