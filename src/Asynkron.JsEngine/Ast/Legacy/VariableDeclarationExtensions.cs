
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateVariableDeclarationJsValue(this VariableDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        foreach (var declarator in declaration.Declarators)
        {
            declaration.Kind.EvaluateVariableDeclarator(declarator, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        // Per ES spec, variable declarations have an empty completion value.
        // This must be Unit (not Undefined) so it doesn't overwrite previous
        // statement results in the program's completion value.
        return JsValue.Unit;
    }
}
