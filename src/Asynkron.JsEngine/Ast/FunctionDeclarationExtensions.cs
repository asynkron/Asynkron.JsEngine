#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionDeclaration funcDecl)
    {
        private JsValue EvaluateFunctionDeclarationJsValue(
            JsEnvironment environment,
            EvaluationContext context)
        {
            // In strict mode, function declarations in blocks are NOT hoisted.
            // They should be created as lexical bindings when execution reaches them.
            // (Annex B.3.3.1: extension only applies when strict is false)
            if (context.CurrentScope.IsStrict)
            {
                // Check if the binding already exists in the current environment
                // (it shouldn't, but let's be defensive)
                if (!environment.HasBinding(funcDecl.Name))
                {
                    // Pass skipInternalNameBinding: true so the function doesn't create an internal
                    // const binding for its name (the binding is handled by environment.DefineJsValue below).
                    var functionValue = funcDecl.Function.CreateFunctionValue(environment, context,
                        skipInternalNameBinding: true);
                    
                    // Define it as a lexical binding in the current environment
                    environment.DefineJsValue(
                        funcDecl.Name,
                        JsValue.FromObjectUnsafe(functionValue),
                        isConst: false,
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                }
                
                // Function declarations have empty completion per spec
                return JsValue.Unit;
            }

            // Non-strict mode: Function declarations are hoisted and instantiated 
            // during FunctionDeclarationInstantiation. The actual declaration  
            // statement is a no-op at runtime.
            // Per ES spec, FunctionDeclaration returns NormalCompletion(empty).
            return JsValue.Unit;
        }
    }
}
