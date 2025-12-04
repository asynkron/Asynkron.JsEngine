using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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

                var superAssignedValue = EvaluateExpression(expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return superAssignedValue;
                }

                var superPropertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var binding = ExpectSuperBinding(environment, context);
                if (binding.Prototype is null)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot assign to super property when prototype is null or undefined.",
                        context,
                        context.RealmState);
                }

                binding.SetProperty(superPropertyName, superAssignedValue);
                return superAssignedValue;
            }

            var target = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var property = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (expression.IsCompoundAssignment)
            {
                var propertyName = JsOps.GetRequiredPropertyName(property, context);
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

            var finalPropertyName = JsOps.GetRequiredPropertyName(property, context);
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
