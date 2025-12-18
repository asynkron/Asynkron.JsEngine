using Asynkron.JsEngine.JsTypes;

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
            var discriminantJs = EvaluateExpression(statement.Discriminant, environment, context);
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
            InstantiateSwitchLexicalDeclarations(instantiationPlan, switchEnv, context);

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

                var testJs = EvaluateExpression(switchCase.Test, switchEnv, context);
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
                var (caseCompletionJs, hasCaseJs) = EvaluateCaseClauseBodyJsValue(switchCase.Body, switchEnv, context);

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

                if (context.IsReturn || context.IsThrow)
                {
                    break;
                }
            }

            return completionValue;
        }

        private static void InstantiateSwitchLexicalDeclarations(SwitchInstantiationPlan plan, JsEnvironment switchEnv, EvaluationContext context)
        {
            foreach (var binding in plan.LexicalBindings)
            {
                binding.Target.CreateUninitializedLexicalBindings(switchEnv, isConst: binding.IsConst);
            }

            foreach (var funcBinding in plan.FunctionBindings)
            {
                if (!funcBinding.InitializeNow)
                {
                    switchEnv.DefineJsValue(
                        funcBinding.Name,
                        JsValue.Uninitialized,
                        isConst: true,
                        isLexical: true,
                        blocksFunctionScopeOverride: true);
                    continue;
                }

                var functionValue = CreateFunctionValue(funcBinding.Function, switchEnv, context,
                    skipInternalNameBinding: true);
                switchEnv.DefineJsValue(
                    funcBinding.Name,
                    JsValue.FromObjectUnsafe(functionValue),
                    isConst: true,
                    isLexical: true,
                    blocksFunctionScopeOverride: true);
            }

            foreach (var className in plan.ClassBindings)
            {
                switchEnv.DefineJsValue(
                    className,
                    JsValue.Undefined,
                    isConst: true,
                    isLexical: true,
                    blocksFunctionScopeOverride: false);
            }
        }

        /// <summary>
        /// Evaluates a case clause body and returns a tuple with the value and whether it produced a value.
        /// </summary>
        private static (JsValue result, bool hasResult) EvaluateCaseClauseBodyJsValue(BlockStatement body, JsEnvironment switchEnv, EvaluationContext context)
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
                    var functionValue = CreateFunctionValue(funcDecl.Function, switchEnv, context,
                        skipInternalNameBinding: true);
                    switchEnv.Assign(funcDecl.Name, functionValue);
                    // Function declarations have empty completion
                    continue;
                }

                var completion = EvaluateStatementJsValue(stmt, switchEnv, context);
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
