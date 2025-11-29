namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(FunctionDeclaration declaration)
    {
        private object? EvaluateFunctionDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            var currentScope = context.CurrentScope;
            var annexBEnabled = currentScope.AllowAnnexB;
            var isStrictScope = currentScope.IsStrict;
            object? function = null;
            if (!isStrictScope &&
                environment.TryFindBinding(declaration.Name, out var bindingEnvironment, out var existingValue) &&
                bindingEnvironment.HasOwnLexicalBinding(declaration.Name))
            {
                function = existingValue;
            }

            function ??= CreateFunctionValue(declaration.Function, environment, context);
            var isBlockEnvironment = !environment.IsFunctionScope;
            var shouldCreateLexicalBinding = isStrictScope ||
                                             (!annexBEnabled && isBlockEnvironment);
            if (shouldCreateLexicalBinding)
            {
                environment.Define(declaration.Name, function);
            }
            var skipVarBinding = (context.BlockedFunctionVarNames is { } blocked &&
                                  blocked.Contains(declaration.Name)) || environment.HasBodyLexicalName(declaration.Name);

            var hasBlockingLexicalBeforeFunctionScope =
                !isStrictScope && HasBlockingLexicalBeforeFunctionScope(environment, declaration.Name);

            var isAnnexBBlockFunction = !isStrictScope && annexBEnabled && isBlockEnvironment;
            var shouldCreateVarBinding = annexBEnabled || !isBlockEnvironment;
            if (!shouldCreateVarBinding || skipVarBinding || hasBlockingLexicalBeforeFunctionScope)
            {
                return EmptyCompletion;
            }

            if (!isStrictScope)
            {
                var assigned = environment.TryAssignBlockedBinding(declaration.Name, function);
            }

            var configurable = context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
            bool? globalVarConfigurable = isAnnexBBlockFunction ? true : null;
            bool? globalFunctionConfigurable = isAnnexBBlockFunction ? null : configurable;
            environment.DefineFunctionScoped(
                declaration.Name,
                function,
                true,
                isFunctionDeclaration: !isAnnexBBlockFunction,
                globalFunctionConfigurable: globalFunctionConfigurable,
                context: context,
                globalVarConfigurable: globalVarConfigurable);

            return EmptyCompletion;
        }
    }

}
