namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionDeclaration declaration)
    {
        private object? EvaluateFunctionDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            // FunctionDeclarationInstantiation handles most bindings up front.
            // Annex B.3.3.4 (sloppy eval only) requires copying the block-scoped
            // function object into the var/global binding when the declaration
            // is evaluated, mirroring the web‑compat behaviour described in the spec.
            if (context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false } &&
                context.CurrentScope.AllowAnnexB)
            {
                var functionScope = environment.GetFunctionScope();
                if (!functionScope.HasFunctionScopedBinding(declaration.Name))
                {
                    return EmptyCompletion;
                }

                var functionValue = CreateFunctionValue(declaration.Function, environment, context);
                bool? globalFunctionConfigurable = functionScope.IsGlobalFunctionScope ? true : null;
                functionScope.DefineFunctionScoped(
                    declaration.Name,
                    functionValue,
                    hasInitializer: true,
                    isFunctionDeclaration: true,
                    globalFunctionConfigurable: globalFunctionConfigurable,
                    context: context,
                    blocksFunctionScopeOverride: true,
                    globalVarConfigurable: null,
                    allowExistingGlobalFunctionRedeclaration: true,
                    isAnnexBFunction: true);
            }

            return EmptyCompletion;
        }
    }
}
