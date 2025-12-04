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

            if (expression.IsCompoundAssignment)
            {
                var propertyName = JsOps.GetRequiredPropertyName(index, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var reference = CreatePropertyReference(target, propertyName, context);

                if (TryEvaluateCompoundAssignmentValue(expression.Value, reference, environment, context,
                        out var compoundValue))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return compoundValue;
                    }

                    reference.SetValue(compoundValue);
                    return compoundValue;
                }
            }

            var assignedValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValue;
            }

            if (assignedValue is IFunctionNameTarget nameTarget &&
                expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
            {
                nameTarget.EnsureHasName(string.Empty);
            }

            var finalPropertyName = JsOps.GetRequiredPropertyName(index, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var finalReference = CreatePropertyReference(target, finalPropertyName, context);
            finalReference.SetValue(assignedValue);
            return assignedValue;
        }
    }
}
