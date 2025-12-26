#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BlockStatement block)
    {
        /// <summary>
        /// Evaluates a block statement and returns the completion value as JsValue.
        /// Returns JsValue.Undefined for empty blocks to match browser behavior.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBlockJsValue(
            JsEnvironment environment,
            EvaluationContext context)
        {
            return block.EvaluateBlockCore(environment, context);
        }

        /// <summary>
        /// Core block evaluation that returns JsValue directly.
        /// Returns JsValue.Undefined for empty blocks to match browser behavior.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBlockCore(
            JsEnvironment environment,
            EvaluationContext context)
        {
            if (context.AllowIdentifierCache)
            {
                context.AllowIdentifierCache = AllowsIdentifierCaching(block);
            }

            var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

            // Fast path: if the block has no lexical/function decls, execute directly in the incoming environment
            if (!hoistPlan.NeedsEnvironment)
            {
                return block.EvaluateBlockFast(environment, context);
            }

            // Slow path: needs new environment for lexical bindings
            return block.EvaluateBlockSlow(environment, context);
        }

        /// <summary>
        /// Fast path for blocks that don't need a new environment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JsValue EvaluateBlockFast(
            JsEnvironment environment,
            EvaluationContext context)
        {
            // Start with Unit (empty completion) - per ES spec, empty blocks return empty completion
            // which doesn't override previous statement completion values
            var resultJs = JsValue.Unit;
            foreach (var statement in block.Statements)
            {
                context.ThrowIfCancellationRequested();

                var completionJs = statement.EvaluateStatementJsValue(environment, context);
                var shouldStop = context.ShouldStopEvaluation;
                var shouldCapture =
                    !completionJs.IsUnit &&
                    (!shouldStop ||
                     context.IsReturn ||
                     context.IsThrow ||
                     context.IsYield ||
                     context.IsBreak ||
                     context.IsContinue);

                if (shouldCapture)
                {
                    resultJs = completionJs;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            return resultJs;
        }

        /// <summary>
        /// Slow path for blocks that need a new environment for lexical bindings.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateBlockSlow(
            JsEnvironment environment,
            EvaluationContext context)
        {
            // Check if we can safely pool the environment (no closures or dynamic scope that would capture it)
            var canPoolEnvironment = !ContainsWithOrDirectEval(block) && !ContainsInnerFunctionExpression(block);

            var scope = canPoolEnvironment
                ? JsEnvironmentPool.Rent(environment, false, block.IsStrict)
                : new JsEnvironment(environment, false, block.IsStrict);

            try
            {
                return block.EvaluateBlockSlowCore(scope, context);
            }
            finally
            {
                if (canPoolEnvironment)
                {
                    JsEnvironmentPool.Return(scope);
                }
            }
        }

        /// <summary>
        /// Core slow path logic, separated to allow proper try/finally for pooling.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private JsValue EvaluateBlockSlowCore(
            JsEnvironment scope,
            EvaluationContext context)
        {
            // Start with Unit (empty completion) - per ES spec, empty blocks return empty completion
            // which doesn't override previous statement completion values
            var resultJs = JsValue.Unit;
            scope.ScopeId = block.ScopeId;
            scope.SetSlotMap(block.SlotMap);
            if (block is { SlotCount: > 0, ScopeId: >= 0 })
            {
                scope.InitializeSlots(block.SlotCount, block.ScopeId);
            }

            var mode = scope.IsStrict || block.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
            using var scopeHandle = context.PushScope(ScopeKind.Block, mode);

            // Per ES spec, lexical declarations (let/const/class) must be hoisted to create
            // bindings in the TDZ (Temporal Dead Zone) BEFORE function hoisting.
            // This ensures closures that reference lexical variables will find TDZ bindings
            // and throw ReferenceError if accessed before initialization.
            foreach (var stmt in block.Statements)
            {
                switch (stmt)
                {
                    case VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } lexDecl:
                        var isConst =
                            lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                        foreach (var declarator in lexDecl.Declarators)
                        {
                            block.HoistLexicalBindingTargetForTdz(declarator.Target, scope, isConst);
                        }

                        break;
                    case ClassDeclaration classDecl:
                        if (!scope.HasBinding(classDecl.Name))
                        {
                            scope.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isLexical: true,
                                blocksFunctionScopeOverride: true, isConst: true);
                        }

                        break;
                }
            }

            // Block-scoped function declarations (strict mode behavior - no AnnexB hoisting)
            block.InstantiateLexicalBlockFunctions(scope, context);

            foreach (var statement in block.Statements)
            {
                context.ThrowIfCancellationRequested();

                var completionJs = statement.EvaluateStatementJsValue(scope, context);
                var shouldStop = context.ShouldStopEvaluation;
                var shouldCapture =
                    !completionJs.IsUnit &&
                    (!shouldStop ||
                     context.IsReturn ||
                     context.IsThrow ||
                     context.IsYield ||
                     context.IsBreak ||
                     context.IsContinue);

                if (shouldCapture)
                {
                    resultJs = completionJs;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            return resultJs;
        }

        private void InstantiateLexicalBlockFunctions(
            JsEnvironment blockEnvironment,
            EvaluationContext context)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is not FunctionDeclaration functionDeclaration)
                {
                    continue;
                }

                // Pass skipInternalNameBinding: true so the function doesn't create an internal
                // const binding for its name (the binding is handled by blockEnvironment.Define below).
                var functionValue = functionDeclaration.Function.CreateFunctionValue(blockEnvironment, context,
                    skipInternalNameBinding: true);
                blockEnvironment.DefineJsValue(
                    functionDeclaration.Name,
                    JsValue.FromObjectUnsafe(functionValue),
                    true,
                    isLexical: true,
                    blocksFunctionScopeOverride: true);
            }
        }

        private void HoistLexicalBindingTargetForTdz(BindingTarget target, JsEnvironment blockEnvironment, bool isConst)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!blockEnvironment.HasBinding(id.Name))
                    {
                        blockEnvironment.DefineJsValue(id.Name, JsValue.Uninitialized, isLexical: true,
                            blocksFunctionScopeOverride: true, isConst: isConst);
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            block.HoistLexicalBindingTargetForTdz(elementTarget, blockEnvironment, isConst);
                        }
                    }

                    if (arrayBinding.RestElement is { } restTarget)
                    {
                        block.HoistLexicalBindingTargetForTdz(restTarget, blockEnvironment, isConst);
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        block.HoistLexicalBindingTargetForTdz(prop.Target, blockEnvironment, isConst);
                    }

                    if (objectBinding.RestElement is { } restObjTarget)
                    {
                        block.HoistLexicalBindingTargetForTdz(restObjTarget, blockEnvironment, isConst);
                    }

                    break;
            }
        }

        private void HoistVarDeclarations(JsEnvironment environment,
            EvaluationContext context,
            bool hoistFunctionValues = true,
            HashSet<Symbol>? lexicalNames = null,
            HashSet<Symbol>? catchParameterNames = null,
            HashSet<Symbol>? simpleCatchParameterNames = null,
            bool inBlockScope = false)
        {
            var effectiveLexicalNames = lexicalNames is null
                ? block.CollectLexicalNames()
                : [..lexicalNames];
            if (lexicalNames is not null)
            {
                effectiveLexicalNames.UnionWith(block.CollectLexicalNames());
            }

            var effectiveCatchNames = catchParameterNames is null
                ? block.CollectCatchParameterNames()
                : [..catchParameterNames];
            if (catchParameterNames is not null)
            {
                effectiveCatchNames.UnionWith(block.CollectCatchParameterNames());
            }

            var effectiveSimpleCatchNames = simpleCatchParameterNames is null
                ? block.CollectSimpleCatchParameterNames()
                : [..simpleCatchParameterNames];
            if (simpleCatchParameterNames is not null)
            {
                effectiveSimpleCatchNames.UnionWith(block.CollectSimpleCatchParameterNames());
            }

            block.HoistVarDeclarationsPass(environment,
                context,
                hoistFunctionValues,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Functions,
                inBlockScope);
            block.HoistVarDeclarationsPass(environment,
                context,
                false,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Vars,
                inBlockScope);
        }

        private void HoistVarDeclarationsPass(JsEnvironment environment,
            EvaluationContext context,
            bool hoistFunctionValues,
            HashSet<Symbol> lexicalNames,
            HashSet<Symbol> catchParameterNames,
            HashSet<Symbol> simpleCatchParameterNames,
            HoistPass pass,
            bool inBlockScope)
        {
            foreach (var statement in block.Statements)
            {
                statement.HoistFromStatement(environment, context, hoistFunctionValues, lexicalNames,
                    catchParameterNames,
                    simpleCatchParameterNames,
                    pass,
                    inBlockScope);
            }
        }

        private HashSet<Symbol> MergeLexicalNames(HashSet<Symbol> lexicalNames)
        {
            var merged = new HashSet<Symbol>(lexicalNames);
            merged.UnionWith(block.CollectLexicalNames());
            return merged;
        }

        private HashSet<Symbol> MergeCatchNames(HashSet<Symbol> catchParameterNames)
        {
            var merged = new HashSet<Symbol>(catchParameterNames);
            merged.UnionWith(block.CollectCatchParameterNames());
            return merged;
        }

        private HashSet<Symbol> MergeSimpleCatchNames(HashSet<Symbol> simpleCatchParameterNames)
        {
            var merged = new HashSet<Symbol>(simpleCatchParameterNames);
            merged.UnionWith(block.CollectSimpleCatchParameterNames());
            return merged;
        }

        private HashSet<Symbol> CollectLexicalNames()
        {
            var names = new HashSet<Symbol>();
            block.CollectLexicalNamesFromStatement(names);
            return names;
        }

        private HashSet<Symbol> CollectCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            block.CollectCatchNamesFromStatement(names);
            return names;
        }

        private HashSet<Symbol> CollectSimpleCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            block.CollectSimpleCatchNamesFromStatement(names);
            return names;
        }
    }
}
