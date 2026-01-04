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

            // Per ES spec, class declarations create MUTABLE bindings in the declaring scope
            // (like let), not immutable ones (like const). The immutable binding is only
            // inside the class body (handled in ClassDefinitionExtensions.CreateClassScopeIfNeeded).
            environment.DefineJsValue(declaration.Name, constructorValue, isConst: false, isLexicalBinding: true,
                blocksFunctionScopeOverride: true, isImmutableBinding: false);
            // Per ES spec, ClassDeclaration returns NormalCompletion(empty).
            return JsValue.Unit;
        }
    }
}
