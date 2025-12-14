using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(MemberExpression expression)
    {
        private object? EvaluateMember(JsEnvironment environment,
            EvaluationContext context)
        {
            // Fast-path well-known symbol properties so expressions like
            // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
            // values that can be used as keys (e.g. o[Symbol.iterator]).
            if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
                string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
                expression.Property is LiteralExpression { Value: string symbolProp })
            {
                return symbolProp switch
                {
                    "iterator" => Symbols.Iterator,
                    "asyncIterator" => Symbols.AsyncIterator,
                    "toStringTag" => Symbols.ToStringTag,
                    _ => EvaluateDefaultMember(expression, environment, context)
                };
            }

            return EvaluateDefaultMember(expression, environment, context);
        }

        private object? EvaluateDefaultMember(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is ThisExpression && !context.IsThisInitialized)
            {
                throw StandardLibrary.ThrowReferenceError(
                    "Must call super constructor in derived class before accessing 'this'",
                    context,
                    context.RealmState);
            }

            if (expression.Target is SuperExpression)
            {
                var (memberValue, _) = ResolveSuperMember(expression, environment, context);
                return context.ShouldStopEvaluation ? Symbol.Undefined : memberValue;
            }

            var targetJs = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var target = targetJs.ToObject();
            if (expression.IsOptional && IsNullish(target))
            {
                return Symbol.Undefined;
            }

            if (IsNullish(target) && HasOptionalChaining(expression.Target))
            {
                return Symbol.Undefined;
            }

            if (IsNullish(target))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Cannot read properties of null or undefined",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                return Symbol.Undefined;
            }

            var propertyValueJs = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var propertyName = JsOps.GetRequiredPropertyName(propertyValueJs.ToObject(), context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (expression.IsComputed || !propertyName.IsPrivateName())
            {
                if (JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context))
                {
                    return context.ShouldStopEvaluation ? Symbol.Undefined : directValue;
                }

                return Symbol.Undefined;
            }

            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: !expression.IsComputed);
            var value = handle.GetValue();
            return context.ShouldStopEvaluation ? Symbol.Undefined : value;
        }

        private (object? Value, SuperBinding Binding) ResolveSuperMember(JsEnvironment environment,
            EvaluationContext context)
        {
            // Per ES spec 12.3.5.3 MakeSuperPropertyReference:
            // 3. Let actualThis be ? env.GetThisBinding().
            // This throws ReferenceError if this is uninitialized
            if (!context.IsThisInitialized)
            {
                throw CreateSuperReferenceError(environment, context, null);
            }

            var binding = ExpectSuperBinding(environment, context);

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
                return (Symbol.Undefined, binding);
            }

            var propertyValueJs = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, binding);
            }

            // Use JsOps.GetRequiredPropertyName which properly handles errors from ToPropertyName
            // (e.g., when toString() throws during property key coercion)
            var propertyName = JsOps.GetRequiredPropertyName(propertyValueJs.ToObject(), context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, binding);
            }

            if (!binding.TryGetProperty(propertyName, out var value))
            {
                return (Symbol.Undefined, binding);
            }

            return (value, binding);
        }
    }
}
