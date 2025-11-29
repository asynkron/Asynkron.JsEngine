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
}
