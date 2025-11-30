namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDeclaration declaration)
    {
        private object? EvaluateClass(JsEnvironment environment,
            EvaluationContext context)
        {
            var constructorValue = CreateClassValue(declaration.Definition, environment, context, declaration.Name);
            if (context.ShouldStopEvaluation)
            {
                return EmptyCompletion;
            }

            environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
            return EmptyCompletion;
        }
    }
}
