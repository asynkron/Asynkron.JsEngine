namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private interface IFunctionNameTarget
    {
        void EnsureHasName(string name);
    }
}
