#region

using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

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

                return AssignToSuperBinding(environment, context, superPropertyName, superAssignedValueJs, "property");
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

            if (expression.IsCompoundAssignment)
            {
                var propertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var reference = CreatePropertyReference(targetJs, propertyName, context, true);
                if (TryApplyCompoundAssignment(null, expression.Value, reference, environment, context, out var compoundValueJs))
                {
                    return compoundValueJs;
                }
            }

            var assignedValueJs = expression.Value.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValueJs;
            }

            // Only extract for IFunctionNameTarget check - keep as JsValue otherwise
            if (assignedValueJs is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget } &&
                expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
            {
                nameTarget.EnsureHasName(string.Empty);
            }

            var finalPropertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var finalReference = CreatePropertyReference(targetJs, finalPropertyName, context, true);
            finalReference.SetValue(assignedValueJs);
            return assignedValueJs;
        }
    }
}
