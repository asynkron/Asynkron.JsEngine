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
                AssignmentReference? preResolvedReference = null;
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

                if (mode == BindingMode.Assign && property.Target is AssignmentTargetBinding assignmentTarget)
                {
                    preResolvedReference = AssignmentReferenceResolver.ResolveForDestructuring(
                        assignmentTarget.Expression,
                        environment,
                        context,
                        EvaluateExpression);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (property.Target is IdentifierBinding identifierForSideEffects)
                {
                    // Resolve the binding reference before touching the source value so proxies
                    // used in with-environments observe the same lookup order as the spec.
                    _ = environment.HasBinding(identifierForSideEffects.Name);
                }

                usedKeys.Add(propertyName);
                var hasProperty = JsOps.TryGetPropertyValue(obj, propertyName, out var val, context);
                if (context.ShouldStopEvaluation)
                {
                    throw new ThrowSignal(context.FlowValue);
                }

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

                var skipBlockedLookup = mode == BindingMode.DefineVar &&
                                        property.Target is IdentifierBinding;

                if (preResolvedReference is { } resolvedReference)
                {
                    resolvedReference.SetValue(propertyValue);
                }
                else
                {
                    ApplyBindingTarget(property.Target, propertyValue, environment, context, mode,
                        allowNameInference: false, skipBlockedBindingLookup: skipBlockedLookup);
                }
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

            foreach (var key in obj.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
            {
                if (usedKeys.Contains(key))
                {
                    continue;
                }

                var descriptor = obj.GetOwnPropertyDescriptor(key);
                if (descriptor is not { Enumerable: true })
                {
                    continue;
                }

                if (JsOps.TryGetPropertyValue(obj, key, out var restValue, context))
                {
                    restObject.SetProperty(key, restValue);
                }
                else if (context.ShouldStopEvaluation)
                {
                    throw new ThrowSignal(context.FlowValue);
                }
            }

            ApplyBindingTarget(binding.RestElement, restObject, environment, context, mode, allowNameInference: false);
        }
    }
}
