#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IfStatement statement)
    {
        /// <summary>
        /// JsValue-returning version for use in hot loops to avoid boxing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateIfJsValue(JsEnvironment environment, EvaluationContext context)
        {
            var test = statement.Condition.EvaluateExpression(environment, context);
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
                result = block.EvaluateBlockJsValue(environment, context);
            }
            else
            {
                // For simple statements that don't introduce new bindings (return, throw, expression, etc.),
                // we can reuse the parent environment instead of creating a new one.
                // Only block statements with let/const need their own lexical environment.
                result = branch.EvaluateStatementJsValue(environment, context);
            }

            // Per ECMAScript spec 14.6.2 (Runtime Semantics: Evaluation):
            // Return Completion(UpdateEmpty(stmtCompletion, undefined)).
            // UpdateEmpty replaces an empty completion value with undefined.
            return result.IsUnit ? JsValue.Undefined : result;
        }
    }
}
