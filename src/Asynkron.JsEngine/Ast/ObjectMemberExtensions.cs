#region

using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static string ResolveObjectMemberName(this ObjectMember member, JsEnvironment environment,
        EvaluationContext context)
    {
        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            var keyValue = keyExpression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return string.Empty;
            }

            var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
            return context.ShouldStopEvaluation ? string.Empty : propertyName;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        var propertyNameFromKey = JsOps.GetRequiredPropertyName(JsValue.FromObjectUnsafe(member.Key), context);
        return context.ShouldStopEvaluation ? string.Empty : propertyNameFromKey;
    }
}
