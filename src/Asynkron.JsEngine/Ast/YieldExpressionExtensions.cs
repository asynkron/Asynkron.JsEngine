#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Well-known symbol for storing yield resume state in the environment.
    /// Used when yields happen inside StatementInstruction (e.g., in destructuring defaults).
    /// </summary>
    private static readonly Symbol YieldResumeStateKey = Symbol.Intern("__yield_resume_state__");

    /// <summary>
    /// Sets the yield resume value in the environment so that the next call to EvaluateYield
    /// with a matching source position will return this value instead of yielding.
    /// </summary>
    internal static void SetYieldResumeValue(JsEnvironment environment, JsValue resumeValue, int yieldSourceStart,
        int yieldSourceEnd)
    {
        var state = new YieldResumeState
        {
            HasResumeValue = true,
            ResumeValue = resumeValue,
            YieldSourceStart = yieldSourceStart,
            YieldSourceEnd = yieldSourceEnd
        };

        if (environment.HasOwnBinding(YieldResumeStateKey))
        {
            environment.AssignJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state));
        }
        else
        {
            environment.DefineJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state), isLexical: true,
                canDelete: true);
        }
    }

    /// <summary>
    /// State for resuming from a yield that happened during AST evaluation (via StatementInstruction).
    /// </summary>
    internal sealed class YieldResumeState
    {
        /// <summary>
        /// When true, the yield has been resumed and ResumeValue should be returned.
        /// </summary>
        public bool HasResumeValue { get; set; }

        /// <summary>
        /// The value passed to iter.next(value) when resuming.
        /// </summary>
        public JsValue ResumeValue { get; set; }

        /// <summary>
        /// Source position of the yield expression that yielded.
        /// Used to match the correct yield on resume.
        /// </summary>
        public int YieldSourceStart { get; set; }

        public int YieldSourceEnd { get; set; }
    }

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

            // Check if we're resuming from a previous yield at this position.
            // If so, return the resume value instead of yielding again.
            if (environment.TryGetObject<YieldResumeState>(YieldResumeStateKey, out var resumeState) &&
                resumeState.HasResumeValue &&
                resumeState.YieldSourceStart == (expression.Source?.StartPosition ?? -1) &&
                resumeState.YieldSourceEnd == (expression.Source?.EndPosition ?? -1))
            {
                // Clear the resume state so future yields at this position work correctly
                resumeState.HasResumeValue = false;
                return resumeState.ResumeValue;
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
            // Use the source position to identify this yield for resume.
            context.SetYield(yieldedValue, expression.Source?.StartPosition ?? -1);

            // Store the yield position so the IR interpreter can set up resume state
            context.LastYieldSourceStart = expression.Source?.StartPosition ?? -1;
            context.LastYieldSourceEnd = expression.Source?.EndPosition ?? -1;

            // Return undefined; the actual resume value will be provided when the generator continues.
            // The caller (e.g., BindArrayPattern) will check context.IsYield and save state.
            return JsValue.Undefined;
        }
    }
}
