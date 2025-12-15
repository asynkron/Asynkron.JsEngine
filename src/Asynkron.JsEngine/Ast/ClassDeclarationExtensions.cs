using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDeclaration declaration)
    {
        private object? EvaluateClass(JsEnvironment environment,
            EvaluationContext context)
        {
            return EvaluateClassJsValue(declaration, environment, context).ToObject();
        }

        private JsValue EvaluateClassJsValue(JsEnvironment environment,
            EvaluationContext context)
        {
            var constructorValue = CreateClassValue(declaration.Definition, environment, context, declaration.Name);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
            return JsValue.Undefined;
        }
    }
}
