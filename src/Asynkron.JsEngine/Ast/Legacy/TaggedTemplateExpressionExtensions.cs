#region

using System.Collections.Immutable;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTaggedTemplate(this TaggedTemplateExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var (tagValue, thisValue, skippedOptional) = expression.Tag.EvaluateCallTarget(environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsValue.Undefined;
        }

        if (!tagValue.TryGetObject<IJsCallable>(out var callable))
        {
            var error = StandardLibrary.CreateTypeError("Tag in tagged template must be a function.",
                context, environment.RealmState);
            throw new ThrowSignal(error);
        }

        // Per ES spec 13.2.8.4 GetTemplateObject, template objects are cached by parse node.
        // Check the realm's template cache first.
        var realmState = context.RealmState;
        JsArray templateObject;
        if (realmState.TemplateObjectCache.TryGetValue(expression, out var cachedTemplate))
        {
            templateObject = (JsArray)cachedTemplate;
        }
        else
        {
            var stringsArrayValueJs = EvaluateCachedExpressionProgram(
                expression.StringsArray,
                environment,
                context,
                "Dynamic tagged template cooked strings");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (!stringsArrayValueJs.TryGetObject<JsArray>(out var stringsArray))
            {
                throw new InvalidOperationException("Tagged template strings array is invalid.");
            }

            var rawStringsArrayValueJs = EvaluateCachedExpressionProgram(
                expression.RawStringsArray,
                environment,
                context,
                "Dynamic tagged template raw strings");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (!rawStringsArrayValueJs.TryGetObject<JsArray>(out var rawStringsArray))
            {
                throw new InvalidOperationException("Tagged template raw strings array is invalid.");
            }

            templateObject = (JsArray)stringsArray.CreateTemplateObject(rawStringsArray);

            // Cache the template object for subsequent calls to the same parse node
            realmState?.TemplateObjectCache[expression] = templateObject;
        }

        var arguments = ImmutableArray.CreateBuilder<JsValue>(expression.Expressions.Length + 1);
        arguments.Add(JsValue.FromJsArray(templateObject));

        foreach (var expr in expression.Expressions)
        {
            arguments.Add(EvaluateCachedExpressionProgram(
                expr,
                environment,
                context,
                "Dynamic tagged template argument"));
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
            // Use InvokeWithContext for SyncFunctionInvoker to ensure proper this coercion in non-strict mode.
            // The regular Invoke() passes null context, which can skip the coercion in some paths.
            if (callable is SyncFunctionInvoker typedFunction)
            {
                return typedFunction.InvokeWithContext(frozenArguments, thisValue, context);
            }

            if (callable is HostFunction hostFunction)
            {
                return hostFunction.InvokeWithContext(frozenArguments, thisValue, context);
            }

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
