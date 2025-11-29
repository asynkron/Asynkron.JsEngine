using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IndexAssignmentExpression expression)
    {
        private object? EvaluateIndexAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is SuperExpression)
            {
                throw new InvalidOperationException(
                    $"Assigning through super is not supported.{GetSourceInfo(context, expression.Source)}");
            }

            var target = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var index = EvaluateExpression(expression.Index, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var propertyName = JsOps.GetRequiredPropertyName(index, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var reference = CreatePropertyReference(target, propertyName, context);

            if (expression.IsCompoundAssignment &&
                TryEvaluateCompoundAssignmentValue(expression.Value, reference, environment, context,
                    out var compoundValue))
            {
                if (context.ShouldStopEvaluation)
                {
                    return compoundValue;
                }

                reference.SetValue(compoundValue);
                return compoundValue;
            }

            var assignedValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValue;
            }

            reference.SetValue(assignedValue);
            return assignedValue;
        }
    }
}
