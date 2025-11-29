using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(IJsCallable targetFunction)
    {
        private object? InvokeWithApply(ImmutableArray<CallArgument> callArguments,
            JsEnvironment environment,
            EvaluationContext context)
        {
            object? thisArg = Symbol.Undefined;
            if (callArguments.Length > 0)
            {
                thisArg = EvaluateExpression(callArguments[0].Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
            }

            var argsBuilder = ImmutableArray.CreateBuilder<object?>();
            if (callArguments.Length > 1)
            {
                var argsArray = EvaluateExpression(callArguments[1].Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                foreach (var item in EnumerateSpread(argsArray, context))
                {
                    argsBuilder.Add(item);
                }
            }

            if (targetFunction is IJsEnvironmentAwareCallable envAware)
            {
                envAware.CallingJsEnvironment = environment;
            }

            var frozenArguments = FreezeArguments(argsBuilder);
            if (targetFunction is TypedFunction typedFunction)
            {
                return typedFunction.InvokeWithContext(frozenArguments, thisArg, context, newTarget: null);
            }

            return targetFunction.Invoke(frozenArguments, thisArg);
        }
    }

extension(IJsCallable targetFunction)
    {
        private object? InvokeWithCall(ImmutableArray<CallArgument> callArguments,
            JsEnvironment environment,
            EvaluationContext context)
        {
            object? thisArg = Symbol.Undefined;
            var argsBuilder = ImmutableArray.CreateBuilder<object?>();

            for (var i = 0; i < callArguments.Length; i++)
            {
                var argValue = EvaluateExpression(callArguments[i].Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
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
                return typedFunction.InvokeWithContext(frozenArguments, thisArg, context, newTarget: null);
            }

            return targetFunction.Invoke(frozenArguments, thisArg);
        }
    }

}
