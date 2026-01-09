#region

using System.Runtime.CompilerServices;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// JsValue-returning version for use in hot loops to avoid boxing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIfJsValue(this IfStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var test = statement.Condition.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var branch = test.IsTruthy ? statement.Then : statement.Else;
        if (branch is null)
        {
            // Per ES spec 14.6.2, if condition is false and no else branch, result is
            // NormalCompletion(undefined).
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
        // If the statement returns an empty completion (Unit), replace with undefined.
        return result.IsUnit ? JsValue.Undefined : result;
    }
}
