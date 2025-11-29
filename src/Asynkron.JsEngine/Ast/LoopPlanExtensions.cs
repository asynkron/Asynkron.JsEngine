using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(LoopPlan plan)
    {
        private object? EvaluateLoopPlan(JsEnvironment environment, EvaluationContext context,
            Symbol? loopLabel)
        {
            object? lastValue = Symbol.Undefined;

            if (!plan.LeadingStatements.IsDefaultOrEmpty)
            {
                foreach (var statement in plan.LeadingStatements)
                {
                    lastValue = EvaluateStatement(statement, environment, context, loopLabel);
                    if (context.ShouldStopEvaluation)
                    {
                        return NormalizeLoopCompletion(lastValue);
                    }
                }
            }

            while (true)
            {
                context.ThrowIfCancellationRequested();

                if (!plan.ConditionAfterBody)
                {
                    if (!ExecuteCondition(plan, environment, context))
                    {
                        break;
                    }
                }

                lastValue = EvaluateStatement(plan.Body, environment, context, loopLabel);
                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }

                if (context.TryClearContinue(loopLabel))
                {
                    if (!ExecutePostIteration(plan, environment, context, ref lastValue))
                    {
                        break;
                    }

                    if (plan.ConditionAfterBody && !ExecuteCondition(plan, environment, context))
                    {
                        break;
                    }

                    continue;
                }

                if (context.TryClearBreak(loopLabel))
                {
                    break;
                }

                if (context.ShouldStopEvaluation)
                {
                    break;
                }

                if (!ExecutePostIteration(plan, environment, context, ref lastValue))
                {
                    break;
                }

                if (!plan.ConditionAfterBody)
                {
                    continue;
                }

                if (!ExecuteCondition(plan, environment, context))
                {
                    break;
                }
            }

            return NormalizeLoopCompletion(lastValue);
        }

        private bool ExecuteCondition(JsEnvironment environment, EvaluationContext context)
        {
            if (!plan.ConditionPrologue.IsDefaultOrEmpty)
            {
                foreach (var statement in plan.ConditionPrologue)
                {
                    _ = EvaluateStatement(statement, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }
                }
            }

            var test = EvaluateExpression(plan.Condition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            return IsTruthy(test);
        }

        private bool ExecutePostIteration(JsEnvironment environment, EvaluationContext context,
            ref object? lastValue)
        {
            if (plan.PostIteration.IsDefaultOrEmpty)
            {
                return true;
            }

            foreach (var statement in plan.PostIteration)
            {
                lastValue = EvaluateStatement(statement, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
