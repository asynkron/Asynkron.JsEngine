#region

using System.Globalization;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;

#endregion

namespace Asynkron.JsEngine.Ast;

internal static class ClassPropertyNameResolver
{
    public static bool TryResolveMemberName(this ClassMember member, ExpressionProgram? computedNameProgram,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        return TryResolveNameCore(
            member.Name,
            member.IsComputed,
            computedNameProgram,
            member.IsPrivate,
            "class member",
            environment,
            context,
            privateNameScope,
            out propertyName);
    }

    public static bool TryResolveFieldName(this ClassField field, ExpressionProgram? computedNameProgram,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        return TryResolveNameCore(
            field.Name,
            field.IsComputed,
            computedNameProgram,
            field.IsPrivate,
            "class field",
            environment,
            context,
            privateNameScope,
            out propertyName);
    }

    private static bool TryResolveNameCore(
        string name,
        bool isComputed,
        ExpressionProgram? computedNameProgram,
        bool isPrivate,
        string elementType,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope,
        out string propertyName)
    {
        propertyName = name;

        if (isComputed)
        {
            if (computedNameProgram is null)
            {
                throw new InvalidOperationException($"Computed {elementType} is missing lowered name program.");
            }

            var nameValue = TypedAstEvaluator.EvaluateLoweredExpressionProgram(
                computedNameProgram.Value,
                environment,
                context);
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
