#region

using System.Collections.Immutable;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue InvokeWithApply(this IJsCallable targetFunction, ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var thisArg = JsValue.Undefined;
        if (callArguments.Length > 0)
        {
            thisArg = callArguments[0].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        var argsBuilder = ImmutableArray.CreateBuilder<JsValue>();
        if (callArguments.Length > 1)
        {
            var argsArrayJs = callArguments[1].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            argsBuilder.AddRange(EnumerateSpread(argsArrayJs, context));

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        if (targetFunction is SyncFunctionInvoker typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static JsValue InvokeWithCall(this IJsCallable targetFunction, ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var thisArg = JsValue.Undefined;
        var argsBuilder = ImmutableArray.CreateBuilder<JsValue>();

        for (var i = 0; i < callArguments.Length; i++)
        {
            var argValue = callArguments[i].Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (i == 0)
            {
                thisArg = argValue;
            }
            else
            {
                argsBuilder.Add(argValue);
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        if (targetFunction is SyncFunctionInvoker typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }
}
