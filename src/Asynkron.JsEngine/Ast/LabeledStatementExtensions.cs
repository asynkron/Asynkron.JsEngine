#region

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot paths.
    /// </summary>
    private static JsValue EvaluateLabeledJsValue(this LabeledStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = statement.Statement.EvaluateStatementJsValue(environment, context, statement.Label);

            // Per ES spec 13.13.14: If stmtResult.[[Type]] is break and the label matches,
            // return NormalCompletion(stmtResult.[[Value]]) - i.e., the completion value
            // before the break, not empty.
            context.TryClearBreak(statement.Label);
            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }
}
