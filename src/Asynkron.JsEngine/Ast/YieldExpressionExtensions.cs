using Microsoft.Extensions.Logging;

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

        private object? EvaluateSimpleYield(JsEnvironment environment,
            EvaluationContext context)
        {
            var logger = environment.RealmState?.Logger;
            var yieldTracker = GetYieldTracker(environment);
            var shouldYield = yieldTracker.ShouldYield(out var yieldIndex);
            if (!shouldYield)
            {
                var payload = GetResumePayload(environment, yieldIndex);
                if (!payload.HasValue)
                {
                    logger?.LogInformation("Yield skip without payload index={Index}", yieldIndex);
                    return Symbol.Undefined;
                }

                logger?.LogInformation(
                    "Yield skip uses payload index={Index} throw={Throw} return={Return} type={Type}",
                    yieldIndex,
                    payload.IsThrow,
                    payload.IsReturn,
                    payload.Value?.GetType().Name ?? "null");

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

            var yieldedValue = expression.Expression is null
                ? Symbol.Undefined
                : EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return yieldedValue;
            }

            context.SetYield(yieldedValue, yieldIndex);
            yieldTracker.MarkConsumed(yieldIndex);
            return yieldedValue;
        }

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

                    state = CreateDelegatedState(iterable, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return context.FlowValue;
                    }

                    StoreDelegatedState(stateKey, environment, state);
                }

            var tracker = GetYieldTracker(environment);
            object? pendingSend = null;
            var hasPendingSend = false;
            var pendingThrow = false;
            var pendingReturn = false;

            // Check for throw/return payload BEFORE first fetch.
            // This is needed because on resume, we might need to call throw/return
            // on the inner iterator instead of next.
            if (!tracker.ShouldYield(out var initialYieldIndex))
            {
                var initialPayload = GetResumePayload(environment, initialYieldIndex);
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
                var iteratorResult = state.GetOrFetchNext(pendingSend,
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

                    // For return propagation (when inner iterator has no return method),
                    // signal a return completion to the outer generator
                    ClearDelegatedState(stateKey, environment);
                    context.SetReturn(iteratorResult.Value);
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
