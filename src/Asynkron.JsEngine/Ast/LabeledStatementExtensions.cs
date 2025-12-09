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

                // Per ES spec 13.13.14: If stmtResult.[[Type]] is break and the label matches,
                // return NormalCompletion(stmtResult.[[Value]]) - i.e., the completion value
                // before the break, not empty.
                if (context.TryClearBreak(statement.Label))
                {
                    // Per ES spec UpdateEmpty semantics: if result is empty, use undefined
                    return ReferenceEquals(result, EmptyCompletion) ? Symbol.Undefined : result;
                }

                return result;
            }
            finally
            {
                context.PopLabel();
            }
        }
    }
}
