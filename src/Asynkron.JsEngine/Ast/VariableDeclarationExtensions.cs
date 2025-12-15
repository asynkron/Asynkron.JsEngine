using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(VariableDeclaration declaration)
    {
        private object? EvaluateVariableDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateVariableDeclarationJsValue(declaration, environment, context).ToObject();
        }

        private JsValue EvaluateVariableDeclarationJsValue(JsEnvironment environment,
            EvaluationContext context)
        {
            foreach (var declarator in declaration.Declarators)
            {
                EvaluateVariableDeclarator(declaration.Kind, declarator, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }

            return JsValue.Undefined;
        }
    }
}
