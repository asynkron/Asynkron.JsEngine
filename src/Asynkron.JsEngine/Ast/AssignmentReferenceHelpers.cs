using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static AssignmentReference CreatePropertyReference(
        object? target,
        string propertyName,
        EvaluationContext context)
    {
        return new AssignmentReference(
            () => ReadPropertyValue(target, propertyName, context),
            value => AssignPropertyValueWithNullCheck(target, propertyName, value, context));
    }

    private static object? ReadPropertyValue(object? target, string propertyName, EvaluationContext context)
    {
        if (IsNullish(target))
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState);
            context.SetThrow(error);
            return Symbol.Undefined;
        }

        return TryGetPropertyValue(target, propertyName, out var existingValue, context)
            ? existingValue
            : Symbol.Undefined;
    }

    private static void AssignPropertyValueWithNullCheck(
        object? target,
        string propertyName,
        object? value,
        EvaluationContext context)
    {
        if (IsNullish(target))
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot set property on null or undefined.",
                context,
                context.RealmState);
            context.SetThrow(error);
            return;
        }

        AssignPropertyValue(target, propertyName, value, context);
    }
}
