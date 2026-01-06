#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(SwitchStatement statement)
    {
        /// <summary>
        /// JsValue-returning version for use in hot paths.
        /// </summary>
        private JsValue EvaluateSwitchJsValue(JsEnvironment environment,
            EvaluationContext context,
            Symbol? targetLabel)
        {
            var discriminantJs = statement.Discriminant.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var instantiationPlan = ((IAstCacheable<SwitchInstantiationPlan>)statement).GetOrCreateCache();

            // Create a lexical environment for the entire switch block
            // This environment is shared by all case clause bodies
            var switchEnv = new JsEnvironment(environment, false, instantiationPlan.IsStrict);

            // Push a scope context for the switch block
            var scopeMode = instantiationPlan.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var scopeHandle = context.PushScope(ScopeKind.Block, scopeMode);

            // Hoist lexical declarations from all case bodies
            SwitchStatement.InstantiateSwitchLexicalDeclarations(instantiationPlan, switchEnv, context);

            // In strict mode, instantiate function declarations as lexical bindings
            SwitchStatement.InstantiateSwitchFunctionsInStrictMode(statement, switchEnv, context, instantiationPlan.IsStrict);

            // V = undefined (spec step 1)
            var completionValue = JsValue.Undefined;

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

                var testJs = switchCase.Test.EvaluateExpression(switchEnv, context);
                if (context.ShouldStopEvaluation)
                {
                    return completionValue;
                }

                if (StrictEqualsValue(discriminantJs, testJs))
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
                return JsValue.Undefined;
            }

            // Second pass: Execute from the matched/default case onwards
            for (var i = startIndex.Value; i < statement.Cases.Length; i++)
            {
                var switchCase = statement.Cases[i];

                // Evaluate the case clause body statements in the switch environment
                // We evaluate the statements directly without creating a new block environment
                var (caseCompletionJs, hasCaseJs) =
                    SwitchStatement.EvaluateCaseClauseBodyJsValue(switchCase.Body, switchEnv, context);

                // If R.[[value]] is not empty, let V = R.[[value]] (spec step 4.b.ii)
                // UpdateEmpty semantics: only update V if the completion is not empty
                if (hasCaseJs)
                {
                    completionValue = caseCompletionJs;
                }

                if (context.TryClearBreak(targetLabel))
                {
                    // Return Completion(UpdateEmpty(R, V)) (spec step 4.b.iii)
                    // Break already happened, return the accumulated value
                    break;
                }

                if (context.IsReturn || context.IsThrow || context.IsContinue)
                {
                    break;
                }
            }

            return completionValue;
        }

        private static void InstantiateSwitchLexicalDeclarations(SwitchInstantiationPlan plan, JsEnvironment switchEnv,
            EvaluationContext context)
        {
            foreach (var binding in plan.LexicalBindings)
            {
                binding.Target.CreateUninitializedLexicalBindings(switchEnv, binding.IsConst);
            }

            foreach (var funcBinding in plan.FunctionBindings)
            {
                if (!funcBinding.InitializeNow)
                {
                    switchEnv.DefineJsValue(
                        funcBinding.Name,
                        JsValue.Uninitialized,
                        true,
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                    continue;
                }

                var functionValue = funcBinding.Function.CreateFunctionValue(switchEnv, context,
                    skipInternalNameBinding: true);
                switchEnv.DefineJsValue(
                    funcBinding.Name,
                    JsValue.FromObjectUnsafe(functionValue),
                    true,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            foreach (var className in plan.ClassBindings)
            {
                switchEnv.DefineJsValue(
                    className,
                    JsValue.Undefined,
                    true,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: false);
            }
        }

        /// <summary>
        /// In strict mode, instantiate function declarations as lexical bindings in the switch environment.
        /// This is called AFTER InstantiateSwitchLexicalDeclarations.
        /// </summary>
        private static void InstantiateSwitchFunctionsInStrictMode(SwitchStatement switchStmt, 
            JsEnvironment switchEnv, EvaluationContext context, bool isStrict)
        {
            if (!isStrict)
            {
                return;
            }

            // In strict mode, function declarations behave like lexical declarations
            foreach (var switchCase in switchStmt.Cases)
            {
                foreach (var stmt in switchCase.Body.Statements)
                {
                    if (stmt is not FunctionDeclaration funcDecl)
                    {
                        continue;
                    }

                    // Pass skipInternalNameBinding: true so the function doesn't create an internal
                    // const binding for its name (the binding is handled by switchEnv.Define below).
                    var functionValue = funcDecl.Function.CreateFunctionValue(switchEnv, context,
                        skipInternalNameBinding: true);
                    switchEnv.DefineJsValue(
                        funcDecl.Name,
                        JsValue.FromObjectUnsafe(functionValue),
                        true,
                        isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }
            }
        }

        /// <summary>
        /// Evaluates a case clause body and returns a tuple with the value and whether it produced a value.
        /// </summary>
        private static (JsValue result, bool hasResult) EvaluateCaseClauseBodyJsValue(BlockStatement body,
            JsEnvironment switchEnv, EvaluationContext context)
        {
            // Evaluate statements in the case clause body without creating a new environment
            // The statements are evaluated in the shared switch environment
            var hasResult = false;
            var result = JsValue.Undefined;

            foreach (var stmt in body.Statements)
            {
                context.ThrowIfCancellationRequested();

                // Special handling for async/generator function declarations
                // They need to be initialized when evaluated (not during instantiation)
                if (stmt is FunctionDeclaration funcDecl &&
                    (funcDecl.Function.IsAsync || funcDecl.Function.WasAsync || funcDecl.Function.IsGenerator))
                {
                    // Pass skipInternalNameBinding: true so the function doesn't create an internal
                    // const binding for its name (the binding was already defined during instantiation).
                    var functionValue = funcDecl.Function.CreateFunctionValue(switchEnv, context,
                        skipInternalNameBinding: true);
                    switchEnv.AssignJsValue(funcDecl.Name, JsValue.FromObjectUnsafe(functionValue));
                    // Function declarations have empty completion
                    continue;
                }

                var completion = stmt.EvaluateStatementJsValue(switchEnv, context);
                var shouldStop = context.ShouldStopEvaluation;
                // Apply UpdateEmpty semantics: only update result if completion is not empty (Unit)
                // This preserves the previous value when break/continue have empty completion
                var shouldCapture =
                    !completion.IsUnit &&
                    (!shouldStop ||
                     context.IsReturn ||
                     context.IsThrow ||
                     context.IsYield ||
                     context.IsBreak ||
                     context.IsContinue);

                if (shouldCapture)
                {
                    result = completion;
                    hasResult = true;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            return (result, hasResult);
        }

        // Strictness is precomputed in the instantiation plan.
    }
}
