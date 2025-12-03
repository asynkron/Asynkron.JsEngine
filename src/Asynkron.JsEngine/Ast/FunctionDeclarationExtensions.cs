namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(FunctionDeclaration declaration)
    {
        private object? EvaluateFunctionDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            // FunctionDeclarationInstantiation handles most bindings up front.
            // In sloppy eval Annex B cases, we must copy the function object
            // into the variable environment when the declaration is evaluated.
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
