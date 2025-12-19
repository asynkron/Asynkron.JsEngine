using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class YieldResumeContext
    {
        private readonly Dictionary<int, ResumePayload> _pending = new();

        public void SetValue(int yieldIndex, JsValue value)
        {
            _pending[yieldIndex] = ResumePayload.FromValue(value);
        }

        public void SetException(int yieldIndex, JsValue value)
        {
            _pending[yieldIndex] = ResumePayload.FromThrow(value);
        }

        public void SetReturn(int yieldIndex, JsValue value)
        {
            _pending[yieldIndex] = ResumePayload.FromReturn(value);
        }

        public ResumePayload TakePayload(int yieldIndex)
        {
            if (_pending.TryGetValue(yieldIndex, out var payload))
            {
                return payload;
            }

            return ResumePayload.Empty;
        }

        public void Clear()
        {
            _pending.Clear();
        }
    }
}
