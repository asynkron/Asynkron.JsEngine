using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private struct ArrayPatternIterator
    {
        private readonly IJsObjectLike? _iterator;
        private readonly IEnumerator<object?>? _enumerator;
        private IJsCallable? _nextMethod;

        public ArrayPatternIterator(IJsObjectLike? iterator, IEnumerator<object?>? enumerator)
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
            if (candidate is not IJsObjectLike result)
            {
                throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
            }

            var done = JsOps.TryGetPropertyValue(result, "done", out var doneValue, context) &&
                       JsOps.ToBoolean(doneValue);

            if (done)
            {
                return (Symbol.Undefined, true);
            }

            var value = JsOps.TryGetPropertyValue(result, "value", out var yielded, context)
                ? yielded
                : Symbol.Undefined;

            return (value, false);
        }
    }
}
