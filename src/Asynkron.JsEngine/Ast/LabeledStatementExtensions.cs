namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(LabeledStatement statement)
    {
        private object? EvaluateLabeled(JsEnvironment environment,
            EvaluationContext context)
        {
            context.PushLabel(statement.Label);
            try
            {
                var result = EvaluateStatement(statement.Statement, environment, context, statement.Label);

                return context.TryClearBreak(statement.Label) ? EmptyCompletion : result;
            }
            finally
            {
                context.PopLabel();
            }
        }
    }

}
