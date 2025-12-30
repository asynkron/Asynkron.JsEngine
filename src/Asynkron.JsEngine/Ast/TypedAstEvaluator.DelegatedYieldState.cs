#region

using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class DelegatedYieldState
    {
        private readonly IEnumerator<JsValue>? _enumerator;
        private readonly bool _isGeneratorObject;
        private readonly IJsObjectLike? _iterator;
        private IJsCallable? _nextMethod;

        private DelegatedYieldState(IJsObjectLike? iterator, IEnumerator<JsValue>? enumerator, bool isGeneratorObject)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _isGeneratorObject = isGeneratorObject;
        }

        public static DelegatedYieldState FromIterator(IJsObjectLike iterator)
        {
            return new DelegatedYieldState(iterator, null, IsGeneratorObject(iterator));
        }

        public static DelegatedYieldState FromEnumerable(IEnumerable<JsValue> enumerable)
        {
            return new DelegatedYieldState(null, enumerable.GetEnumerator(), false);
        }

        public (JsValue Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow, IJsObjectLike?
            IteratorResultObject) MoveNext(
                JsValue sendValue,
                bool propagateThrow,
                bool propagateReturn,
                EvaluationContext context,
                out bool awaitedPromise)
        {
            awaitedPromise = false;
            if (_iterator is not null)
            {
                IJsObjectLike? nextResult;
                var candidate = JsValue.Undefined;
                var methodInvoked = false;
                try
                {
                    if (propagateThrow)
                    {
                        methodInvoked = _iterator.TryInvokeIteratorMethod(
                            "throw",
                            sendValue.IsUndefined ? JsValue.Undefined : sendValue,
                            context,
                            out candidate);
                    }
                    else if (propagateReturn)
                    {
                        methodInvoked = _iterator.TryInvokeIteratorMethod(
                            "return",
                            sendValue.IsUndefined ? JsValue.Undefined : sendValue,
                            context,
                            out candidate);
                    }
                    else
                    {
                        _nextMethod ??= _iterator.GetIteratorNextCallable(context);
                        // Per ES spec (14.4.14) and Node.js V8 behavior, inner iterator's next()
                        // should always be called with an argument - even if it's undefined.
                        // This ensures arguments.length === 1 inside the next() method.
                        // Use JsValue.Undefined for undefined sendValue to match JavaScript undefined semantics.
                        candidate = _iterator.InvokeIteratorNext(_nextMethod,
                            sendValue.IsUndefined ? JsValue.Undefined : sendValue, true, context);
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
                        _iterator.IteratorClose(context, false);

                        // Throw TypeError as per spec - this will be caught below and returned as delegated completion
                        throw StandardLibrary.ThrowTypeError(
                            "The iterator does not provide a 'throw' method.",
                            context,
                            context.RealmState);
                    }

                    if (!methodInvoked && candidate.IsUndefined)
                    {
                        return (JsValue.Undefined, true, propagateThrow, propagateThrow, null);
                    }

                    if (methodInvoked && candidate.IsUndefined)
                    {
                        throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                    }

                    var nextCandidate = candidate.IsUndefined
                        ? throw new InvalidOperationException("Iterator result missing.")
                        : candidate;
                    JsValue awaitedCandidate;
                    if (nextCandidate.TryGetObject<IJsObjectLike>(out var promiseCandidate) &&
                        IsPromiseLike(nextCandidate))
                    {
                        awaitedPromise = true;
                        if (!AwaitScheduler.TryAwaitPromiseSync(
                                nextCandidate,
                                context,
                                out awaitedCandidate,
                                context.DrainAwaitMicrotasks))
                        {
                            return (JsValue.Undefined, true, true, propagateThrow, null);
                        }
                    }
                    else
                    {
                        awaitedCandidate = nextCandidate;
                    }

                    if (!awaitedCandidate.TryGetObject<IJsObjectLike>(out var resolvedObject))
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
                var nextResultJsValue = JsValue.FromObjectUnsafe(nextResult);
                var gotDone = JsOps.TryGetPropertyValue(nextResultJsValue, "done", out var doneValue, context);
                if (gotDone && context?.IsThrow == true)
                {
                    // Getter threw - return as delegated completion to be handled by generator's try/catch
                    return (context.FlowValue, true, true, true, null);
                }

                var done = gotDone && JsOps.ToBoolean(doneValue);
                // Per ES spec 14.4.14, only read `value` when iteration is complete (done is true).
                // When done is false, yield the innerResult directly without accessing `value`.
                // This is important because the spec says IteratorValue is only called when done is true.
                JsValue value;
                if (done)
                {
                    var gotValue = JsOps.TryGetPropertyValue(nextResultJsValue, "value", out var yielded, context);
                    if (gotValue && context?.IsThrow == true)
                    {
                        // Getter threw - return as delegated completion to be handled by generator's try/catch
                        return (context.FlowValue, true, true, true, null);
                    }

                    value = gotValue ? yielded : JsValue.Undefined;
                }
                else
                {
                    // Don't read `value` yet - it will be read lazily from the iterator result object
                    // if needed. Pass JsValue.Undefined here; the caller will use the nextResult object directly.
                    value = JsValue.Undefined;
                }

                // When we awaited a Promise from return() and got done: true, report done: false
                // to allow the generator to suspend and continue on the next call. The actual iterator
                // result object (nextResult) still has done: true so the caller sees the correct result.
                // On the next g.next() call, inner.next() will be called to continue iteration.
                var reportedDone = done;
                if (awaitedPromise && propagateReturn && done)
                {
                    reportedDone = false;
                }

                // Delegated completion occurs when:
                // 1. Inner iterator is a generator AND we propagated throw/return, OR
                // 2. We propagated return AND inner iterator completed (done: true) - even for non-generator iterators
                // Use reportedDone for delegated completion check to avoid early completion when Promise was awaited
                var delegatedCompletion = (_isGeneratorObject && (propagateThrow || propagateReturn)) ||
                                          (propagateReturn && reportedDone);
                var propagateThrowResult = propagateThrow && reportedDone;
                return (value, reportedDone, delegatedCompletion, propagateThrowResult, nextResult);
            }

            if (_enumerator is null)
            {
                if (propagateThrow)
                {
                    throw new ThrowSignal(sendValue);
                }

                return (JsValue.Undefined, true, propagateReturn, false, null);
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
                return (JsValue.Undefined, true, false, false, null);
            }

            return (_enumerator.Current, false, false, false, null);
        }

        private static bool IsGeneratorObject(IJsObjectLike iterator)
        {
            // brand is a JsValue - we need to unwrap it to compare with GeneratorBrandMarker
            // GeneratorBrandMarker is a plain System.Object, not a JsObject, so we compare ObjectValue directly
            return iterator.TryGetProperty(GeneratorBrandPropertyName, out var brand) &&
                   brand.Kind == JsValueKind.Object &&
                   ReferenceEquals(brand.ObjectValue, GeneratorBrandMarker);
        }
    }
}
