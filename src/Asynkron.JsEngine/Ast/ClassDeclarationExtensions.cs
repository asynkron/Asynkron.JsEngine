namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDeclaration declaration)
    {
        private object? EvaluateClass(JsEnvironment environment,
            EvaluationContext context)
        {
            JsEnvironment evaluationEnvironment = environment;
            JsEnvironment? classScopeEnvironment = null;
            if (declaration.Name is { } className)
            {
                classScopeEnvironment = CreateClassScopeEnvironment(environment, className, declaration.Source);
                evaluationEnvironment = classScopeEnvironment;
            }

            var constructorValue = CreateClassValue(declaration.Definition, evaluationEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return EmptyCompletion;
            }

            if (declaration.Name is { } bindingName && classScopeEnvironment is not null)
            {
                var assigned = classScopeEnvironment.TryAssignBlockedBinding(bindingName, constructorValue);
                if (!assigned)
                {
                    throw new InvalidOperationException("Failed to initialize class name binding in class scope.");
                }
            }

            environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
            return EmptyCompletion;
        }
    }
}
