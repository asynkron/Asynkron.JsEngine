using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IfStatement statement)
    {
        /// <summary>
        /// Object-returning version for compatibility with existing callers.
        /// </summary>
        private object? EvaluateIf(JsEnvironment environment, EvaluationContext context)
        {
            return EvaluateIfJsValue(statement, environment, context).ToObject();
        }

        /// <summary>
        /// JsValue-returning version for use in hot loops to avoid boxing.
        /// </summary>
        private JsValue EvaluateIfJsValue(JsEnvironment environment, EvaluationContext context)
        {
            var test = EvaluateExpression(statement.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var branch = test.IsTruthy ? statement.Then : statement.Else;
            if (branch is null)
            {
                return JsValue.Undefined;
            }

            JsValue result;
            if (branch is BlockStatement block)
            {
                result = EvaluateBlockJsValue(block, environment, context);
            }
            else
            {
                var branchScope = new JsEnvironment(environment, false, context.CurrentScope.IsStrict);
                result = EvaluateStatementJsValue(branch, branchScope, context);
            }

            // Per ECMAScript spec 14.6.2 (Runtime Semantics: Evaluation):
            // Return Completion(UpdateEmpty(stmtCompletion, undefined)).
            // UpdateEmpty replaces an empty completion value with undefined.
            // Note: JsValue.Undefined is used for empty completion
            return result;
        }
    }
}
