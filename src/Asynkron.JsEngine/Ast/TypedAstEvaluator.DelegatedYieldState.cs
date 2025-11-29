using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class DelegatedYieldState
    {
        private readonly IEnumerator<object?>? _enumerator;
        private readonly bool _isGeneratorObject;
        private readonly JsObject? _iterator;

        private DelegatedYieldState(JsObject? iterator, IEnumerator<object?>? enumerator, bool isGeneratorObject)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _isGeneratorObject = isGeneratorObject;
        }

        public static DelegatedYieldState FromIterator(JsObject iterator)
        {
            return new DelegatedYieldState(iterator, null, IsGeneratorObject(iterator));
        }

        public static DelegatedYieldState FromEnumerable(IEnumerable<object?> enumerable)
        {
            return new DelegatedYieldState(null, enumerable.GetEnumerator(), false);
        }

        public (object? Value, bool Done, bool IsDelegatedCompletion, bool PropagateThrow) MoveNext(
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
                    methodInvoked = TryInvokeIteratorMethod(
                        _iterator,
                        "throw",
                        sendValue ?? Symbol.Undefined,
                        context,
                        out candidate);
                }
                else if (propagateReturn)
                {
                    methodInvoked = TryInvokeIteratorMethod(
                        _iterator,
                        "return",
                        sendValue ?? Symbol.Undefined,
                        context,
                        out candidate);
                }
                else
                {
                    candidate = InvokeIteratorNext(_iterator, sendValue, hasSendValue);
                }

                if (!methodInvoked && candidate is null)
                {
                    return (Symbol.Undefined, true, propagateThrow, propagateThrow);
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
                        return (Symbol.Undefined, true, true, propagateThrow);
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
                           doneValue is bool and true;
                var value = nextResult.TryGetProperty("value", out var yielded)
                    ? yielded
                    : Symbol.Undefined;
                var delegatedCompletion = _isGeneratorObject && (propagateThrow || propagateReturn);
                var propagateThrowResult = _isGeneratorObject && propagateThrow && done;
                return (value, done, delegatedCompletion, propagateThrowResult);
            }

            if (_enumerator is null)
            {
                if (propagateThrow)
                {
                    throw new ThrowSignal(sendValue);
                }

                return (Symbol.Undefined, true, propagateReturn, false);
            }

            if (propagateThrow)
            {
                throw new ThrowSignal(sendValue);
            }

            if (propagateReturn)
            {
                return (sendValue, true, true, false);
            }

            if (!_enumerator.MoveNext())
            {
                return (Symbol.Undefined, true, false, false);
            }

            return (_enumerator.Current, false, false, false);
        }

        private static bool IsGeneratorObject(JsObject iterator)
        {
            return iterator.TryGetProperty(GeneratorBrandPropertyName, out var brand) &&
                   ReferenceEquals(brand, GeneratorBrandMarker);
        }

    }
}
