using Asynkron.JsEngine.JsTypes;
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
                    "iterator" => TypedAstSymbol.For("Symbol.iterator"),
                    "asyncIterator" => TypedAstSymbol.For("Symbol.asyncIterator"),
                    "toStringTag" => TypedAstSymbol.For("Symbol.toStringTag"),
                    _ => EvaluateDefaultMember(expression, environment, context)
                };
            }

            return EvaluateDefaultMember(expression, environment, context);
        }

        private object? EvaluateDefaultMember(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Target is SuperExpression)
            {
                var (memberValue, _) = ResolveSuperMember(expression, environment, context);
                return context.ShouldStopEvaluation ? Symbol.Undefined : memberValue;
            }

            var target = EvaluateExpression(expression.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (expression.IsOptional && IsNullish(target))
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

            var propertyValue = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var isPrivateName = propertyName.Length > 0 && propertyName[0] == '#';
            PrivateNameScope? privateScopeForAccess = null;
            if (isPrivateName)
            {
                privateScopeForAccess = context.CurrentPrivateNameScope;
                if (privateScopeForAccess is null)
                {
                    PrivateNameScope.TryResolveScope(propertyName, out privateScopeForAccess);
                }

                if (privateScopeForAccess is null)
                {
                    throw StandardLibrary.ThrowTypeError("Invalid access of private member", context,
                        context.RealmState);
                }

                if (!propertyName.Contains("@", StringComparison.Ordinal))
                {
                    propertyName = privateScopeForAccess.GetKey(propertyName);
                }

                var brandToken = privateScopeForAccess.BrandToken;
                if (target is not IPrivateBrandHolder brandHolder || !brandHolder.HasPrivateBrand(brandToken))
                {
                    throw StandardLibrary.ThrowTypeError("Invalid access of private member", context,
                        context.RealmState);
                }
            }

            if (TryGetPropertyValue(target, propertyName, out var value, context))
            {
                return context.ShouldStopEvaluation ? Symbol.Undefined : value;
            }

            if (privateScopeForAccess is not null)
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
            }

            return Symbol.Undefined;
        }

        private (object? Value, SuperBinding Binding) ResolveSuperMember(JsEnvironment environment,
            EvaluationContext context)
        {
            if (!context.IsThisInitialized)
            {
                throw CreateSuperReferenceError(environment, context, null);
            }

            var binding = ExpectSuperBinding(environment, context);
            var propertyValue = EvaluateExpression(expression.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, binding);
            }

            var propertyName = ToPropertyName(propertyValue, context)
                               ?? throw new InvalidOperationException(
                                   $"Property name cannot be null.{GetSourceInfo(context, expression.Source)}");

            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, binding);
            }

            if (!binding.TryGetProperty(propertyName, out var value))
            {
                throw new InvalidOperationException(
                    $"Cannot read property '{propertyName}' from super prototype.{GetSourceInfo(context, expression.Source)}");
            }

            return (value, binding);
        }
    }
}
