namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassExpression expression)
    {
        private object? EvaluateClassExpression(JsEnvironment environment,
            EvaluationContext context)
        {
            JsEnvironment evaluationEnvironment = environment;
            JsEnvironment? classScopeEnvironment = null;
            if (expression.Name is { } className)
            {
                classScopeEnvironment = CreateClassScopeEnvironment(environment, className, expression.Source);
                evaluationEnvironment = classScopeEnvironment;
            }

            var classValue = CreateClassValue(expression.Definition, evaluationEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return classValue;
            }

            if (expression.Name is { } nameSymbol && classScopeEnvironment is not null)
            {
                classScopeEnvironment.TryAssignBlockedBinding(nameSymbol, classValue);
            }

            return classValue;
        }
    }
}
