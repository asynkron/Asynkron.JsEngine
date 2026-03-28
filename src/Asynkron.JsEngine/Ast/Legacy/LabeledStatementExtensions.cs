
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateLabeledJsValue(this LabeledStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = statement.Statement.EvaluateStatementJsValue(environment, context, statement.Label);

            // Per ES spec, labeled statement completion is UpdateEmpty(stmtResult, undefined)
            // when the labeled break is handled here. A matching `break label;` therefore
            // produces undefined, not an empty completion that would preserve a previous
            // script completion value.
            if (context.TryClearBreak(statement.Label) && result.IsUnit)
            {
                return JsValue.Undefined;
            }

            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }
}
