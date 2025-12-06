namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SwitchStatement statement)
    {
        private object? EvaluateSwitch(JsEnvironment environment,
            EvaluationContext context,
            Symbol? targetLabel)
        {
            var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            // V = undefined (spec step 1)
            object? completionValue = Symbol.Undefined;
            var hasMatched = false;

            foreach (var switchCase in statement.Cases)
            {
                if (!hasMatched)
                {
                    if (switchCase.Test is null)
                    {
                        hasMatched = true;
                    }
                    else
                    {
                        var test = EvaluateExpression(switchCase.Test, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return completionValue;
                        }

                        hasMatched = StrictEquals(discriminant, test);
                    }

                    if (!hasMatched)
                    {
                        continue;
                    }
                }

                // Evaluate the case clause body
                var caseCompletion = EvaluateBlock(switchCase.Body, environment, context);

                // If R.[[value]] is not empty, let V = R.[[value]] (spec step 4.b.ii)
                // UpdateEmpty semantics: only update V if the completion is not empty
                if (!ReferenceEquals(caseCompletion, EmptyCompletion))
                {
                    completionValue = caseCompletion;
                }

                if (context.TryClearBreak(targetLabel))
                {
                    // Return Completion(UpdateEmpty(R, V)) (spec step 4.b.iii)
                    // Break already happened, return the accumulated value
                    break;
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }
            }

            return completionValue;
        }
    }
}
