#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateIndexAssignment(this IndexAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is SuperExpression)
        {
            // According to ES spec 13.3.7.1, GetThisBinding must be evaluated BEFORE the index expression
            // to ensure ReferenceError is thrown if this is uninitialized before any side effects occur
            if (!context.IsThisInitialized)
            {
                throw environment.CreateSuperReferenceError(context, null);
            }

            var superIndexJs = expression.Index.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var superAssignedValueJs = expression.Value.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return superAssignedValueJs;
            }

            // Only extract for IFunctionNameTarget check - keep as JsValue otherwise
            if (superAssignedValueJs is
                    { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget superNameTarget } &&
                expression.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
            {
                superNameTarget.EnsureHasName(string.Empty);
            }

            var propertyName = JsOps.GetRequiredPropertyName(superIndexJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return AssignToSuperBinding(environment, context, propertyName, superAssignedValueJs, "index");
        }

        var targetJs = expression.Target.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var indexJs = expression.Index.EvaluateExpression(environment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (expression.IsCompoundAssignment)
        {
            if (targetJs.IsNullOrUndefined)
            {
                throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                    context.RealmState);
            }

            var propertyName = JsOps.GetRequiredPropertyName(indexJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var reference = CreatePropertyReference(targetJs, propertyName, context, false);

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

        if (targetJs.IsNullOrUndefined)
        {
            throw StandardLibrary.ThrowTypeError("Cannot read properties of null or undefined", context,
                context.RealmState);
        }

        var finalPropertyName = JsOps.GetRequiredPropertyName(indexJs, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var finalReference = CreatePropertyReference(targetJs, finalPropertyName, context, false);
        finalReference.SetValue(assignedValueJs);
        return assignedValueJs;
    }
}
