namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private enum BindingMode
    {
        Assign,
        DefineLet,
        DefineConst,
        DefineVar,
        DefineParameter
    }
}
