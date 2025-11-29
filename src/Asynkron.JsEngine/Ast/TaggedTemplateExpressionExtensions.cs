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

extension(TaggedTemplateExpression expression)
    {
        private object? EvaluateTaggedTemplate(JsEnvironment environment,
            EvaluationContext context)
        {
            var (tagValue, thisValue, skippedOptional) = EvaluateCallTarget(expression.Tag, environment, context);
            if (context.ShouldStopEvaluation || skippedOptional)
            {
                return Symbol.Undefined;
            }

            if (tagValue is not IJsCallable callable)
            {
                throw new InvalidOperationException("Tag in tagged template must be a function.");
            }

            var stringsArrayValue = EvaluateExpression(expression.StringsArray, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (stringsArrayValue is not JsArray stringsArray)
            {
                throw new InvalidOperationException("Tagged template strings array is invalid.");
            }

            var rawStringsArrayValue = EvaluateExpression(expression.RawStringsArray, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (rawStringsArrayValue is not JsArray rawStringsArray)
            {
                throw new InvalidOperationException("Tagged template raw strings array is invalid.");
            }

            var templateObject = CreateTemplateObject(stringsArray, rawStringsArray);

            var arguments = ImmutableArray.CreateBuilder<object?>(expression.Expressions.Length + 1);
            arguments.Add(templateObject);

            foreach (var expr in expression.Expressions)
            {
                arguments.Add(EvaluateExpression(expr, environment, context));
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
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
