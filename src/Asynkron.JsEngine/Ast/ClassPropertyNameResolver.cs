#region

using System.Globalization;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

internal static class ClassPropertyNameResolver
{
    public static bool TryResolveMemberName(this ClassMember member, Func<ExpressionNode, JsValue> evaluator,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        return TryResolveNameCore(
            member.Name,
            member.IsComputed,
            member.ComputedName,
            member.IsPrivate,
            "class member",
            evaluator,
            context,
            privateNameScope,
            out propertyName);
    }

    public static bool TryResolveFieldName(this ClassField field, Func<ExpressionNode, JsValue> evaluator,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        return TryResolveNameCore(
            field.Name,
            field.IsComputed,
            field.ComputedName,
            field.IsPrivate,
            "class field",
            evaluator,
            context,
            privateNameScope,
            out propertyName);
    }

    private static bool TryResolveNameCore(
        string name,
        bool isComputed,
        ExpressionNode? computedName,
        bool isPrivate,
        string elementType,
        Func<ExpressionNode, JsValue> evaluator,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        propertyName = name;

        if (isComputed)
        {
            if (computedName is null)
            {
                throw new InvalidOperationException($"Computed {elementType} is missing name expression.");
            }

            var nameValue = evaluator(computedName);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            propertyName = JsOps.GetRequiredPropertyName(nameValue, context);
            return !context.ShouldStopEvaluation;
        }

        if (isPrivate && privateNameScope is not null)
        {
            propertyName = privateNameScope.GetKey(propertyName);
            return true;
        }

        if (double.TryParse(propertyName, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericKey))
        {
            propertyName = JsOps.ToCanonicalNumberString(numericKey);
        }

        return true;
    }
}
