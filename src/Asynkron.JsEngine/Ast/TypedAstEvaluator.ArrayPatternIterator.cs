using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private readonly struct ArrayPatternIterator(JsObject? iterator, IEnumerator<object?>? enumerator)
    {
        public (object? Value, bool Done) Next(EvaluationContext context)
        {
            if (iterator is null)
            {
                return enumerator?.MoveNext() != true ? (Symbol.Undefined, true) : (enumerator.Current, false);
            }

            var candidate = InvokeIteratorNext(iterator);
            if (candidate is not JsObject result)
            {
                throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
            }

            var done = result.TryGetProperty("done", out var doneValue) &&
                       doneValue is bool and true;

            var value = result.TryGetProperty("value", out var yielded)
                ? yielded
                : Symbol.Undefined;

            return (done ? Symbol.Undefined : value, done);
        }
    }
}
