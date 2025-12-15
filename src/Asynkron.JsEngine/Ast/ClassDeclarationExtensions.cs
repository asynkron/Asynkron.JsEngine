using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDeclaration declaration)
    {
        private JsValue EvaluateClassJsValue(JsEnvironment environment,
            EvaluationContext context)
        {
            var constructorValue = CreateClassValue(declaration.Definition, environment, context, declaration.Name);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            environment.DefineJsValue(declaration.Name, JsValue.FromObject(constructorValue), isLexical: true, blocksFunctionScopeOverride: true);
            return JsValue.Undefined;
        }
    }
}
