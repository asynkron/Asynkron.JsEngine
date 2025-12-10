namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SwitchStatement statement)
    {
        private object? EvaluateSwitch(JsEnvironment environment,
            EvaluationContext context,
            Symbol? targetLabel)
        {
            var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            // Create a lexical environment for the entire switch block
            // This environment is shared by all case clause bodies
            var switchEnv = new JsEnvironment(environment, false, IsStrictBlock(statement));

            // Push a scope context for the switch block
            // Switch blocks use Sloppy mode (not SloppyAnnexB) to prevent function hoisting
            var isStrict = IsStrictBlock(statement);
            var scopeMode = isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var scopeHandle = context.PushScope(
                ScopeKind.Block,
                scopeMode,
                skipAnnexBInstantiation: false);

            // Hoist lexical declarations from all case bodies
            InstantiateSwitchLexicalDeclarations(statement, switchEnv, context);

            // V = undefined (spec step 1)
            object? completionValue = Symbol.Undefined;

            // First pass: Find the index of the matching case or default
            int? matchedCaseIndex = null;
            int? defaultCaseIndex = null;

            for (var i = 0; i < statement.Cases.Length; i++)
            {
                var switchCase = statement.Cases[i];

                if (switchCase.Test is null)
                {
                    defaultCaseIndex = i;
                    continue;
                }

                var test = EvaluateExpression(switchCase.Test, switchEnv, context);
                if (context.ShouldStopEvaluation)
                {
                    return completionValue;
                }

                if (StrictEquals(discriminant, test))
                {
                    matchedCaseIndex = i;
                    break;
                }
            }

            // Determine where to start executing
            var startIndex = matchedCaseIndex ?? defaultCaseIndex;
            if (startIndex is null)
            {
                // No match and no default, return undefined
                return Symbol.Undefined;
            }

            // Second pass: Execute from the matched/default case onwards
            for (var i = startIndex.Value; i < statement.Cases.Length; i++)
            {
                var switchCase = statement.Cases[i];

                // Evaluate the case clause body statements in the switch environment
                // We evaluate the statements directly without creating a new block environment
                var caseCompletion = EvaluateCaseClauseBody(statement, switchCase.Body, switchEnv, context);

                // If R.[[value]] is not empty, let V = R.[[value]] (spec step 4.b.ii)
                // UpdateEmpty semantics: only update V if the completion is not empty
                if (!ReferenceEquals(caseCompletion, EmptyCompletion))
                {
                    completionValue = caseCompletion;
                }

                if (context.TryClearBreak(targetLabel))
                {
                    // Return Completion(UpdateEmpty(R, V)) (spec step 4.b.iii)
                    // Break already happened, return the accumulated value
                    break;
                }

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }
            }

            return completionValue;
        }

        private void InstantiateSwitchLexicalDeclarations(JsEnvironment switchEnv, EvaluationContext context)
        {
            // Collect all lexical declarations from all case clause bodies
            foreach (var switchCase in statement.Cases)
            {
                foreach (var stmt in switchCase.Body.Statements)
                {
                    if (stmt is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                        } varDecl)
                    {
                        var isConst = varDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                        foreach (var declarator in varDecl.Declarators)
                        {
                            declarator.Target.CreateUninitializedLexicalBindings(switchEnv, isConst: isConst);
                        }
                    }
                    else if (stmt is FunctionDeclaration funcDecl)
                    {
                        // Function declarations in switch case blocks create lexical bindings
                        // For async/generator functions, create uninitialized binding (they'll be initialized when evaluated)
                        // For regular functions, create and initialize the binding immediately
                        var isAsyncOrGenerator = funcDecl.Function.IsAsync || funcDecl.Function.IsGenerator;
                        if (isAsyncOrGenerator)
                        {
                            // Async and generator functions are always lexically scoped, never Annex B
                            switchEnv.Define(
                                funcDecl.Name,
                                JsEnvironment.Uninitialized,
                                isConst: true,
                                isLexical: true,
                                blocksFunctionScopeOverride: true);
                        }
                        else
                        {
                            // Regular functions get initialized during instantiation
                            // Pass skipInternalNameBinding: true so the function doesn't create an internal
                            // const binding for its name (the binding is handled by switchEnv.Define below).
                            var functionValue = CreateFunctionValue(funcDecl.Function, switchEnv, context,
                                skipInternalNameBinding: true);
                            switchEnv.Define(
                                funcDecl.Name,
                                functionValue,
                                isConst: true,
                                isLexical: true,
                                blocksFunctionScopeOverride: true);
                        }
                    }
                    else if (stmt is ClassDeclaration classDecl)
                    {
                        // Class declarations create lexical bindings
                        switchEnv.Define(
                            classDecl.Name,
                            Symbol.Undefined,
                            isConst: true,
                            isLexical: true,
                            blocksFunctionScopeOverride: false);
                    }
                }
            }
        }

        private object? EvaluateCaseClauseBody(BlockStatement body, JsEnvironment switchEnv, EvaluationContext context)
        {
            // Evaluate statements in the case clause body without creating a new environment
            // The statements are evaluated in the shared switch environment
            var result = EmptyCompletion;

            foreach (var stmt in body.Statements)
            {
                context.ThrowIfCancellationRequested();

                // Special handling for async/generator function declarations
                // They need to be initialized when evaluated (not during instantiation)
                if (stmt is FunctionDeclaration funcDecl &&
                    (funcDecl.Function.IsAsync || funcDecl.Function.IsGenerator))
                {
                    // Pass skipInternalNameBinding: true so the function doesn't create an internal
                    // const binding for its name (the binding was already defined during instantiation).
                    var functionValue = CreateFunctionValue(funcDecl.Function, switchEnv, context,
                        skipInternalNameBinding: true);
                    switchEnv.Assign(funcDecl.Name, functionValue);
                    // Function declarations have empty completion
                    continue;
                }

                var completion = EvaluateStatement(stmt, switchEnv, context);
                var shouldStop = context.ShouldStopEvaluation;
                var shouldCapture =
                    !ReferenceEquals(completion, EmptyCompletion) &&
                    (!shouldStop ||
                     context.IsReturn ||
                     context.IsThrow ||
                     context.IsYield ||
                     context.IsBreak ||
                     context.IsContinue);

                if (shouldCapture)
                {
                    result = completion;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            return result;
        }

        private bool IsStrictBlock(SwitchStatement stmt)
        {
            // Check if any case body is strict
            foreach (var switchCase in stmt.Cases)
            {
                if (switchCase.Body.IsStrict)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
