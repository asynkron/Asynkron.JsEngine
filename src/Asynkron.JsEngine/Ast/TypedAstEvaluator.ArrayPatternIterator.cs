using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private struct ArrayPatternIterator
    {
        private readonly JsObject? _iterator;
        private readonly IEnumerator<object?>? _enumerator;
        private IJsCallable? _nextMethod;

        public ArrayPatternIterator(JsObject? iterator, IEnumerator<object?>? enumerator)
        {
            _iterator = iterator;
            _enumerator = enumerator;
            _nextMethod = null;
        }

        public (object? Value, bool Done) Next(EvaluationContext context)
        {
            if (_iterator is null)
            {
                return _enumerator?.MoveNext() != true ? (Symbol.Undefined, true) : (_enumerator.Current, false);
            }

            _nextMethod ??= _iterator.GetIteratorNextCallable(context);
            var candidate = _iterator.InvokeIteratorNext(_nextMethod, context: context);
            if (candidate is not JsObject result)
            {
                throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
            }

            var done = result.TryGetProperty("done", result, context, out var doneValue) &&
                       JsOps.ToBoolean(doneValue);

            if (done)
            {
                return (Symbol.Undefined, true);
            }

            var value = result.TryGetProperty("value", result, context, out var yielded)
                ? yielded
                : Symbol.Undefined;

            return (value, false);
        }
    }
}
