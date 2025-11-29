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

extension(YieldExpression expression)
    {
        private object? EvaluateYield(JsEnvironment environment,
            EvaluationContext context)
        {
            return expression.IsDelegated
                ? EvaluateDelegatedYield(expression, environment, context)
                : EvaluateSimpleYield(expression, environment, context);
        }
    }

extension(YieldExpression expression)
    {
        private object? EvaluateSimpleYield(JsEnvironment environment,
            EvaluationContext context)
        {
            var yieldedValue = expression.Expression is null
                ? Symbol.Undefined
                : EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return yieldedValue;
            }

            var yieldTracker = GetYieldTracker(environment);
            if (!yieldTracker.ShouldYield(out var yieldIndex))
            {
                var payload = GetResumePayload(environment, yieldIndex);
                if (!payload.HasValue)
                {
                    return Symbol.Undefined;
                }

                if (payload.IsThrow)
                {
                    context.SetThrow(payload.Value);
                    return payload.Value;
                }

                if (payload.IsReturn)
                {
                    context.SetReturn(payload.Value);
                    return payload.Value;
                }

                return payload.Value;
            }

            context.SetYield(yieldedValue);
            return yieldedValue;
        }
    }

extension(YieldExpression expression)
    {
        private object? EvaluateDelegatedYield(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Expression is null)
            {
                throw new InvalidOperationException("yield* requires an expression.");
            }

            var stateKey = GetDelegatedStateKey(expression);
            var state = GetDelegatedState(stateKey, environment);

            if (state is null)
            {
                var iterable = EvaluateExpression(expression.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return iterable;
                }

                state = CreateDelegatedState(iterable);
                StoreDelegatedState(stateKey, environment, state);
            }

            var tracker = GetYieldTracker(environment);
            object? pendingSend = null;
            var hasPendingSend = false;
            var pendingThrow = false;
            var pendingReturn = false;

            while (true)
            {
                var iteratorResult = state.MoveNext(pendingSend,
                    hasPendingSend && !pendingThrow && !pendingReturn,
                    pendingThrow,
                    pendingReturn,
                    context,
                    out var awaitedPromise);

                if (awaitedPromise && context.IsThrow)
                {
                    return Symbol.Undefined;
                }

                pendingSend = null;
                hasPendingSend = false;
                pendingThrow = false;
                pendingReturn = false;

                if (iteratorResult.IsDelegatedCompletion)
                {
                    if (iteratorResult.PropagateThrow)
                    {
                        context.SetThrow(iteratorResult.Value);
                        ClearDelegatedState(stateKey, environment);
                        return iteratorResult.Value;
                    }

                    ClearDelegatedState(stateKey, environment);
                    return iteratorResult.Value;
                }

                var (value, done) = (iteratorResult.Value, iteratorResult.Done);
                if (done)
                {
                    ClearDelegatedState(stateKey, environment);
                    return value;
                }

                if (!tracker.ShouldYield(out var yieldIndex))
                {
                    var payload = GetResumePayload(environment, yieldIndex);
                    if (!payload.HasValue)
                    {
                        continue;
                    }

                    if (payload.IsThrow)
                    {
                        pendingSend = payload.Value;
                        hasPendingSend = true;
                        pendingThrow = true;
                        continue;
                    }

                    if (payload.IsReturn)
                    {
                        pendingSend = payload.Value;
                        hasPendingSend = true;
                        pendingReturn = true;
                        continue;
                    }

                    pendingSend = payload.Value;
                    hasPendingSend = true;
                    continue;
                }

                context.SetYield(value);
                return value;
            }
        }
    }

extension(YieldExpression expression)
    {
        private Symbol? GetDelegatedStateKey()
        {
            if (expression.Source is null)
            {
                return null;
            }

            var key = $"__yield_delegate_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
            return Symbol.Intern(key);
        }
    }

}
