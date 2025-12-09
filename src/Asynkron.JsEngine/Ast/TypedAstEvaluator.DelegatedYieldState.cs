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
        private readonly IJsObjectLike? _iterator;
        private IJsCallable? _nextMethod;

        // Cached result from the last MoveNext call that hasn't been consumed yet
        private (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, IJsObjectLike? IteratorResultObject)? _cachedResult;
        private bool _hasCachedResult;

        private DelegatedYieldState(IJsObjectLike? iterator, IEnumerator<object?>? enumerator, bool isGeneratorObject)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _isGeneratorObject = isGeneratorObject;
        }

        /// <summary>
        /// Returns the cached result without advancing the iterator, or null if no cached result.
        /// </summary>
        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, IJsObjectLike? IteratorResultObject)? PeekCachedResult()
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
        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, IJsObjectLike? IteratorResultObject) GetOrFetchNext(
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

        public static DelegatedYieldState FromIterator(IJsObjectLike iterator)
        {
            return new DelegatedYieldState(iterator, null, IsGeneratorObject(iterator));
        }

        public static DelegatedYieldState FromEnumerable(IEnumerable<object?> enumerable)
        {
            return new DelegatedYieldState(null, enumerable.GetEnumerator(), false);
        }

        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, IJsObjectLike? IteratorResultObject) MoveNext(
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
                IJsObjectLike? nextResult;
                object? candidate = null;
                var methodInvoked = false;
                try
                {
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
                        // Per ES spec (14.4.14) and Node.js V8 behavior, inner iterator's next()
                        // should always be called with an argument - even if it's undefined.
                        // This ensures arguments.length === 1 inside the next() method.
                        // Use Symbol.Undefined for null sendValue to match JavaScript undefined semantics.
                        candidate = _iterator.InvokeIteratorNext(_nextMethod, sendValue ?? Symbol.Undefined, true, context);
                    }

                    // Per spec: if return method is undefined, return Completion(received)
                    // This means we should signal immediate completion to the outer generator
                    if (propagateReturn && !methodInvoked)
                    {
                        // Inner iterator has no return method - signal delegated completion
                        // The outer generator should complete with the received value
                        return (sendValue, true, true, false, null);
                    }

                    // Per spec: if throw method is null/undefined, call IteratorClose and throw TypeError
                    if (propagateThrow && !methodInvoked)
                    {
                        // Call IteratorClose before throwing - this calls the return method if it exists
                        _iterator.IteratorClose(context, preserveExistingThrow: false);

                        // Throw TypeError as per spec - this will be caught below and returned as delegated completion
                        throw StandardLibrary.ThrowTypeError(
                            "The iterator does not provide a 'throw' method.",
                            context,
                            context.RealmState);
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
                    if (nextCandidate is IJsObjectLike promiseCandidate && IsPromiseLike(promiseCandidate))
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

                    if (awaitedCandidate is not IJsObjectLike resolvedObject)
                    {
                        throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                    }

                    nextResult = resolvedObject;
                }
                catch (ThrowSignal signal)
                {
                    // Convert ThrowSignal to delegated completion so generator's try/catch can handle it
                    return (signal.ThrownValue, true, true, true, null);
                }

                // Use JsOps for context-aware property access to propagate getter errors
                var gotDone = JsOps.TryGetPropertyValue(nextResult, "done", out var doneValue, context);
                if (gotDone && context?.IsThrow == true)
                {
                    // Getter threw - return as delegated completion to be handled by generator's try/catch
                    return (context.FlowValue, true, true, true, null);
                }
                var done = gotDone && JsOps.ToBoolean(doneValue);
                // Per ES spec 14.4.14, only read `value` when iteration is complete (done is true).
                // When done is false, yield the innerResult directly without accessing `value`.
                // This is important because the spec says IteratorValue is only called when done is true.
                object? value;
                if (done)
                {
                    var gotValue = JsOps.TryGetPropertyValue(nextResult, "value", out var yielded, context);
                    if (gotValue && context?.IsThrow == true)
                    {
                        // Getter threw - return as delegated completion to be handled by generator's try/catch
                        return (context.FlowValue, true, true, true, null);
                    }
                    value = gotValue ? yielded : Symbol.Undefined;
                }
                else
                {
                    // Don't read `value` yet - it will be read lazily from the iterator result object
                    // if needed. Pass null here; the caller will use the nextResult object directly.
                    value = null;
                }
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

        private static bool IsGeneratorObject(IJsObjectLike iterator)
        {
            return iterator.TryGetProperty(GeneratorBrandPropertyName, out var brand) &&
                   ReferenceEquals(brand, GeneratorBrandMarker);
        }
    }
}
