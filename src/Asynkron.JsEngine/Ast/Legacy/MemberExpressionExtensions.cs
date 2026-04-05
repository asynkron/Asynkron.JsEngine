#region

using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateMember(this MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Fast-path well-known symbol properties so expressions like
        // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
        // values that can be used as keys (e.g. o[Symbol.iterator]).
        if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
            string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
            expression.Property is LiteralExpression { Value.IsString: true } symbolPropLit)
        {
            var symbolProp = symbolPropLit.Value.AsString();
            return symbolProp switch
            {
                "iterator" => (JsValue)Symbols.Iterator,
                "asyncIterator" => (JsValue)Symbols.AsyncIterator,
                "toStringTag" => (JsValue)Symbols.ToStringTag,
                _ => expression.EvaluateDefaultMember(environment, context)
            };
        }

        return expression.EvaluateDefaultMember(environment, context);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDefaultMember(this MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is ThisExpression && !environment.IsThisInitializationKnownTrue(context))
        {
            throw StandardLibrary.ThrowReferenceError(
                "Must call super constructor in derived class before accessing 'this'",
                context,
                context.RealmState);
        }

        if (expression.Target is SuperExpression)
        {
            var (memberValue, _) = expression.ResolveSuperMember(environment, context);
            return context.ShouldStopEvaluation ? JsValue.Undefined : memberValue;
        }

        var targetJs = EvaluateCachedExpressionProgram(
            expression.Target,
            environment,
            context,
            "Dynamic member target");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (expression.IsOptional && targetJs.IsNullOrUndefined)
        {
            return JsValue.Undefined;
        }

        if (targetJs.IsNullOrUndefined && HasOptionalChaining(expression.Target))
        {
            return JsValue.Undefined;
        }

        if (targetJs.IsNullOrUndefined)
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState);
            context.SetThrow(error);
            return JsValue.Undefined;
        }

        // Fast path: for non-computed member access with literal string property,
        // skip expression evaluation and use the property name directly
        string? propertyName;
        if (expression is { IsComputed: false, Property: LiteralExpression { Value.IsString: true } literalProp })
        {
            propertyName = literalProp.Value.AsString();
        }
        else
        {
            var propertyValueJs = EvaluateCachedExpressionProgram(
                expression.Property,
                environment,
                context,
                "Dynamic member property");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            propertyName = JsOps.GetRequiredPropertyName(propertyValueJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        if (expression.IsComputed || !propertyName.IsPrivateName())
        {
            if (JsOps.TryGetPropertyValue(targetJs, propertyName, out var directValue, context))
            {
                return context.ShouldStopEvaluation
                    ? JsValue.Undefined
                    : directValue;
            }

            return JsValue.Undefined;
        }

        // Use JsValue overload of PropertyHandle.Resolve
        var handle = PropertyHandle.Resolve(
            targetJs,
            propertyName,
            context,
            context.CurrentScope.IsStrict,
            !expression.IsComputed);
        var value = handle.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return value;
    }

    private static (JsValue Value, SuperBinding Binding) ResolveSuperMember(this MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Per ES spec 12.3.5.3 MakeSuperPropertyReference:
        // 3. Let actualThis be ? env.GetThisBinding().
        // This throws ReferenceError if this is uninitialized
        if (!environment.IsThisInitializationKnownTrue(context))
        {
            throw environment.CreateSuperReferenceError(context);
        }

        var binding = environment.ExpectSuperBinding(context);

        // Per ES spec 12.3.5.3 MakeSuperPropertyReference:
        // 4. Let baseValue be ? env.GetSuperBase().
        // 5. Let bv be ? RequireObjectCoercible(baseValue).
        // If the prototype (super base) is null or undefined, throw TypeError
        if (binding.Prototype is null)
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null (reading from super)",
                context,
                context.RealmState);
            context.SetThrow(error);
            return (JsValue.Undefined, binding);
        }

        var propertyValueJs = EvaluateCachedExpressionProgram(
            expression.Property,
            environment,
            context,
            "Dynamic super member property");
        if (context.ShouldStopEvaluation)
        {
            return (JsValue.Undefined, binding);
        }

        // Use JsOps.GetRequiredPropertyName which properly handles errors from ToPropertyName
        // (e.g., when toString() throws during property key coercion)
        var propertyName = JsOps.GetRequiredPropertyName(propertyValueJs, context);
        if (context.ShouldStopEvaluation)
        {
            return (JsValue.Undefined, binding);
        }

        if (!binding.TryGetProperty(propertyName, out var value))
        {
            return (JsValue.Undefined, binding);
        }

        return (value, binding);
    }
}
