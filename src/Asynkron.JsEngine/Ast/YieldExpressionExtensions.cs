#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(YieldExpression expression)
    {
        private JsValue EvaluateYield(JsEnvironment environment,
            EvaluationContext context)
        {
            // Most yield expressions should be lowered by GeneratorYieldLowerer and compiled to IR.
            // However, some yields (like those in destructuring default values) cannot be extracted
            // and are evaluated via StatementInstruction wrapping the containing for-of loop.
            // In this case, we signal a yield via the context so the caller can save state.

            if (expression.IsDelegated)
            {
                // yield* is more complex and should be handled by the IR interpreter.
                // If we reach here with yield*, something went wrong.
                throw new InvalidOperationException(
                    "Delegated yield (yield*) expression encountered during AST evaluation. " +
                    "This should have been lowered to IR by GeneratorYieldLowerer. " +
                    $"Source: {expression.Source?.StartPosition}-{expression.Source?.EndPosition}");
            }

            // Evaluate the yield operand if present
            var yieldedValue = JsValue.Undefined;
            if (expression.Expression is not null)
            {
                yieldedValue = expression.Expression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return yieldedValue;
                }
            }

            // Signal the yield via the context.
            // Use -1 as the yield index since we're in AST evaluation mode, not IR.
            // The IR interpreter will see context.IsYield and handle it appropriately.
            context.SetYield(yieldedValue, -1);

            // Return undefined; the actual resume value will be provided when the generator continues.
            // The caller (e.g., BindArrayPattern) will check context.IsYield and save state.
            return JsValue.Undefined;
        }
    }
}
