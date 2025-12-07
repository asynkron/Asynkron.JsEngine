using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(PropertyAssignmentExpression expression)
    {
        private object? EvaluatePropertyAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            var superPropertyExpression = expression.Target switch
            {
                MemberExpression { Target: SuperExpression } member => member.Property,
                SuperExpression => expression.Property,
                _ => null
            };

            if (superPropertyExpression is not null)
            {
                var logger = environment.RealmState?.Logger;
                var hasOwnSuper = environment.HasBinding(Symbol.Super);
                var hasOwnThis = environment.HasBinding(Symbol.This);
                logger?.LogInformation(
                    "SuperAssignment: start env={Env} thisInit={ThisInit} hasOwnSuper={HasSuper} hasOwnThis={HasThis}",
                    environment.GetHashCode(),
                    context.IsThisInitialized,
                    hasOwnSuper,
                    hasOwnThis);

                // According to ES spec 13.3.7.1, GetThisBinding must be evaluated BEFORE the property expression
                // to ensure ReferenceError is thrown if this is uninitialized before any side effects occur
                if (!context.IsThisInitialized)
                {
                    throw CreateSuperReferenceError(environment, context, null);
                }

                var propertyKey = EvaluateExpression(superPropertyExpression, environment, context);
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
                environment.RealmState?.Logger?.LogInformation(
                    "SuperBinding: assign super property protoNull={ProtoNull} thisInit={ThisInit}",
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

                // Per ES spec 6.2.3.2 PutValue, if set fails in strict mode, throw TypeError
                if (!binding.TrySetProperty(superPropertyName, superAssignedValue, out _) &&
                    context.CurrentScope.IsStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to read only property '{superPropertyName}' of object",
                        context,
                        context.RealmState);
                }

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

                var reference = CreatePropertyReference(target, propertyName, context, allowPrivate: true);
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

            var finalReference = CreatePropertyReference(target, finalPropertyName, context, allowPrivate: true);
            finalReference.SetValue(assignedValue);
            return assignedValue;
        }
    }
}
