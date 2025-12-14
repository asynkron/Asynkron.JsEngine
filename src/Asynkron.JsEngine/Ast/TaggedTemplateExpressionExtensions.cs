using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(TaggedTemplateExpression expression)
    {
        private JsValue EvaluateTaggedTemplate(JsEnvironment environment,
            EvaluationContext context)
        {
            var (tagValue, thisValue, skippedOptional) = EvaluateCallTarget(expression.Tag, environment, context);
            if (context.ShouldStopEvaluation || skippedOptional)
            {
                return JsValue.Undefined;
            }

            if (tagValue is not IJsCallable callable)
            {
                var error = StandardLibrary.CreateTypeError("Tag in tagged template must be a function.",
                    context, environment.RealmState);
                throw new ThrowSignal(error);
            }

            // Per ES spec 13.2.8.4 GetTemplateObject, template objects are cached by parse node.
            // Check the realm's template cache first.
            var realmState = context.RealmState;
            object templateObject;
            if (realmState is not null && realmState.TemplateObjectCache.TryGetValue(expression, out var cachedTemplate))
            {
                templateObject = cachedTemplate;
            }
            else
            {
                var stringsArrayValueJs = EvaluateExpression(expression.StringsArray, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                if (stringsArrayValueJs.ToObject() is not JsArray stringsArray)
                {
                    throw new InvalidOperationException("Tagged template strings array is invalid.");
                }

                var rawStringsArrayValueJs = EvaluateExpression(expression.RawStringsArray, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                if (rawStringsArrayValueJs.ToObject() is not JsArray rawStringsArray)
                {
                    throw new InvalidOperationException("Tagged template raw strings array is invalid.");
                }

                templateObject = CreateTemplateObject(stringsArray, rawStringsArray);

                // Cache the template object for subsequent calls to the same parse node
                realmState?.TemplateObjectCache[expression] = templateObject;
            }

            var arguments = ImmutableArray.CreateBuilder<JsValue>(expression.Expressions.Length + 1);
            arguments.Add(new JsValue(templateObject));

            foreach (var expr in expression.Expressions)
            {
                arguments.Add(EvaluateExpression(expr, environment, context));
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }
            }

            if (callable is IJsEnvironmentAwareCallable envAware)
            {
                envAware.CallingJsEnvironment = environment;
            }

            DebugAwareHostFunction? debugFunction = null;
            if (callable is DebugAwareHostFunction debugAware)
            {
                debugFunction = debugAware;
                debugFunction.CurrentJsEnvironment = environment;
                debugFunction.CurrentContext = context;
            }

            var frozenArguments = FreezeArguments(arguments);

            try
            {
                return callable.Invoke(frozenArguments, thisValue);
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                return signal.ThrownValue;
            }
            finally
            {
                if (debugFunction is not null)
                {
                    debugFunction.CurrentJsEnvironment = null;
                    debugFunction.CurrentContext = null;
                }
            }
        }
    }
}
