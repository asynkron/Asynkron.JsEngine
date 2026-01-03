#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDeclaration declaration)
    {
        private JsValue EvaluateClassJsValue(JsEnvironment environment,
            EvaluationContext context)
        {
            var constructorValue = declaration.Definition.CreateClassValue(environment, context, declaration.Name);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Unit;
            }

            environment.DefineJsValue(declaration.Name, constructorValue, isConst: true, isLexicalBinding: true,
                blocksFunctionScopeOverride: true, isImmutableBinding: true);
            // Per ES spec, ClassDeclaration returns NormalCompletion(empty).
            return JsValue.Unit;
        }
    }
}
