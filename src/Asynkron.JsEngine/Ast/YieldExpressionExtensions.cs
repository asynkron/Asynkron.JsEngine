using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(YieldExpression expression)
    {
        private JsValue EvaluateYield(JsEnvironment environment,
            EvaluationContext context)
        {
            return expression.IsDelegated
                ? expression.EvaluateDelegatedYield(environment, context)
                : expression.EvaluateSimpleYield(environment, context);
        }

        private JsValue EvaluateSimpleYield(JsEnvironment environment,
            EvaluationContext context)
        {
            var logger = environment.RealmState?.Logger;
            var yieldTracker = environment.GetYieldTracker();
            var shouldYield = yieldTracker.ShouldYield(out var yieldIndex);
            if (!shouldYield)
            {
                var payload = environment.GetResumePayload(yieldIndex);
                if (!payload.HasValue)
                {
                    logger?.LogInformation("Yield skip without payload index={Index}", yieldIndex);
                    return JsValue.Undefined;
                }

                logger?.LogInformation(
                    "Yield skip uses payload index={Index} throw={Throw} return={Return} type={Type}",
                    yieldIndex,
                    payload.IsThrow,
                    payload.IsReturn,
                    payload.Value?.GetType().Name ?? "null");

                // payload.Value might be a boxed JsValue (from resumeValue.ToObject())
                var payloadValueJs = payload.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(payload.Value);

                if (payload.IsThrow)
                {
                    context.SetThrow(payloadValueJs);
                    return payloadValueJs;
                }

                if (!payload.IsReturn)
                {
                    return payloadValueJs;
                }

                context.SetReturn(payloadValueJs);
                return payloadValueJs;

            }

            var yieldedValueJs = expression.Expression is null
                ? JsValue.Undefined
                : expression.Expression.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return yieldedValueJs;
            }

            context.SetYield(yieldedValueJs, yieldIndex);
            yieldTracker.MarkConsumed(yieldIndex);
            return yieldedValueJs;
        }

        private JsValue EvaluateDelegatedYield(JsEnvironment environment,
            EvaluationContext context)
        {
            if (expression.Expression is null)
            {
                throw new InvalidOperationException("yield* requires an expression.");
            }

            var stateKey = expression.GetDelegatedStateKey();
            var state = stateKey.GetDelegatedState(environment);

                if (state is null)
                {
                    var iterableJs = expression.Expression.EvaluateExpression(environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return iterableJs;
                    }

                    state = CreateDelegatedState(iterableJs.ToObject(), context);
                    if (context.ShouldStopEvaluation)
                    {
                        return context.FlowValue;
                    }

                    stateKey.StoreDelegatedState(environment, state);
                }

            var tracker = environment.GetYieldTracker();
            object? pendingSend = null;
            var hasPendingSend = false;
            var pendingThrow = false;
            var pendingReturn = false;

            // Check for throw/return payload BEFORE first fetch.
            // This is needed because on resume, we might need to call throw/return
            // on the inner iterator instead of next.
            if (!tracker.ShouldYield(out var initialYieldIndex))
            {
                var initialPayload = environment.GetResumePayload(initialYieldIndex);
                if (initialPayload.HasValue)
                {
                    if (initialPayload.IsThrow)
                    {
                        pendingThrow = true;
                        pendingSend = initialPayload.Value;
                        hasPendingSend = true;
                    }
                    else if (initialPayload.IsReturn)
                    {
                        pendingReturn = true;
                        pendingSend = initialPayload.Value;
                        hasPendingSend = true;
                    }
                    // For normal send values, we don't set flags here.
                    // They'll be processed when we reach the right yield point.
                }
            }

            while (true)
            {
                // Use GetOrFetchNext which returns cached result if available,
                // or advances the iterator if not. This prevents skipping values
                // when resuming a generator that has already yielded.
                // pendingSend might be a boxed JsValue from ResumePayload
                var pendingSendJs = pendingSend is JsValue psJs ? psJs : JsValue.FromObjectUnsafe(pendingSend);
                var iteratorResult = state.GetOrFetchNext(pendingSendJs,
                    hasPendingSend && !pendingThrow && !pendingReturn,
                    pendingThrow,
                    pendingReturn,
                    context,
                    out var awaitedPromise);

                if (awaitedPromise && context.IsThrow)
                {
                    return JsValue.Undefined;
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
                        stateKey.ClearDelegatedState(environment);
                        return iteratorResult.Value;
                    }

                    // For return propagation (when inner iterator has no return method),
                    // signal a return completion to the outer generator
                    stateKey.ClearDelegatedState(environment);
                    context.SetReturn(iteratorResult.Value);
                    return iteratorResult.Value;
                }

                var (value, done) = (iteratorResult.Value, iteratorResult.Done);
                if (done)
                {
                    stateKey.ClearDelegatedState(environment);
                    return value;
                }

                if (!tracker.ShouldYield(out var yieldIndex))
                {
                    var payload = environment.GetResumePayload(yieldIndex);
                    if (!payload.HasValue)
                    {
                        // We're skipping this yield point (already consumed).
                        // DO NOT consume the cached iterator result - this yield point
                        // was already yielded on a previous .next() call, so we just
                        // skip the yield tracker check and loop again with the SAME value.
                        // The next ShouldYield call will check the next yield index.
                        continue;
                    }

                    if (payload.IsThrow)
                    {
                        pendingSend = payload.Value;
                        hasPendingSend = true;
                        pendingThrow = true;
                        // Consume because we're passing a value to the inner iterator
                        state.ConsumeCachedResult();
                        continue;
                    }

                    if (payload.IsReturn)
                    {
                        pendingSend = payload.Value;
                        hasPendingSend = true;
                        pendingReturn = true;
                        // Consume because we're passing a value to the inner iterator
                        state.ConsumeCachedResult();
                        continue;
                    }

                    // Normal resume with .next(value) - for non-generator iterators like arrays,
                    // the send value is ignored. DO NOT consume the cache or set hasPendingSend.
                    // Just continue to find the next yield point that should actually yield.
                    // The cached value remains valid for the next iteration.
                    continue;
                }

                // We're yielding this value - consume the cached result and yield
                state.ConsumeCachedResult();
                // Use SetYieldWithIteratorResult to preserve the original iterator result object
                // This ensures that if the inner iterator returns {value: 1} without done,
                // the outer generator returns that same object instead of creating a new one with done: false
                context.SetYieldWithIteratorResult(value, yieldIndex, iteratorResult.IteratorResultObject);
                tracker.MarkConsumed(yieldIndex);
                // value is already JsValue from iteratorResult.Value
                return value;
            }
        }

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
