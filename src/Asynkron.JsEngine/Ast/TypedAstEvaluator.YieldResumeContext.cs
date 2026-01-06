#region

#endregion

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

        public void Clear()
        {
            _pending.Clear();
        }
    }
}
