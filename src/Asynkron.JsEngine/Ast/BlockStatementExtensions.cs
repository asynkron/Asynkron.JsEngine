using System.Diagnostics;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(BlockStatement block)
    {
        private object? EvaluateBlock(
            JsEnvironment environment,
            EvaluationContext context,
            bool skipAnnexBFunctionInstantiation = false)
        {
            var scope = new JsEnvironment(environment, false, block.IsStrict);
            var result = EmptyCompletion;

            var currentMode = context.CurrentScope.Mode;
            var allowAnnexB = currentMode == ScopeMode.SloppyAnnexB &&
                              !scope.IsStrict &&
                              !block.IsStrict;
            var mode = scope.IsStrict
                ? ScopeMode.Strict
                : allowAnnexB
                    ? ScopeMode.SloppyAnnexB
                    : ScopeMode.Sloppy;
            using var scopeHandle = context.PushScope(
                ScopeKind.Block,
                mode,
                skipAnnexBFunctionInstantiation);
            using var blockActivity = Activity.Current?.StartEvaluatorActivity("Scope:Block", context, block.Source);
            blockActivity?.SetTag("js.block.strict", block.IsStrict);
            blockActivity?.SetTag("js.block.statementCount", block.Statements.Length);

            var currentFrame = context.CurrentScope;

            // Per ES spec, lexical declarations (let/const/class) must be hoisted to create
            // bindings in the TDZ (Temporal Dead Zone) BEFORE function hoisting.
            // This ensures closures that reference lexical variables will find TDZ bindings
            // and throw ReferenceError if accessed before initialization.
            foreach (var stmt in block.Statements)
            {
                if (stmt is VariableDeclaration
                    {
                        Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
                    } lexDecl)
                {
                    var isConst = lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    foreach (var declarator in lexDecl.Declarators)
                    {
                        HoistLexicalBindingTargetForTdz(block, declarator.Target, scope, isConst);
                    }
                }
            }

            if (currentFrame.SkipAnnexBInstantiation || !currentFrame.AllowAnnexB)
            {
                InstantiateLexicalBlockFunctions(block, scope, context);
            }
            if (currentFrame is { AllowAnnexB: true, SkipAnnexBInstantiation: false })
            {
                InstantiateAnnexBBlockFunctions(block, scope, context);
            }

            foreach (var statement in block.Statements)
            {
                context.ThrowIfCancellationRequested();
                var completion = EvaluateStatement(statement, scope, context);
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

        private void InstantiateAnnexBBlockFunctions(
            JsEnvironment blockEnvironment,
            EvaluationContext context)
        {
            var frame = context.CurrentScope;
            if (!frame.AllowAnnexB || frame.SkipAnnexBInstantiation)
            {
                return;
            }

            var functionScope = blockEnvironment.GetFunctionScope();
            var lexicalNames = CollectLexicalNames(block);
            var blockFunctionNames = CollectFunctionNames(block);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(block);

            foreach (var statement in block.Statements)
            {
                if (statement is not FunctionDeclaration functionDeclaration)
                {
                    continue;
                }

                // Per Annex B.3.3, only regular (non-async, non-generator) function declarations
                // are eligible for Annex B hoisting. Async functions, generators, and async generators
                // are always block-scoped and never hoisted via Annex B.
                if (functionDeclaration.Function.IsAsync || functionDeclaration.Function.IsGenerator)
                {
                    // Create a lexical binding for async/generator functions (they're block-scoped only)
                    // Pass skipInternalNameBinding: true so the function doesn't create an internal
                    // const binding for its name (the binding is handled by blockEnvironment.Define below).
                    var asyncGenFunctionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context,
                        skipInternalNameBinding: true);
                    blockEnvironment.Define(
                        functionDeclaration.Name,
                        asyncGenFunctionValue,
                        isConst: true,
                        isLexical: true,
                        blocksFunctionScopeOverride: true);
                    continue;
                }

                var hasNonCatchLexical = (lexicalNames.Contains(functionDeclaration.Name) ||
                                          blockFunctionNames.Contains(functionDeclaration.Name)) &&
                                         !simpleCatchParameterNames.Contains(functionDeclaration.Name);
                var shouldCreateVarBinding = !hasNonCatchLexical &&
                                             !functionScope.HasBodyLexicalName(functionDeclaration.Name);
                var blockedByParameters = context.BlockedFunctionVarNames is { } blocked &&
                                          blocked.Contains(functionDeclaration.Name);
                // B.3.3.1 checks for conflicting *lexical* declarations only; existing
                // var hoists in the function scope must not block Annex B var creation.
                var hasLexicalBeforeFunctionScope =
                    blockEnvironment.HasLexicalBindingBeforeFunctionScope(functionDeclaration.Name);
                var hasBlockingLexicalBeforeFunctionScope = hasLexicalBeforeFunctionScope &&
                                                            !simpleCatchParameterNames.Contains(
                                                                functionDeclaration.Name) &&
                                                            !IsSimpleCatchParameterBinding(blockEnvironment,
                                                                functionDeclaration.Name);
                var bindingExists =
                    hasLexicalBeforeFunctionScope ||
                    functionScope.HasBodyLexicalName(functionDeclaration.Name) ||
                    (functionScope.IsGlobalFunctionScope &&
                     functionScope.HasOwnLexicalBinding(functionDeclaration.Name));

                // Pass skipInternalNameBinding: true so the function doesn't create an internal
                // const binding for its name (the binding is handled by blockEnvironment.Define below).
                var functionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context,
                    skipInternalNameBinding: true);

                blockEnvironment.Define(functionDeclaration.Name, functionValue, isLexical: true,
                    blocksFunctionScopeOverride: true);

                var skipVarUpdateForExistingGlobal = false;
                if (bindingExists && functionScope.IsGlobalFunctionScope)
                {
                    try
                    {
                        if (functionScope.TryGet(functionDeclaration.Name, out var existingValue) &&
                            !ReferenceEquals(existingValue, Symbol.Undefined))
                        {
                            skipVarUpdateForExistingGlobal = true;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore lookup failures (e.g., uninitialized); allow update in that case.
                    }
                }

                if (!shouldCreateVarBinding || blockedByParameters || skipVarUpdateForExistingGlobal ||
                    hasBlockingLexicalBeforeFunctionScope)
                {
                    continue;
                }

                var hasFunctionBinding = functionScope.HasFunctionScopedBinding(functionDeclaration.Name);
                if (bindingExists && !hasFunctionBinding)
                {
                    continue;
                }

                // Remember the specific declaration so runtime copying (B.3.3.4)
                // only applies to functions that actually produced a var/global binding.
                context.AnnexBApplicableFunctions.Add(functionDeclaration);

                // Track which block-level functions received Annex B var bindings so
                // the runtime copy (B.3.3.4) only applies to applicable declarations.
                blockEnvironment.MarkAnnexBApplicableFunction(functionDeclaration.Name);

                // Annex B.3.3.3 (function/global code): create/update the var/global
                // binding with the function object when allowed. For global code,
                // CreateGlobalVarBinding is invoked with configurable:true.
                bool? globalFunctionConfigurable = functionScope.IsGlobalFunctionScope ? true : null;
                bool? globalVarConfigurable = functionScope.IsGlobalFunctionScope ? true : null;
                functionScope.DefineFunctionScoped(
                    functionDeclaration.Name,
                    functionValue,
                    true,
                    true,
                    globalFunctionConfigurable,
                    context,
                    blocksFunctionScopeOverride: true,
                    globalVarConfigurable: globalVarConfigurable,
                    allowExistingGlobalFunctionRedeclaration: true,
                    isAnnexBFunction: true,
                    canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });

                // B.3.3.4: When the declaration is evaluated, copy the block-scoped
                // function object into the var/global binding so callers see the
                // function value (while preserving existing property attributes).
                functionScope.DefineFunctionScoped(
                    functionDeclaration.Name,
                    functionValue,
                    true,
                    true,
                    globalFunctionConfigurable,
                    context,
                    blocksFunctionScopeOverride: true,
                    globalVarConfigurable: null,
                    allowExistingGlobalFunctionRedeclaration: true,
                    isAnnexBFunction: true,
                    canDelete: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false });
            }
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
                var functionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context,
                    skipInternalNameBinding: true);
                blockEnvironment.Define(
                    functionDeclaration.Name,
                    functionValue,
                    isConst: true,
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
                        blockEnvironment.Define(id.Name, JsEnvironment.Uninitialized, isLexical: true,
                            blocksFunctionScopeOverride: true, isConst: isConst);
                    }
                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } elementTarget)
                        {
                            HoistLexicalBindingTargetForTdz(block, elementTarget, blockEnvironment, isConst);
                        }
                    }
                    if (arrayBinding.RestElement is { } restTarget)
                    {
                        HoistLexicalBindingTargetForTdz(block, restTarget, blockEnvironment, isConst);
                    }
                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        HoistLexicalBindingTargetForTdz(block, prop.Target, blockEnvironment, isConst);
                    }
                    if (objectBinding.RestElement is { } restObjTarget)
                    {
                        HoistLexicalBindingTargetForTdz(block, restObjTarget, blockEnvironment, isConst);
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
                ? CollectLexicalNames(block)
                : [..lexicalNames];
            if (lexicalNames is not null)
            {
                effectiveLexicalNames.UnionWith(CollectLexicalNames(block));
            }

            var effectiveCatchNames = catchParameterNames is null
                ? CollectCatchParameterNames(block)
                : [..catchParameterNames];
            if (catchParameterNames is not null)
            {
                effectiveCatchNames.UnionWith(CollectCatchParameterNames(block));
            }

            var effectiveSimpleCatchNames = simpleCatchParameterNames is null
                ? CollectSimpleCatchParameterNames(block)
                : [..simpleCatchParameterNames];
            if (simpleCatchParameterNames is not null)
            {
                effectiveSimpleCatchNames.UnionWith(CollectSimpleCatchParameterNames(block));
            }

            HoistVarDeclarationsPass(
                block,
                environment,
                context,
                hoistFunctionValues,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Functions,
                inBlockScope);
            HoistVarDeclarationsPass(
                block,
                environment,
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
                HoistFromStatement(statement, environment, context, hoistFunctionValues, lexicalNames,
                    catchParameterNames,
                    simpleCatchParameterNames,
                    pass,
                    inBlockScope);
            }
        }

        private HashSet<Symbol> MergeLexicalNames(HashSet<Symbol> lexicalNames)
        {
            var merged = new HashSet<Symbol>(lexicalNames);
            merged.UnionWith(CollectLexicalNames(block));
            return merged;
        }

        private HashSet<Symbol> MergeCatchNames(HashSet<Symbol> catchParameterNames)
        {
            var merged = new HashSet<Symbol>(catchParameterNames);
            merged.UnionWith(CollectCatchParameterNames(block));
            return merged;
        }

        private HashSet<Symbol> MergeSimpleCatchNames(HashSet<Symbol> simpleCatchParameterNames)
        {
            var merged = new HashSet<Symbol>(simpleCatchParameterNames);
            merged.UnionWith(CollectSimpleCatchParameterNames(block));
            return merged;
        }

        private HashSet<Symbol> CollectLexicalNames()
        {
            var names = new HashSet<Symbol>();
            CollectLexicalNamesFromStatement(block, names);
            return names;
        }

        private HashSet<Symbol> CollectFunctionNames()
        {
            var names = new HashSet<Symbol>();
            foreach (var statement in block.Statements)
            {
                if (statement is FunctionDeclaration functionDeclaration)
                {
                    names.Add(functionDeclaration.Name);
                }
            }

            return names;
        }

        private HashSet<Symbol> CollectCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectCatchNamesFromStatement(block, names);
            return names;
        }

        private HashSet<Symbol> CollectSimpleCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectSimpleCatchNamesFromStatement(block, names);
            return names;
        }

        private bool HasHoistableDeclarations()
        {
            var stack = new Stack<StatementNode>();
            stack.Push(block);

            while (stack.Count > 0)
            {
                var statement = stack.Pop();
                switch (statement)
                {
                    case VariableDeclaration { Kind: VariableKind.Var }:
                    case FunctionDeclaration:
                        return true;
                    case BlockStatement b:
                        foreach (var inner in b.Statements)
                        {
                            stack.Push(inner);
                        }

                        break;
                    case IfStatement ifStatement:
                        stack.Push(ifStatement.Then);
                        if (ifStatement.Else is { } elseBranch)
                        {
                            stack.Push(elseBranch);
                        }

                        break;
                    case WhileStatement whileStatement:
                        stack.Push(whileStatement.Body);
                        break;
                    case DoWhileStatement doWhileStatement:
                        stack.Push(doWhileStatement.Body);
                        break;
                    case WithStatement withStatement:
                        stack.Push(withStatement.Body);
                        break;
                    case ForStatement forStatement:
                        if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var })
                        {
                            return true;
                        }

                        if (forStatement.Body is not null)
                        {
                            stack.Push(forStatement.Body);
                        }

                        break;
                    case ForEachStatement forEachStatement:
                        if (forEachStatement.DeclarationKind == VariableKind.Var)
                        {
                            return true;
                        }

                        stack.Push(forEachStatement.Body);
                        break;
                    case LabeledStatement labeled:
                        stack.Push(labeled.Statement);
                        break;
                    case TryStatement tryStatement:
                        stack.Push(tryStatement.TryBlock);
                        if (tryStatement.Catch is { } catchClause)
                        {
                            stack.Push(catchClause.Body);
                        }

                        if (tryStatement.Finally is { } finallyBlock)
                        {
                            stack.Push(finallyBlock);
                        }

                        break;
                    case SwitchStatement switchStatement:
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            stack.Push(switchCase.Body);
                        }

                        break;
                }
            }

            return false;
        }
    }
}
