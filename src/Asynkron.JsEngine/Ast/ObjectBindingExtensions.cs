using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ObjectBinding binding)
    {
        private void BindObjectPattern(object? /* intentional */ value, JsEnvironment environment,
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
                    var propertyKeyValueJs = property.NameExpression.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }

                    propertyName = JsOps.GetRequiredPropertyName(propertyKeyValueJs, context);
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
                        (e, env, ctx) => e.EvaluateExpression(env, ctx));
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

                var propertyValueJs = hasProperty
                    ? (val is null ? JsValue.Null : JsValue.FromObjectUnsafe(val))
                    : JsValue.Undefined;

                var usedDefault = false;
                if (propertyValueJs.IsUndefined && property.DefaultValue is not null)
                {
                    usedDefault = true;
                    propertyValueJs = property.DefaultValue.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }

                if (usedDefault &&
                    property is { Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression } && defaultExpression.IsAnonymousFunctionDefinition() &&
                    propertyValueJs.TryGetObject<IFunctionNameTarget>(out var nameTarget))
                {
                    nameTarget.EnsureHasName(identifierTarget.Name.Name);
                }

                var skipBlockedLookup = mode == BindingMode.DefineVar &&
                                        property.Target is IdentifierBinding;

                if (preResolvedReference is { } resolvedReference)
                {
                    resolvedReference.SetValue(propertyValueJs);
                }
                else
                {
                    property.Target.ApplyBindingTargetJsValue(propertyValueJs, environment, context, mode,
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
                    restObject.SetProperty(key, JsValue.FromObjectUnsafe(restValue));
                }
                else if (context.ShouldStopEvaluation)
                {
                    throw new ThrowSignal(context.FlowValue);
                }
            }

            binding.RestElement.ApplyBindingTargetJsValue(JsValue.FromObjectUnsafe(restObject), environment, context, mode, allowNameInference: false);
        }
    }
}
