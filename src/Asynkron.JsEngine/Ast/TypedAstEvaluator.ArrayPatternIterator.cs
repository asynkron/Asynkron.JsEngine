using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private struct ArrayPatternIterator(IJsObjectLike? iterator, IEnumerator<JsValue>? enumerator)
    {
        private IJsCallable? _nextMethod = null;

        public (JsValue Value, bool Done) Next(EvaluationContext context)
        {
            if (iterator is null)
            {
                return enumerator?.MoveNext() != true ? (JsValue.Undefined, true) : (enumerator.Current, false);
            }

            _nextMethod ??= iterator.GetIteratorNextCallable(context);
            var candidate = iterator.InvokeIteratorNext(_nextMethod, context: context);
            if (!candidate.TryGetObject<IJsObjectLike>(out var result))
            {
                throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
            }

            var done = JsOps.TryGetPropertyValue(result, "done", out var doneValue, context) &&
                       JsOps.ToBoolean(doneValue);

            if (done)
            {
                return (JsValue.Undefined, true);
            }

            var value = JsOps.TryGetPropertyValue(result, "value", out var yielded, context)
                ? JsValue.FromObject(yielded)
                : JsValue.Undefined;

            return (value, false);
        }
    }
}
