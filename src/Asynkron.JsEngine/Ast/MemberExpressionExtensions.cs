using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(MemberExpression expression)
    {
        private JsValue EvaluateMember(JsEnvironment environment,
            EvaluationContext context)
        {
            // Fast-path well-known symbol properties so expressions like
            // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
            // values that can be used as keys (e.g. o[Symbol.iterator]).
            if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
                string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
                expression.Property is LiteralExpression { Value.IsString: true } symbolPropLit)
            {
                var symbolProp = symbolPropLit.Value.AsString()!;
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

        private JsValue EvaluateDefaultMember(JsEnvironment environment,
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
                var (memberValue, _) = expression.ResolveSuperMember(environment, context);
                return context.ShouldStopEvaluation ? JsValue.Undefined : memberValue;
            }

            var targetJs = expression.Target.EvaluateExpression(environment, context);
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
                context.SetThrow(JsValue.FromObjectUnsafe(error));
                return JsValue.Undefined;
            }

            // Extract the value for property access - primitives need boxing for prototype chain lookup
            var target = targetJs.Kind switch
            {
                JsValueKind.Boolean => (object?)(targetJs.NumberValue != 0),
                JsValueKind.Number => targetJs.NumberValue,
                JsValueKind.String => targetJs.ObjectValue,
                JsValueKind.Symbol => targetJs.ObjectValue,
                JsValueKind.BigInt => targetJs.ObjectValue,
                JsValueKind.Object => targetJs.ObjectValue,
                _ => targetJs.ObjectValue
            };

            // Fast path: for non-computed member access with literal string property,
            // skip expression evaluation and use the property name directly
            string? propertyName;
            if (!expression.IsComputed && expression.Property is LiteralExpression { Value.IsString: true } literalProp)
            {
                propertyName = literalProp.Value.AsString();
            }
            else
            {
                var propertyValueJs = expression.Property.EvaluateExpression(environment, context);
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

            if (propertyName is null)
            {
                return JsValue.Undefined;
            }

            if (expression.IsComputed || !propertyName.IsPrivateName())
            {
                if (JsOps.TryGetPropertyValue(target, propertyName, out var directValue, context))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    // Handle case where value is already a boxed JsValue
                    return directValue is JsValue jsVal ? jsVal : JsValue.FromObjectUnsafe(directValue);
                }

                return JsValue.Undefined;
            }

            var handle = PropertyHandle.Resolve(
                target,
                propertyName,
                context,
                context.CurrentScope.IsStrict,
                allowPrivate: !expression.IsComputed);
            var value = handle.GetJsValue();
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return value;
        }

        private (JsValue Value, SuperBinding Binding) ResolveSuperMember(JsEnvironment environment,
            EvaluationContext context)
        {
            // Per ES spec 12.3.5.3 MakeSuperPropertyReference:
            // 3. Let actualThis be ? env.GetThisBinding().
            // This throws ReferenceError if this is uninitialized
            if (!context.IsThisInitialized)
            {
                throw environment.CreateSuperReferenceError(context, null);
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
                context.SetThrow(JsValue.FromObjectUnsafe(error));
                return (JsValue.Undefined, binding);
            }

            var propertyValueJs = expression.Property.EvaluateExpression(environment, context);
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
}
