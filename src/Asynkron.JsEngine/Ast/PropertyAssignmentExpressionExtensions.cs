using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(PropertyAssignmentExpression expression)
    {
        private JsValue EvaluatePropertyAssignment(JsEnvironment environment,
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
                    throw environment.CreateSuperReferenceError(context, null);
                }

                var propertyKeyJs = superPropertyExpression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var superAssignedValueJs = expression.Value.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return superAssignedValueJs;
                }

                var superPropertyName = JsOps.GetRequiredPropertyName(propertyKeyJs, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var binding = environment.ExpectSuperBinding(context);
                environment.RealmState?.Logger?.LogInformation(
                    "SuperBinding: assign super property protoNull={ProtoNull} thisInit={ThisInit}",
                    binding.Prototype is null,
                    binding.IsThisInitialized);
                if (!binding.IsThisInitialized)
                {
                    throw environment.CreateSuperReferenceError(context, null);
                }

                if (binding.Prototype is null)
                {
                    throw StandardLibrary.ThrowTypeError(
                        "Cannot assign to super property when prototype is null or undefined.",
                        context,
                        context.RealmState);
                }

                // Per ES spec 6.2.3.2 PutValue, if set fails in strict mode, throw TypeError
                if (!binding.TrySetProperty(superPropertyName, superAssignedValueJs, out _) &&
                    context.CurrentScope.IsStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to read only property '{superPropertyName}' of object",
                        context,
                        context.RealmState);
                }

                return superAssignedValueJs;
            }

            var targetJs = expression.Target.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var propertyJs = expression.Property.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            // For property access, primitives need proper boxing
            var target = targetJs.Kind switch
            {
                JsValueKind.Boolean => (object?)(targetJs.NumberValue != 0),
                JsValueKind.Number => targetJs.NumberValue,
                JsValueKind.String => targetJs.ObjectValue,
                JsValueKind.Symbol => targetJs.ObjectValue,
                JsValueKind.BigInt => targetJs.ObjectValue,
                JsValueKind.Object => targetJs.ObjectValue,
                JsValueKind.Null => null,
                JsValueKind.Undefined => null,
                _ => targetJs.ObjectValue
            };

            if (expression.IsCompoundAssignment)
            {
                var propertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var reference = CreatePropertyReference(target, propertyName, context, allowPrivate: true);
                if (TryEvaluateCompoundAssignmentJsValue(null, expression.Value, reference, environment, context,
                        out var compoundValueJs, out var shouldAssign))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return compoundValueJs;
                    }

                    if (shouldAssign)
                    {
                        reference.SetValue(compoundValueJs);
                    }

                    return compoundValueJs;
                }
            }

            var assignedValueJs = expression.Value.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValueJs;
            }

            // Only extract for IFunctionNameTarget check - keep as JsValue otherwise
            if (assignedValueJs.Kind == JsValueKind.Object &&
                assignedValueJs.ObjectValue is IFunctionNameTarget nameTarget &&
                expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
            {
                nameTarget.EnsureHasName(string.Empty);
            }

            var finalPropertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var finalReference = CreatePropertyReference(target, finalPropertyName, context, allowPrivate: true);
            finalReference.SetValue(assignedValueJs);
            return assignedValueJs;
        }
    }
}
