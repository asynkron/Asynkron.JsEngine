using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IndexAssignmentExpression expression)
    {
        private JsValue EvaluateIndexAssignment(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is SuperExpression)
            {
                // According to ES spec 13.3.7.1, GetThisBinding must be evaluated BEFORE the index expression
                // to ensure ReferenceError is thrown if this is uninitialized before any side effects occur
                if (!context.IsThisInitialized)
                {
                    throw CreateSuperReferenceError(environment, context, null);
                }

                var superIndexJs = EvaluateExpression(expression.Index, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var superAssignedValueJs = EvaluateExpression(expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return superAssignedValueJs;
                }

                var superAssignedValue = superAssignedValueJs.ToObject();
                if (superAssignedValue is IFunctionNameTarget superNameTarget &&
                    expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                {
                    superNameTarget.EnsureHasName(string.Empty);
                }

                var propertyName = JsOps.GetRequiredPropertyName(superIndexJs.ToObject(), context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
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

                // Per ES spec 6.2.3.2 PutValue, if set fails in strict mode, throw TypeError
                if (!binding.TrySetProperty(propertyName, JsValue.FromObject(superAssignedValue), out _) &&
                    context.CurrentScope.IsStrict)
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot assign to read only property '{propertyName}' of object",
                        context,
                        context.RealmState);
                }

                return JsValue.FromObject(superAssignedValue);
            }

            var targetJs = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var indexJs = EvaluateExpression(expression.Index, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var target = targetJs.ToObject();
            var index = indexJs.ToObject();

            if (expression.IsCompoundAssignment)
            {
                if (IsNullish(target))
                {
                    throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                        context.RealmState);
                }

                var propertyName = JsOps.GetRequiredPropertyName(index, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                var reference = CreatePropertyReference(target, propertyName, context, allowPrivate: false);

                if (TryEvaluateCompoundAssignmentValue(null, expression.Value, reference, environment, context,
                        out var compoundValue, out var shouldAssign))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.FromObject(compoundValue);
                    }

                    if (shouldAssign)
                    {
                        reference.SetValue(JsValue.FromObject(compoundValue));
                    }

                    return JsValue.FromObject(compoundValue);
                }
            }

            var assignedValueJs = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValueJs;
            }

            var assignedValue = assignedValueJs.ToObject();
            if (assignedValue is IFunctionNameTarget nameTarget &&
                expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
            {
                nameTarget.EnsureHasName(string.Empty);
            }

            if (IsNullish(target))
            {
                throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                    context.RealmState);
            }

            var finalPropertyName = JsOps.GetRequiredPropertyName(index, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var finalReference = CreatePropertyReference(target, finalPropertyName, context, allowPrivate: false);
            finalReference.SetValue(JsValue.FromObject(assignedValue));
            return JsValue.FromObject(assignedValue);
        }
    }
}
