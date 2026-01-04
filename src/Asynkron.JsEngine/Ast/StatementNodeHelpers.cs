namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Determines if the statement type should reset script completion value.
    /// Used by both the interpreter and execution plan runner.
    /// </summary>
    private static bool ShouldResetScriptCompletion(StatementNode statement)
    {
        return statement switch
        {
            IfStatement => true,
            SwitchStatement => true,
            TryStatement => true,
            ForStatement => true,
            ForEachStatement => true,
            WhileStatement => true,
            DoWhileStatement => true,
            LabeledStatement => true,
            _ => false
        };
    }
}
