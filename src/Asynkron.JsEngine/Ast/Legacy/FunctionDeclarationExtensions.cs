
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateFunctionDeclarationJsValue(
        FunctionDeclaration funcDecl,
        JsEnvironment environment,
        EvaluationContext context)
    {
        // Check if we're at the function/global scope level (no nested block environment).
        // - If we're at the function scope level: function was hoisted during FunctionDeclarationInstantiation.
        //   Per ES spec, the runtime evaluation is a no-op (NormalCompletion(empty)).
        // - If we're in a nested block (if/while/etc): this is a block-scoped function declaration.
        //   Per Annex B.3.3.3, we need to create the function and assign it to both the block-scoped
        //   lexical binding and the outer function-scoped var binding.
        var functionScope = environment.GetFunctionScope();
        var isAtFunctionScope = ReferenceEquals(functionScope, environment) ||
                                environment.IsEvalDeclarationEnvironment;

        // Special case: AnnexB function declarations in if/else branches in sloppy mode.
        // In AST evaluation mode, we don't create a block environment for if/else branches,
        // so isAtFunctionScope is true even though we're evaluating a function declaration
        // inside an if/else. We detect this case by checking if the function-scoped binding
        // has value `undefined` (set during hoisting per Annex B.3.3.2).
        var isAnnexBHoistedFunction = false;
        if (isAtFunctionScope && !context.CurrentScope.IsStrict)
        {
            // In sloppy mode, check if there's a function-scoped binding that was hoisted
            // with undefined value (AnnexB pattern)
            if (functionScope.HasFunctionScopedBinding(funcDecl.Name))
            {
                var existingValue = functionScope.GetBindingValueDirect(funcDecl.Name);
                if (existingValue.IsUndefined)
                {
                    isAnnexBHoistedFunction = true;
                }
            }
        }

        if (isAtFunctionScope && !isAnnexBHoistedFunction)
        {
            // Function-scoped: function was hoisted with actual function value, no-op at runtime.
            // Per ES spec, FunctionDeclaration returns NormalCompletion(empty).
            return JsValue.Unit;
        }

        // Block-scoped function declaration (e.g., inside if/else in sloppy mode).
        // We need to create the function and update bindings.

        // Pass skipInternalNameBinding: true so the function doesn't create an internal
        // const binding for its name (the binding is handled by environment.Define below).
        var functionValue = funcDecl.Function.CreateFunctionValue(environment, context,
            skipInternalNameBinding: true);
        var fnValueJs = JsValue.FromObjectUnsafe(functionValue);

        if (isAnnexBHoistedFunction)
        {
            // AnnexB case in AST evaluation: we're at function scope evaluating a function
            // declaration from an if/else branch. The binding already exists with `undefined`,
            // we just need to update it with the function value.
            // Per B.3.3.1/B.3.3.2: skip if the name is blocked (conflicts with a parameter
            // or lexical binding in an enclosing scope).
            if (environment.IsAnnexBBlocked(funcDecl.Name))
            {
                return JsValue.Unit;
            }

            functionScope.AssignJsValue(funcDecl.Name, fnValueJs);

            // For global scope, also update the global object property.
            if (functionScope.IsGlobalFunctionScope)
            {
                var globalThis = functionScope.GetRootGlobalObject();
                globalThis?.SetProperty(funcDecl.Name.Name, fnValueJs);
            }
        }
        else
        {
            var isBlocked = !context.CurrentScope.IsStrict &&
                            environment.IsAnnexBBlocked(funcDecl.Name);

            // Block-scoped case: create lexical binding in block environment.
            // When AnnexB is blocked (param/lexical conflict) and we're in the body
            // environment (no explicit block for if-branches), skip the binding to
            // avoid shadowing the parameter/lexical.
            if (!isBlocked || !environment.IsBodyEnvironment)
            {
                environment.DefineJsValue(
                    funcDecl.Name,
                    fnValueJs,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            // For hoisted functions in sloppy mode, also update the function-scope binding.
            // This implements Annex B.3.3.3 semantics where block-scoped functions also
            // update the outer function-scope binding.
            if (!isBlocked && functionScope.HasFunctionScopedBinding(funcDecl.Name))
            {
                functionScope.AssignJsValue(funcDecl.Name, fnValueJs);

                // For global scope, also update the global object property.
                if (functionScope.IsGlobalFunctionScope)
                {
                    var globalThis = functionScope.GetRootGlobalObject();
                    globalThis?.SetProperty(funcDecl.Name.Name, fnValueJs);
                }
            }
        }

        return JsValue.Unit;
    }
}
