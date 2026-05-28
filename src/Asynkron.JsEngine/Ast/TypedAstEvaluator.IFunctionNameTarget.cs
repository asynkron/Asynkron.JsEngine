namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    internal interface IFunctionNameTarget
    {
        void EnsureHasName(string name, bool overwriteExisting = false);
    }
}
