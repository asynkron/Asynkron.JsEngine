using Asynkron.JsEngine.JsTypes;
using JsSymbols = Asynkron.JsEngine.Ast.Symbols;

namespace Asynkron.JsEngine.Ast;

internal readonly record struct AssignmentReference(Func<object?> GetValue, Action<object?> SetValue);

internal static class AssignmentReferenceResolver
{
    public static AssignmentReference Resolve(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression)
    {
        return expression switch
        {
            IdentifierExpression identifier => new AssignmentReference(
                () => environment.Get(identifier.Name),
                value => environment.Assign(identifier.Name, value)),
            MemberExpression member => ResolveMember(member, environment, context, evaluateExpression),
            UnaryExpression { Operator: "++" or "--" } unary =>
                Resolve(unary.Operand, environment, context, evaluateExpression),
            _ => throw new NotSupportedException("Unsupported assignment target.")
        };
    }

    private static AssignmentReference ResolveMember(
        MemberExpression member,
        JsEnvironment environment,
        EvaluationContext context,
        Func<ExpressionNode, JsEnvironment, EvaluationContext, object?> evaluateExpression)
    {
        var target = evaluateExpression(member.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => JsSymbols.Undefined, _ => { });
        }

        var propertyValue = evaluateExpression(member.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => JsSymbols.Undefined, _ => { });
        }

        if (target is JsArray jsArray && JsOps.TryResolveArrayIndex(propertyValue, out var arrayIndex, context))
        {
            return new AssignmentReference(
                () => jsArray.GetElement(arrayIndex),
                newValue => jsArray.SetElement(arrayIndex, newValue));
        }

        if (target is TypedArrayBase typedArray && JsOps.TryResolveArrayIndex(propertyValue, out var typedIndex, context))
        {
            return new AssignmentReference(
                () => typedIndex >= 0 && typedIndex < typedArray.Length
                    ? typedArray.GetElement(typedIndex)
                    : JsSymbols.Undefined,
                newValue =>
                {
                    if (typedIndex >= 0 && typedIndex < typedArray.Length)
                    {
                        typedArray.SetElement(typedIndex, JsOps.ToNumber(newValue));
                    }
                });
        }

        var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);

        return new AssignmentReference(
            () => JsOps.TryGetPropertyValue(target, propertyName, out var value) ? value : JsSymbols.Undefined,
            newValue => JsOps.AssignPropertyValueByName(target, propertyName, newValue));
    }
}
