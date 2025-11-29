using Asynkron.JsEngine;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

internal static class ClassPropertyNameResolver
{
    public static bool TryResolveMemberName(
        ClassMember member,
        Func<ExpressionNode, object?> evaluator,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        propertyName = member.Name;
        if (member.IsComputed)
        {
            if (member.ComputedName is null)
            {
                throw new InvalidOperationException("Computed class member is missing name expression.");
            }

            var nameValue = evaluator(member.ComputedName);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            propertyName = JsOps.GetRequiredPropertyName(nameValue, context);
            return !context.ShouldStopEvaluation;
        }

        if (propertyName.Length > 0 && propertyName[0] == '#' && privateNameScope is not null)
        {
            propertyName = privateNameScope.GetKey(propertyName);
        }

        return true;
    }

    public static bool TryResolveFieldName(
        ClassField field,
        Func<ExpressionNode, object?> evaluator,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        propertyName = field.Name;
        if (field.IsComputed)
        {
            if (field.ComputedName is null)
            {
                throw new InvalidOperationException("Computed class field is missing name expression.");
            }

            var nameValue = evaluator(field.ComputedName);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            propertyName = JsOps.GetRequiredPropertyName(nameValue, context);
            return !context.ShouldStopEvaluation;
        }

        if (field.IsPrivate && privateNameScope is not null)
        {
            propertyName = privateNameScope.GetKey(propertyName);
        }

        return true;
    }
}
