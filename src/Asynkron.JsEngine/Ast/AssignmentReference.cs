using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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
            IdentifierExpression identifier => ResolveIdentifier(identifier, environment, context),
            MemberExpression member => ResolveMember(member, environment, context, evaluateExpression),
            UnaryExpression { Operator: "++" or "--" } unary =>
                Resolve(unary.Operand, environment, context, evaluateExpression),
            _ => throw new NotSupportedException("Unsupported assignment target.")
        };
    }

    private static AssignmentReference ResolveIdentifier(IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        if (environment.IsStrict &&
            (string.Equals(identifier.Name.Name, "eval", StringComparison.Ordinal) ||
             string.Equals(identifier.Name.Name, "arguments", StringComparison.Ordinal)))
        {
            throw new ThrowSignal(StandardLibrary.CreateSyntaxError(
                "Assignment to eval or arguments is not allowed in strict mode.", context, context.RealmState));
        }

        return new AssignmentReference(
            () => environment.Get(identifier.Name),
            value => environment.Assign(identifier.Name, value));
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
            return new AssignmentReference(() => Symbol.Undefined, _ => { });
        }

        var propertyValue = evaluateExpression(member.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return new AssignmentReference(() => Symbol.Undefined, _ => { });
        }

        if (target is JsArray jsArray && JsOps.TryResolveArrayIndex(propertyValue, out var arrayIndex, context))
        {
            return new AssignmentReference(
                () => jsArray.GetElement(arrayIndex),
                newValue => jsArray.SetElement(arrayIndex, newValue));
        }

        if (target is TypedArrayBase typedArray &&
            JsOps.TryResolveArrayIndex(propertyValue, out var typedIndex, context))
        {
            return new AssignmentReference(
                () => typedIndex >= 0 && typedIndex < typedArray.Length
                    ? typedArray.GetElement(typedIndex)
                    : Symbol.Undefined,
                newValue =>
                {
                    if (typedIndex >= 0 && typedIndex < typedArray.Length)
                    {
                        typedArray.SetElement(typedIndex, JsOps.ToNumber(newValue));
                    }
                });
        }

        var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
        var isPrivateName = propertyName.Length > 0 && propertyName[0] == '#';
        if (isPrivateName)
        {
            var privateScope = context.CurrentPrivateNameScope;
            if (privateScope is null)
            {
                PrivateNameScope.TryResolveScope(propertyName, out privateScope);
            }

            if (privateScope is null)
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
            }

            var brandToken = privateScope.BrandToken;
            if (target is not IPrivateBrandHolder brandHolder || !brandHolder.HasPrivateBrand(brandToken))
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
            }
        }

        return new AssignmentReference(
            () => JsOps.TryGetPropertyValue(target, propertyName, out var value) ? value : Symbol.Undefined,
            newValue => JsOps.AssignPropertyValueByName(target, propertyName, newValue));
    }
}
