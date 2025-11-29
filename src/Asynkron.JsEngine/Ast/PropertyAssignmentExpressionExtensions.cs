using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(PropertyAssignmentExpression expression)
    {
        private object? EvaluatePropertyAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is MemberExpression { Target: SuperExpression } superMember)
            {
                if (!context.IsThisInitialized)
                {
                    throw CreateSuperReferenceError(environment, context, null);
                }

                var propertyKey = EvaluateExpression(superMember.Property, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var assignedValue = EvaluateExpression(expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var binding = ExpectSuperBinding(environment, context);
                binding.SetProperty(propertyName, assignedValue);
                return assignedValue;
            }

            var target = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (expression.IsComputed && IsNullish(target))
            {
                throw new InvalidOperationException("Cannot set property on null or undefined.");
            }

            var property = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var value = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            AssignPropertyValue(target, property, value, context);
            return value;
        }
    }
}
