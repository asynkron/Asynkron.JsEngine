using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class DelegatedYieldState
    {
        private readonly IEnumerator<object?>? _enumerator;
        private readonly bool _isGeneratorObject;
        private readonly JsObject? _iterator;
        private IJsCallable? _nextMethod;

        // Cached result from the last MoveNext call that hasn't been consumed yet
        private (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, JsObject? IteratorResultObject)? _cachedResult;
        private bool _hasCachedResult;

        private DelegatedYieldState(JsObject? iterator, IEnumerator<object?>? enumerator, bool isGeneratorObject)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _isGeneratorObject = isGeneratorObject;
        }

        /// <summary>
        /// Returns the cached result without advancing the iterator, or null if no cached result.
        /// </summary>
        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, JsObject? IteratorResultObject)? PeekCachedResult()
        {
            return _hasCachedResult ? _cachedResult : null;
        }

        /// <summary>
        /// Clears the cached result, signaling that the value has been consumed.
        /// </summary>
        public void ConsumeCachedResult()
        {
            _hasCachedResult = false;
            _cachedResult = null;
        }

        /// <summary>
        /// Gets the next result, either from cache or by advancing the iterator.
        /// The result is cached until ConsumeResult is called.
        /// </summary>
        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, JsObject? IteratorResultObject) GetOrFetchNext(
            object? sendValue,
            bool hasSendValue,
            bool propagateThrow,
            bool propagateReturn,
            EvaluationContext context,
            out bool awaitedPromise)
        {
            // If we have a pending send value (throw/return), we always need to advance
            // because the send value needs to be passed to the inner iterator
            if (!_hasCachedResult || hasSendValue || propagateThrow || propagateReturn)
            {
                var result = MoveNext(sendValue, hasSendValue, propagateThrow, propagateReturn, context, out awaitedPromise);
                _cachedResult = result;
                _hasCachedResult = true;
                return result;
            }

            awaitedPromise = false;
            return _cachedResult!.Value;
        }

        public static DelegatedYieldState FromIterator(JsObject iterator)
        {
            return new DelegatedYieldState(iterator, null, IsGeneratorObject(iterator));
        }

        public static DelegatedYieldState FromEnumerable(IEnumerable<object?> enumerable)
        {
            return new DelegatedYieldState(null, enumerable.GetEnumerator(), false);
        }

        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, JsObject? IteratorResultObject) MoveNext(
            object? sendValue,
            bool hasSendValue,
            bool propagateThrow,
            bool propagateReturn,
            EvaluationContext context,
            out bool awaitedPromise)
        {
            awaitedPromise = false;
            if (_iterator is not null)
            {
                JsObject? nextResult;
                object? candidate = null;
                var methodInvoked = false;
                if (propagateThrow)
                {
                    methodInvoked = _iterator.TryInvokeIteratorMethod(
                        "throw",
                        sendValue ?? Symbol.Undefined,
                        context,
                        out candidate);
                }
                else if (propagateReturn)
                {
                    methodInvoked = _iterator.TryInvokeIteratorMethod(
                        "return",
                        sendValue ?? Symbol.Undefined,
                        context,
                        out candidate);
                }
                else
                {
                    _nextMethod ??= _iterator.GetIteratorNextCallable(context);
                    candidate = _iterator.InvokeIteratorNext(_nextMethod, sendValue, hasSendValue, context);
                }

                // Per spec: if return method is undefined, return Completion(received)
                // This means we should signal immediate completion to the outer generator
                if (propagateReturn && !methodInvoked)
                {
                    // Inner iterator has no return method - signal delegated completion
                    // The outer generator should complete with the received value
                    return (sendValue, true, true, false, null);
                }

                if (!methodInvoked && candidate is null)
                {
                    return (Symbol.Undefined, true, propagateThrow, propagateThrow, null);
                }

                if (methodInvoked && candidate is null)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                var nextCandidate = candidate ?? throw new InvalidOperationException("Iterator result missing.");
                object? awaitedCandidate;
                if (nextCandidate is JsObject promiseCandidate && IsPromiseLike(promiseCandidate))
                {
                    awaitedPromise = true;
                    if (!AwaitScheduler.TryAwaitPromiseSync(promiseCandidate, context, out awaitedCandidate))
                    {
                        return (Symbol.Undefined, true, true, propagateThrow, null);
                    }
                }
                else
                {
                    awaitedCandidate = nextCandidate;
                }

                if (awaitedCandidate is not JsObject resolvedObject)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                nextResult = resolvedObject;

                var done = nextResult.TryGetProperty("done", out var doneValue) &&
                           JsOps.ToBoolean(doneValue);
                var value = nextResult.TryGetProperty("value", out var yielded)
                    ? yielded
                    : Symbol.Undefined;
                var delegatedCompletion = _isGeneratorObject && (propagateThrow || propagateReturn);
                var propagateThrowResult = _isGeneratorObject && propagateThrow && done;
                return (value, done, delegatedCompletion, propagateThrowResult, nextResult);
            }

            if (_enumerator is null)
            {
                if (propagateThrow)
                {
                    throw new ThrowSignal(sendValue);
                }

                return (Symbol.Undefined, true, propagateReturn, false, null);
            }

            if (propagateThrow)
            {
                throw new ThrowSignal(sendValue);
            }

            if (propagateReturn)
            {
                return (sendValue, true, true, false, null);
            }

            if (!_enumerator.MoveNext())
            {
                return (Symbol.Undefined, true, false, false, null);
            }

            return (_enumerator.Current, false, false, false, null);
        }

        private static bool IsGeneratorObject(JsObject iterator)
        {
            return iterator.TryGetProperty(GeneratorBrandPropertyName, out var brand) &&
                   ReferenceEquals(brand, GeneratorBrandMarker);
        }
    }
}
