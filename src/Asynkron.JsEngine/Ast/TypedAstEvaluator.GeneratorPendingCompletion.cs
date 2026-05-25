namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed class GeneratorPendingCompletion
    {
        public bool HasValue { get; set; }
        public bool IsThrow { get; set; }
        public bool IsReturn { get; set; }
        public JsValue Value { get; set; } = JsValue.Undefined;
    }
}
