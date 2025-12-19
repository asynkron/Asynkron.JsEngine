using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(IJsCallable targetFunction)
    {
        private JsValue InvokeWithApply(ImmutableArray<CallArgument> callArguments,
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

                foreach (var item in EnumerateSpread(argsArrayJs, context))
                {
                    argsBuilder.Add(item);
                }

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
            if (targetFunction is TypedFunction typedFunction)
            {
                return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
            }

            return targetFunction.Invoke(frozenArguments, thisArg);
        }

        private JsValue InvokeWithCall(ImmutableArray<CallArgument> callArguments,
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
            if (targetFunction is TypedFunction typedFunction)
            {
                return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
            }

            return targetFunction.Invoke(frozenArguments, thisArg);
        }
    }
}
