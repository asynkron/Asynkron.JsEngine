using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

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
                var superIndex = EvaluateExpression(expression.Index, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var superAssignedValue = EvaluateExpression(expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return superAssignedValue;
                }

                if (superAssignedValue is IFunctionNameTarget superNameTarget &&
                    expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                {
                    superNameTarget.EnsureHasName(string.Empty);
                }

                var propertyName = JsOps.GetRequiredPropertyName(superIndex, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                var binding = ExpectSuperBinding(environment, context);
                environment.RealmState?.Logger?.LogInformation(
                    "SuperBinding: assign super index protoNull={ProtoNull} thisInit={ThisInit}",
                    binding.Prototype is null,
                    binding.IsThisInitialized);
                if (!binding.IsThisInitialized)
                {
                    throw CreateSuperReferenceError(environment, context, null);
                }

                if (binding.Prototype is null)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot assign to super property when prototype is null or undefined.",
                        context,
                        context.RealmState);
                }

                binding.SetProperty(propertyName, superAssignedValue);
                return superAssignedValue;
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
                if (target.IsNullish())
                {
                    throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                        context.RealmState);
                }

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

            if (target.IsNullish())
            {
                throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                    context.RealmState);
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
