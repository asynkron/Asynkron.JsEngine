using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private readonly record struct ResumePayload(bool HasValue, bool IsThrow, bool IsReturn, JsValue Value)
    {
        public static ResumePayload Empty { get; } = new(false, false, false, JsValue.Undefined);

        public static ResumePayload FromValue(JsValue value)
        {
            return new ResumePayload(true, false, false, value);
        }

        public static ResumePayload FromThrow(JsValue value)
        {
            return new ResumePayload(true, true, false, value);
        }

        public static ResumePayload FromReturn(JsValue value)
        {
            return new ResumePayload(true, false, true, value);
        }
    }
}
