using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ObjectBinding binding)
    {
        private void BindObjectPattern(object? value, JsEnvironment environment,
            EvaluationContext context, BindingMode mode)
        {
            var obj = ToObjectForDestructuring(value, context);

            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in binding.Properties)
            {
                var propertyName = property.Name;
                if (property.NameExpression is not null)
                {
                    var propertyKeyValue = EvaluateExpression(property.NameExpression, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    propertyName = JsOps.GetRequiredPropertyName(propertyKeyValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                usedKeys.Add(propertyName);
                var hasProperty = obj.TryGetProperty(propertyName, out var val);
                var propertyValue = hasProperty ? val : Symbol.Undefined;

                var usedDefault = false;
                if (ReferenceEquals(propertyValue, Symbol.Undefined) && property.DefaultValue is not null)
                {
                    usedDefault = true;
                    propertyValue = EvaluateExpression(property.DefaultValue, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (usedDefault &&
                    property is { Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression } &&
                    IsAnonymousFunctionDefinition(defaultExpression) &&
                    propertyValue is IFunctionNameTarget nameTarget)
                {
                    nameTarget.EnsureHasName(identifierTarget.Name.Name);
                }

                ApplyBindingTarget(property.Target, propertyValue, environment, context, mode, allowNameInference: false);
            }

            if (binding.RestElement is null)
            {
                return;
            }

            var restObject = new JsObject();
            if (context.RealmState?.ObjectPrototype is not null)
            {
                restObject.SetPrototype(context.RealmState.ObjectPrototype);
            }
            foreach (var key in GetEnumerableOwnPropertyKeysInOrder(obj))
            {
                if (!usedKeys.Contains(key))
                {
                    if (obj.TryGetProperty(key, out var restValue))
                    {
                        restObject.SetProperty(key, restValue);
                    }
                }
            }

            ApplyBindingTarget(binding.RestElement, restObject, environment, context, mode, allowNameInference: false);
        }
    }

}
