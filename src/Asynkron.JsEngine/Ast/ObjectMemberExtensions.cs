using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ObjectMember member)
    {
        private string ResolveObjectMemberName(JsEnvironment environment,
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

            var propertyNameFromKey = JsOps.GetRequiredPropertyName(member.Key, context);
            return context.ShouldStopEvaluation ? string.Empty : propertyNameFromKey;
        }
    }
}
