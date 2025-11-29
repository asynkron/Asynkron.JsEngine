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

            object? lastValue = Symbol.Undefined;
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
                            return lastValue;
                        }

                        hasMatched = StrictEquals(discriminant, test);
                    }

                    if (!hasMatched)
                    {
                        continue;
                    }
                }

                lastValue = EvaluateBlock(switchCase.Body, environment, context);
                if (context.TryClearBreak(targetLabel))
                {
                    break;
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }
            }

            return lastValue;
        }
    }
}
