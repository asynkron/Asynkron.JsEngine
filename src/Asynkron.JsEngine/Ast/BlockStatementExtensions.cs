using System;
using System.Collections.Generic;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

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
                : (allowAnnexB ? ScopeMode.SloppyAnnexB : ScopeMode.Sloppy);
            using var scopeHandle = context.PushScope(
                ScopeKind.Block,
                mode,
                skipAnnexBFunctionInstantiation);

            var currentFrame = context.CurrentScope;
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
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(block);

            foreach (var statement in block.Statements)
            {
                if (statement is not FunctionDeclaration functionDeclaration)
                {
                    continue;
                }

                var hasNonCatchLexical = lexicalNames.Contains(functionDeclaration.Name) &&
                                         !simpleCatchParameterNames.Contains(functionDeclaration.Name);
                var shouldCreateVarBinding = !hasNonCatchLexical &&
                                             !functionScope.HasBodyLexicalName(functionDeclaration.Name);
                var blockedByParameters = context.BlockedFunctionVarNames is { } blocked &&
                                          blocked.Contains(functionDeclaration.Name);
                var hasLexicalBeforeFunctionScope =
                    blockEnvironment.HasBindingBeforeFunctionScope(functionDeclaration.Name);
                var hasBlockingLexicalBeforeFunctionScope = hasLexicalBeforeFunctionScope &&
                                                            !simpleCatchParameterNames.Contains(functionDeclaration.Name) &&
                                                            !IsSimpleCatchParameterBinding(blockEnvironment,
                                                                functionDeclaration.Name);
                var bindingExists =
                    hasLexicalBeforeFunctionScope ||
                    functionScope.HasBodyLexicalName(functionDeclaration.Name) ||
                    (functionScope.IsGlobalFunctionScope && functionScope.HasOwnLexicalBinding(functionDeclaration.Name));

                var functionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context);

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
                if (!bindingExists || hasFunctionBinding)
                {
                    functionScope.DefineFunctionScoped(
                        functionDeclaration.Name,
                        Symbol.Undefined,
                        hasInitializer: false,
                        isFunctionDeclaration: true,
                        globalFunctionConfigurable: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                        context,
                        blocksFunctionScopeOverride: true);
                }
            }
        }
    }

extension(BlockStatement block)
    {
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
                hoistFunctionValues: false,
                effectiveLexicalNames,
                effectiveCatchNames,
                effectiveSimpleCatchNames,
                HoistPass.Vars,
                inBlockScope);
        }
    }

extension(BlockStatement block)
    {
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
                HoistFromStatement(statement, environment, context, hoistFunctionValues, lexicalNames, catchParameterNames,
                    simpleCatchParameterNames,
                    pass,
                    inBlockScope);
            }
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> MergeLexicalNames(HashSet<Symbol> lexicalNames)
        {
            var merged = new HashSet<Symbol>(lexicalNames);
            merged.UnionWith(CollectLexicalNames(block));
            return merged;
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> MergeCatchNames(HashSet<Symbol> catchParameterNames)
        {
            var merged = new HashSet<Symbol>(catchParameterNames);
            merged.UnionWith(CollectCatchParameterNames(block));
            return merged;
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> MergeSimpleCatchNames(HashSet<Symbol> simpleCatchParameterNames)
        {
            var merged = new HashSet<Symbol>(simpleCatchParameterNames);
            merged.UnionWith(CollectSimpleCatchParameterNames(block));
            return merged;
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> CollectLexicalNames()
        {
            var names = new HashSet<Symbol>();
            CollectLexicalNamesFromStatement(block, names);
            return names;
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> CollectCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectCatchNamesFromStatement(block, names);
            return names;
        }
    }

extension(BlockStatement block)
    {
        private HashSet<Symbol> CollectSimpleCatchParameterNames()
        {
            var names = new HashSet<Symbol>();
            CollectSimpleCatchNamesFromStatement(block, names);
            return names;
        }
    }

extension(BlockStatement block)
    {
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
