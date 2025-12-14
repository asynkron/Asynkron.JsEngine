namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IfStatement statement)
    {
        private object? EvaluateIf(JsEnvironment environment, EvaluationContext context)
        {
            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var branch = test.IsTruthy ? statement.Then : statement.Else;
            if (branch is null)
            {
                return Symbol.Undefined;
            }

            object? result;
            if (branch is BlockStatement block)
            {
                result = EvaluateBlock(block, environment, context);
            }
            else
            {
                var branchScope = new JsEnvironment(environment, false, context.CurrentScope.IsStrict);
                result = EvaluateStatement(branch, branchScope, context);
            }

            // Per ECMAScript spec 14.6.2 (Runtime Semantics: Evaluation):
            // Return Completion(UpdateEmpty(stmtCompletion, undefined)).
            // UpdateEmpty replaces an empty completion value with undefined.
            return ReferenceEquals(result, EmptyCompletion) ? Symbol.Undefined : result;
        }
    }
}
