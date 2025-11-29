namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private readonly record struct ResumePayload(bool HasValue, bool IsThrow, bool IsReturn, object? Value)
    {
        public static ResumePayload Empty { get; } = new(false, false, false, Symbol.Undefined);

        public static ResumePayload FromValue(object? value)
        {
            return new ResumePayload(true, false, false, value);
        }

        public static ResumePayload FromThrow(object? value)
        {
            return new ResumePayload(true, true, false, value);
        }

        public static ResumePayload FromReturn(object? value)
        {
            return new ResumePayload(true, false, true, value);
        }
    }
}
