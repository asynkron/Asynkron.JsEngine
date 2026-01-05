#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // INSTRUCTION HANDLERS: NoInlining methods for profiling visibility
        // Each handler processes one instruction kind and returns control flow action
        // Change to AggressiveInlining after profiling is complete
        // ═══════════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleStatement(
            StatementInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var stmtResult = ProfileEvaluateStatement(instruction.Statement, environment, context);

            if (_isScriptMode)
            {
                if (!stmtResult.IsUnit)
                {
                    _scriptCompletionValue = stmtResult;
                }
                else if (ShouldResetScriptCompletion(instruction.Statement))
                {
                    _scriptCompletionValue = JsValue.Undefined;
                }
            }

            var (signalAction, signalResult) = HandleContextSignals(context, environment, instruction.Next);
            switch (signalAction)
            {
                case SignalAction.Return:
                    returnValue = signalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            if (context.IsBreak || context.IsContinue)
            {
                if (_isScriptMode)
                {
                    _scriptCompletionValue = JsValue.Undefined;
                }

                var isBreak = context.IsBreak;
                var label = (context.CurrentSignal as BreakCompletionSignal)?.Label
                            ?? (context.CurrentSignal as ContinueCompletionSignal)?.Label;
                context.Clear();

                var target = FindBreakableTarget(label, isBreak);
                if (target >= 0)
                {
                    _programCounter = target;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                throw new InvalidOperationException(
                    $"No loop target found for {(isBreak ? "break" : "continue")}{(label is not null ? $" {label.Name}" : "")}");
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleThrow(
            ThrowInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var throwValue = instruction.Expression.EvaluateExpression(environment, context);

            if (_isAsync && TryHandlePendingAwait(context, out var pendingThrowResult, environment))
            {
                returnValue = pendingThrowResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var existingThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, existingThrown, environment))
                {
                    if (_programCounter != _currentInstructionIndex)
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    if (TryCatchStateRef.TryStack.Count > 0)
                    {
                        TryCatchStateRef.TryStack.Pop();
                        if (HandleAbruptCompletion(AbruptKind.Throw, existingThrown, environment))
                        {
                            returnValue = default;
                            return InstructionResult.Continue;
                        }
                    }
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(existingThrown);
            }

            if (HandleAbruptCompletion(AbruptKind.Throw, throwValue, environment))
            {
                if (_programCounter != _currentInstructionIndex)
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (TryCatchStateRef.TryStack.Count > 0)
                {
                    TryCatchStateRef.TryStack.Pop();
                    if (HandleAbruptCompletion(AbruptKind.Throw, throwValue, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }
                }
            }

            TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(throwValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEvaluateAndDiscard(
            EvaluateAndDiscardInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var evaluatedValue = ProfileEvaluateExpression(instruction.Expression, environment, context);

            if (_isScriptMode && !instruction.SuppressCompletionValue)
            {
                _scriptCompletionValue = evaluatedValue;
            }

            var (evalSignalAction, evalSignalResult) = HandleContextSignals(context, environment, instruction.Next);
            switch (evalSignalAction)
            {
                case SignalAction.Return:
                    returnValue = evalSignalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBinaryOp(
            BinaryOpInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var binLeft = instruction.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (_isAsync && TryHandlePendingAwait(context, out var pendingBinLeftResult, environment))
                {
                    returnValue = pendingBinLeftResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binRight = instruction.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (_isAsync && TryHandlePendingAwait(context, out var pendingBinRightResult, environment))
                {
                    returnValue = pendingBinRightResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, binThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binResult = ApplyBinaryOperator(instruction.Operator, binLeft, binRight, context);

            if (instruction.ResultSlot is not null)
            {
                environment.AssignJsValue(instruction.ResultSlot, binResult);
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleIncrementSlot(
            IncrementSlotInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var incCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            if (context.IsThrow)
            {
                var incThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, incThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(incThrown);
            }

            JsValue incNewJsValue;
            JsValue incOldNumericValue;

            var fastResult = ProfileIncrementMath(incCurrentValue, instruction.IsIncrement);
            if (!fastResult.IsUndefined)
            {
                incNewJsValue = fastResult;
                incOldNumericValue = incCurrentValue;
            }
            else if (incCurrentValue.IsBigInt)
            {
                var bigInt = (JsBigInt)incCurrentValue.ObjectValue!;
                incOldNumericValue = incCurrentValue;
                var incNewBigInt = instruction.IsIncrement
                    ? bigInt.Value + 1
                    : bigInt.Value - 1;
                incNewJsValue = new JsBigInt(incNewBigInt);
            }
            else
            {
                var numericJsValue = ToNumericValue(incCurrentValue, context);
                if (context.ShouldStopEvaluation)
                {
                    var incFlowThrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, incFlowThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(incFlowThrown);
                }

                if (numericJsValue.IsBigInt)
                {
                    var bigInt = (JsBigInt)numericJsValue.ObjectValue!;
                    incOldNumericValue = numericJsValue;
                    var incNewBigInt = instruction.IsIncrement
                        ? bigInt.Value + 1
                        : bigInt.Value - 1;
                    incNewJsValue = new JsBigInt(incNewBigInt);
                }
                else
                {
                    var incNumValue = numericJsValue.NumberValue;
                    incOldNumericValue = JsValueCache.GetNumberJsValue(incNumValue);
                    var incNewValue = instruction.IsIncrement
                        ? incNumValue + 1.0
                        : incNumValue - 1.0;
                    incNewJsValue = JsValueCache.GetNumberJsValue(incNewValue);
                }
            }

            ProfileAssignJsValue(environment, instruction.TargetSymbol, incNewJsValue);

            if (_isScriptMode && !instruction.SuppressCompletionValue)
            {
                _scriptCompletionValue = instruction.IsPrefix ? incNewJsValue : incOldNumericValue;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleCompoundAssignmentSlot(
            CompoundAssignmentSlotInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var compCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            if (context.IsThrow)
            {
                var compThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, compThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(compThrown);
            }

            JsValue compRhsValue;
            switch (instruction.RhsExpression)
            {
                case LiteralExpression { Value: var literalValue }:
                    compRhsValue = literalValue;
                    break;
                case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } rhsIdent:
                    if (environment.TryReadIdentifierWithSlot(rhsIdent, context, out compRhsValue))
                    {
                    }
                    else
                    {
                        compRhsValue = rhsIdent.EvaluateExpression(environment, context);
                    }
                    break;
                default:
                    compRhsValue = instruction.RhsExpression.EvaluateExpression(environment, context);
                    break;
            }

            if (context.ShouldStopEvaluation)
            {
                if (_isAsync && TryHandlePendingAwait(context, out var pendingCompResult, environment))
                {
                    returnValue = pendingCompResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var compRhsThrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, compRhsThrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(compRhsThrown);
                }
            }

            JsValue compResult;
            if (instruction.Operator == BinaryOperator.Add)
            {
                var fastAdd = ProfileCompoundAdd(compCurrentValue, compRhsValue);
                compResult = !fastAdd.IsUndefined
                    ? fastAdd
                    : ProfileApplyBinaryOperator(instruction.Operator, compCurrentValue, compRhsValue, context);
            }
            else
            {
                compResult = ProfileApplyBinaryOperator(instruction.Operator, compCurrentValue, compRhsValue, context);
            }

            ProfileAssignJsValue(environment, instruction.TargetSymbol, compResult);

            if (_isScriptMode && !instruction.SuppressCompletionValue)
            {
                _scriptCompletionValue = compResult;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleFunctionDeclaration(
            FunctionDeclarationInstruction instruction,
            out JsValue returnValue)
        {
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleClassDeclaration(
            ClassDeclarationInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var classValue = instruction.Declaration.Definition.CreateClassValue(
                environment, context, instruction.Declaration.Name);

            if (_isAsync && TryHandlePendingAwait(context, out var pendingClassResult, environment))
            {
                returnValue = pendingClassResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var classThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, classThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(classThrown);
            }

            environment.DefineJsValue(instruction.Declaration.Name, classValue,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleSimpleVariableDeclaration(
            SimpleVariableDeclarationInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var isAnonymousFunctionDefinition = instruction.Initializer is not null &&
                ExpressionNode.IsAnonymousFunctionDefinitionNode(instruction.Initializer);

            using var functionNameHint = isAnonymousFunctionDefinition
                ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                : null;

            var varValue = instruction.Initializer?.EvaluateExpression(environment, context)
                           ?? JsValue.Undefined;

            if (_isAsync && TryHandlePendingAwait(context, out var pendingVarResult, environment))
            {
                returnValue = pendingVarResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var varThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, varThrown, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = instruction.Next;
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(varThrown);
            }

            if (context.IsReturn)
            {
                var varReturnValue = context.FlowValue;
                context.ClearReturn();
                if (!HandleAbruptCompletion(AbruptKind.Return, varReturnValue, environment))
                {
                    returnValue = CompleteReturn(varReturnValue);
                    return InstructionResult.Return;
                }

                if (_programCounter == _currentInstructionIndex)
                {
                    _programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            if (context.IsYield)
            {
                var varYieldedValue = context.FlowValue;
                var varIteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
                RecordYield(context, environment);
                context.Clear();
                _state = GeneratorState.Suspended;
                returnValue = varIteratorResultObject is not null
                    ? JsValue.FromObjectUnsafe(varIteratorResultObject)
                    : CreateIteratorResult(varYieldedValue, false);
                return InstructionResult.Return;
            }

            if (instruction.VarKind == VariableKind.Var)
            {
                environment.EnsureFunctionScopedVarBinding(instruction.TargetSymbol, context);
                if (instruction.Initializer is not null)
                {
                    if (!environment.TryAssignBlockedBinding(instruction.TargetSymbol, varValue))
                    {
                        if (instruction.IsScriptLevel)
                        {
                            environment.AssignJsValue(instruction.TargetSymbol, varValue);
                        }
                        else
                        {
                            environment.DefineOrAssignJsValue(instruction.TargetSymbol, varValue);
                        }
                    }
                }
            }
            else
            {
                var isConst = instruction.VarKind == VariableKind.Const;
#pragma warning disable CS0162
                if (JsEngineConstants.TraceIrExecution && _realmState.Logger is not null)
                {
                    ExecutionPlanPrinter.TraceDefine(
                        _realmState.Logger,
                        instruction.VarKind.ToString(),
                        instruction.TargetSymbol.Name,
                        varValue.ToString() ?? "?",
                        environment.Depth,
                        environment.ScopeId,
                        environment.GetHashCode());
                }
#pragma warning restore CS0162
                environment.DefineJsValue(instruction.TargetSymbol, varValue,
                    isConst, isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBreakableEnter(
            BreakableEnterInstruction instruction,
            out JsValue returnValue)
        {
            if (instruction.ConstructKind == BreakableKind.ResetsCompletionValue)
            {
                ResetCompletionValue();
            }

            BreakableStateRef.BreakableStack.Push(new BreakableFrame(
                instruction.Label,
                instruction.BreakTarget,
                instruction.ContinueTarget));

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBreakableExit(
            BreakableExitInstruction instruction,
            out JsValue returnValue)
        {
            if (BreakableStateRef.BreakableStack.Count > 0)
            {
                BreakableStateRef.BreakableStack.Pop();
            }

            FinalizeCompletionValue();
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEnterTry(
            EnterTryInstruction instruction,
            JsEnvironment environment,
            out JsValue returnValue)
        {
            ResetCompletionValue();
            PushTryFrame(instruction, environment);
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleLeaveTry(
            LeaveTryInstruction instruction,
            out JsValue returnValue)
        {
            CompleteTryNormally(instruction.Next);
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleSetCompletionValue(
            SetCompletionValueInstruction instruction,
            out JsValue returnValue)
        {
            if (_isScriptMode)
            {
                _scriptCompletionValue = JsValue.Undefined;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBreak(
            BreakInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            if (HandleAbruptCompletion(AbruptKind.Break, instruction.TargetIndex, environment))
            {
                if (_programCounter == _currentInstructionIndex && TryCatchStateRef.TryStack.Count > 0)
                {
                    var frame = TryCatchStateRef.TryStack.Peek();
                    if (frame.EndFinallyIndex >= 0)
                    {
                        _programCounter = frame.EndFinallyIndex;
                    }
                }

                returnValue = default;
                return InstructionResult.Continue;
            }

            if (instruction.TargetScopeId >= 0)
            {
                var targetScopeId = instruction.TargetScopeId;
                var walkEnv = environment;
                while (walkEnv.ScopeId != targetScopeId && walkEnv.Enclosing != null)
                {
                    walkEnv = walkEnv.Enclosing;
                }

                if (walkEnv.ScopeId == targetScopeId)
                {
                    environment = walkEnv;
                }
            }

            _programCounter = instruction.TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleContinue(
            ContinueInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            if (HandleAbruptCompletion(AbruptKind.Continue, instruction.TargetIndex, environment))
            {
                if (_programCounter == _currentInstructionIndex && TryCatchStateRef.TryStack.Count > 0)
                {
                    var frame = TryCatchStateRef.TryStack.Peek();
                    if (frame.EndFinallyIndex >= 0)
                    {
                        _programCounter = frame.EndFinallyIndex;
                    }
                }

                returnValue = default;
                return InstructionResult.Continue;
            }

            if (instruction.TargetScopeId >= 0)
            {
                var targetScopeId = instruction.TargetScopeId;
                var walkEnv = environment;
                while (walkEnv.ScopeId != targetScopeId && walkEnv.Enclosing != null)
                {
                    walkEnv = walkEnv.Enclosing;
                }

                if (walkEnv.ScopeId == targetScopeId)
                {
                    environment = walkEnv;
                }
            }

            _programCounter = instruction.TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleReturn(
            ReturnInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var returnVal = instruction.ReturnExpression?.EvaluateExpression(environment, context) ?? JsValue.Undefined;

            if (_isAsync && TryHandlePendingAwait(context, out var pendingReturnResult, environment))
            {
                returnValue = pendingReturnResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var pendingThrow = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, pendingThrow, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = instruction.Next;
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(pendingThrow);
            }

            if (context.IsReturn)
            {
                var pendingReturn = context.FlowValue;
                context.ClearReturn();
                returnVal = pendingReturn;
            }

            var wasInsideScheduledFinally = IsInsideScheduledFinally();

            if (HandleAbruptCompletionJsValue(AbruptKind.Return, returnVal, environment))
            {
                if (wasInsideScheduledFinally)
                {
                    returnValue = CompleteReturn(returnVal);
                    return InstructionResult.Return;
                }

                if (_programCounter == _currentInstructionIndex)
                {
                    _programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            returnValue = CompleteReturn(returnVal);
            return InstructionResult.Return;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleJumpSwitch(
            JumpInstruction instruction,
            out JsValue returnValue)
        {
            _programCounter = instruction.TargetIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBranchSwitch(
            BranchInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var testValue = instruction.Condition.EvaluateExpression(environment, context);

            if (_isAsync && TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var thrownValue = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownValue, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownValue);
            }

            _programCounter = testValue.IsTruthy ? instruction.ConsequentIndex : instruction.AlternateIndex;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleBranchFastPath(
            BranchInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Fast path for simple binary comparisons (e.g., i < 1000000)
            JsValue testValue;
            var usedFastPath = false;

            if (instruction.Condition is BinaryExpression
                {
                    Operator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or
                    BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                } binCond)
            {
                // Profiling wrappers - NoInlining so they show up in profiler
                if (ProfileReadOperand(environment, context, binCond.Left, out var leftVal) &&
                    ProfileReadOperand(environment, context, binCond.Right, out var rightVal))
                {
                    // Comparison via profiling wrapper
                    testValue = ProfileBranchCompare(binCond.Operator, leftVal, rightVal, context);
                    usedFastPath = true;
                }
                else
                {
                    testValue = default;
                }
            }
            else
            {
                testValue = default;
            }

            if (!usedFastPath)
            {
                testValue = ProfileEvaluateExpression(instruction.Condition, environment, context);
            }

            // Check for pending await (async code) - skip entirely for sync functions
            if (_isAsync && TryHandlePendingAwait(context, out var pendingBranchResult, environment))
            {
                returnValue = pendingBranchResult;
                return InstructionResult.Return;
            }

            // Check for throw
            if (TryHandleContextThrow(context, environment))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Normal path: branch based on condition (with profiling)
            _programCounter = ProfileBranchDecision(
                testValue.IsTruthy,
                instruction.ConsequentIndex,
                instruction.AlternateIndex);
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEnterCatch(
            EnterCatchInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            ResetCompletionValue();

            var thrownValue = JsValue.Undefined;
            if (TryCatchStateRef.TryStack.Count > 0)
            {
                thrownValue = TryCatchStateRef.TryStack.Peek().ThrownValue;
            }

            var catchEnv = new JsEnvironment(
                environment,
                false,
                environment.IsStrict,
                null,
                "catch");

            if (instruction.SlotCount > 0)
            {
                catchEnv.InitializeSlots(instruction.SlotCount, instruction.ScopeId);
            }

            if (instruction.CatchParameterSymbol is { } param)
            {
                catchEnv.DefineJsValue(param, thrownValue, false, isLexicalBinding: true);
            }

            environment = catchEnv;
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEnterCatchWithDestructuring(
            EnterCatchWithDestructuringInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            ResetCompletionValue();

            var thrownValue = JsValue.Undefined;
            if (TryCatchStateRef.TryStack.Count > 0)
            {
                thrownValue = TryCatchStateRef.TryStack.Peek().ThrownValue;
            }

            var catchEnv = new JsEnvironment(
                environment,
                false,
                environment.IsStrict,
                null,
                "catch");

            if (instruction.SlotCount > 0)
            {
                catchEnv.InitializeSlots(instruction.SlotCount, instruction.ScopeId);
            }

            instruction.BindingPattern.DefineBindingTarget(thrownValue, catchEnv, context, false);

            if (context.ShouldStopEvaluation)
            {
                if (context.IsThrow)
                {
                    var exception = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, exception, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(exception);
                }
            }

            environment = catchEnv;
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEndFinally(
            EndFinallyInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            if (TryCatchStateRef.TryStack.Count == 0)
            {
                _programCounter = instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            var completedFrame = TryCatchStateRef.TryStack.Pop();
            var pending = completedFrame.PendingCompletion;

            if (pending.Kind == AbruptKind.None)
            {
                RestoreCompletionValueFromFinally(completedFrame);
                var target = pending.ResumeTarget >= 0 ? pending.ResumeTarget : instruction.Next;
                _programCounter = target;
                returnValue = default;
                return InstructionResult.Continue;
            }

            if (pending.Kind == AbruptKind.Return)
            {
                if (HandleAbruptCompletion(AbruptKind.Return, pending.Value, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                var pendingJs = pending.Value is JsValue pjs ? pjs : JsValue.FromObjectUnsafe(pending.Value);
                returnValue = CompleteReturn(pendingJs);
                return InstructionResult.Return;
            }

            if (pending.Kind == AbruptKind.Break || pending.Kind == AbruptKind.Continue)
            {
                RestoreCompletionValueFromFinally(completedFrame);
                if (HandleAbruptCompletion(pending.Kind, pending.Value, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                _programCounter = pending.Value is int idx ? idx : instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            if (HandleAbruptCompletion(AbruptKind.Throw, pending.Value, environment))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            TryCatchStateRef.TryStack.Clear();
            var throwJs = pending.Value is JsValue tjs ? tjs : JsValue.FromObjectUnsafe(pending.Value);
            throw new ThrowSignal(throwJs);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleEnterWith(
            EnterWithInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var objValueJs = instruction.ObjectExpression.EvaluateExpression(environment, context);

            if (_isAsync && TryHandlePendingAwait(context, out var pendingWithResult, environment))
            {
                returnValue = pendingWithResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var thrownWith = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownWith, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownWith);
            }

            if (TryConvertToWithBindingObject(objValueJs, context, out var withObject))
            {
                var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict,
                    instruction.ObjectExpression.Source, "with", withObject);
                StoreSymbolValue(_executionEnvironment!, instruction.WithScopeSlot, withEnv);
                WithStateRef.ActiveWithScopes.Push(instruction.WithScopeSlot);
                environment = withEnv;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleLeaveWith(
            LeaveWithInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            if (WithStateRef.ActiveWithScopes.Count > 0 &&
                ReferenceEquals(WithStateRef.ActiveWithScopes.Peek(), instruction.WithScopeSlot))
            {
                WithStateRef.ActiveWithScopes.Pop();
            }

            if (TryGetSymbolValueJsValue(_executionEnvironment!, instruction.WithScopeSlot, out var storedEnvValue) &&
                storedEnvValue.TryGetObject<JsEnvironment>(out var storedWithEnv))
            {
                environment = storedWithEnv.Enclosing ?? environment;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleIteratorClose(
            IteratorCloseInstruction instruction,
            JsEnvironment environment,
            out JsValue returnValue)
        {
            if (TryGetSymbolValueJsValue(environment, instruction.IteratorSlot, out var iterStateValue) &&
                iterStateValue.TryGetObject<IteratorDriverState>(out var iterState) &&
                iterState.IteratorObject is { } iteratorObj)
            {
                if (!iterState.HasEnteredLoop)
                {
                    iterState.MarkIteratorClosed();
                    _programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                iterState.MarkIteratorClosed();

                var hasPendingThrow = false;
                if (TryCatchStateRef.TryStack.Count > 0)
                {
                    var topFrame = TryCatchStateRef.TryStack.Peek();
                    hasPendingThrow = topFrame.PendingCompletion.Kind == AbruptKind.Throw;
                }

                try
                {
                    iteratorObj.IteratorClose(EnsureEvaluationContext(), hasPendingThrow);
                }
                catch (ThrowSignal closeThrown)
                {
                    if (hasPendingThrow)
                    {
                        _programCounter = instruction.Next;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    if (HandleAbruptCompletion(AbruptKind.Throw, closeThrown.ThrownValue, environment))
                    {
                        _programCounter = instruction.Next;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw;
                }
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleYield(
            YieldInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var yieldedValue = JsValue.Undefined;
            if (instruction.YieldExpression is not null)
            {
                yieldedValue = instruction.YieldExpression.EvaluateExpression(environment, context);

                if (_isAsync && TryHandlePendingAwait(context, out var pendingYieldResult, environment))
                {
                    returnValue = pendingYieldResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (context.IsYield)
                {
                    yieldedValue = context.FlowValue;
                    var nestedIteratorResult = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
                    context.Clear();
                    _programCounter = _currentInstructionIndex;
                    RecordYield(context, environment);
                    _state = GeneratorState.Suspended;
                    returnValue = nestedIteratorResult is not null
                        ? JsValue.FromObjectUnsafe(nestedIteratorResult)
                        : CreateIteratorResult(yieldedValue, false);
                    return InstructionResult.Return;
                }
            }

            _programCounter = instruction.Next;
            RecordYield(context, environment);
            _state = GeneratorState.Suspended;
            returnValue = CreateIteratorResult(yieldedValue, false);
            return InstructionResult.Return;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleStoreResumeValue(
            StoreResumeValueInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var (resumeKind, resumePayload) = ConsumeResumeValue();
            if (resumeKind == ResumePayloadKind.Throw)
            {
                context.SetThrow(resumePayload);
            }
            else if (resumeKind == ResumePayloadKind.Return)
            {
                context.SetReturn(resumePayload);
            }
            else if (instruction.TargetSymbol is { } resumeSymbol)
            {
                StoreSymbolValueJsValue(environment, resumeSymbol, resumePayload);
            }

            if (context.IsThrow)
            {
                var thrownPayload = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, thrownPayload, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = instruction.Next;
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrownPayload);
            }

            if (context.IsReturn)
            {
                var resumeReturnValue = context.FlowValue;
                context.ClearReturn();
                if (HandleAbruptCompletion(AbruptKind.Return, resumeReturnValue, environment))
                {
                    if (_programCounter == _currentInstructionIndex)
                    {
                        _programCounter = instruction.Next;
                    }
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                returnValue = CompleteReturn(resumeReturnValue);
                return InstructionResult.Return;
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandlePushEnvironment(
            PushEnvironmentInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            var hasIterationBindings = !instruction.PerIterationBindings.IsDefaultOrEmpty;
            var isSubsequentIteration =
                hasIterationBindings &&
                ((instruction.ScopeId >= 0 && environment.ScopeId == instruction.ScopeId) ||
                 (instruction.ScopeId < 0 && environment.Description == "scope" && environment.Enclosing != null));

            if (isSubsequentIteration &&
                instruction.AllowPooling &&
                !instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                _programCounter = instruction.Next;
                returnValue = default;
                return InstructionResult.Continue;
            }

            JsEnvironment loopScope;
            JsEnvironment? previousIterEnv = null;

            if (isSubsequentIteration)
            {
                previousIterEnv = environment;
                loopScope = environment.Enclosing!;
            }
            else
            {
                loopScope = environment;
            }

            var allowPooling = instruction.AllowPooling;
            var description = instruction.PerIterationBindings.IsDefaultOrEmpty ? "loop-scope" : "scope";
            var newIterationEnv = allowPooling
                ? JsEnvironmentPool.Rent(loopScope, false, false, null, description, logger: _realmState.Logger)
                : new JsEnvironment(loopScope, false, false, null, description);

            if (instruction is { SlotCount: > 0, ScopeId: >= 0 })
            {
                newIterationEnv.InitializeSlots(instruction.SlotCount, instruction.ScopeId);
                if (!instruction.SlotMap.IsEmpty)
                {
                    newIterationEnv.SetSlotMap(instruction.SlotMap);
                }

                if (instruction.LexicalBindings is { Count: > 0 })
                {
                    newIterationEnv.MarkSlotsLexicalUninitialized(instruction.LexicalBindings);
                }
            }

            if (previousIterEnv != null && !instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                var useSlotCopy = newIterationEnv.HasSlots &&
                                  previousIterEnv.HasSlots &&
                                  !instruction.SlotMap.IsEmpty;

                if (useSlotCopy)
                {
                    foreach (var binding in instruction.PerIterationBindings)
                    {
                        if (instruction.SlotMap.TryGetValue(binding, out var slotIndex))
                        {
                            var value = previousIterEnv.GetSlotRef(slotIndex);
                            newIterationEnv.SetSlotDirect(slotIndex, value);
                        }
                    }
                }
                else
                {
                    foreach (var binding in instruction.PerIterationBindings)
                    {
                        if (previousIterEnv.TryGetJsValueWithConst(binding, out var value, out var isConst))
                        {
                            newIterationEnv.DefineJsValue(binding, value, isConst, isLexicalBinding: true);
                        }
                    }
                }

                if (allowPooling && !ReferenceEquals(previousIterEnv, IteratorStateRef.ResumedWithEnvironment))
                {
                    JsEnvironmentPool.Return(previousIterEnv, _realmState.Logger);
                }
            }
            else if (!instruction.PerIterationBindings.IsDefaultOrEmpty)
            {
                foreach (var binding in instruction.PerIterationBindings)
                {
                    if (loopScope.TryGetJsValueWithConst(binding, out var value, out var isConst))
                    {
                        newIterationEnv.DefineJsValue(binding, value, isConst, isLexicalBinding: true);
                    }
                }
            }

            IteratorStateRef.ResumedWithEnvironment = null;

            _realmState.Logger?.LogInformation(
                "PushEnv: old.ScopeId={OldScope} new.ScopeId={NewScope} loopScope.ScopeId={LoopScope} parent={Parent}",
                environment.ScopeId,
                newIterationEnv.ScopeId,
                loopScope.ScopeId,
                newIterationEnv.Enclosing?.ScopeId);

            environment = newIterationEnv;
            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandlePopEnvironment(
            PopEnvironmentInstruction instruction,
            ref JsEnvironment environment,
            out JsValue returnValue)
        {
            var shouldPop = instruction.ScopeId >= 0
                ? environment.ScopeId == instruction.ScopeId
                : environment.Description is "scope" or "loop-scope" && environment.Enclosing != null;

            if (shouldPop)
            {
                var envToPop = environment;
                environment = environment.Enclosing!;

                if (instruction.AllowPooling)
                {
                    JsEnvironmentPool.Return(envToPop, _realmState.Logger);
                }
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleYieldStar(
            YieldStarInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var currentIndex = _programCounter;
            if (!TryGetSymbolValueJsValue(environment, instruction.StateSlotSymbol,
                    out var stateValue) ||
                !stateValue.TryGetObject<YieldStarState>(out var yieldStarState))
            {
                yieldStarState = new YieldStarState();
                StoreSymbolValue(environment, instruction.StateSlotSymbol, yieldStarState);
            }

            if (yieldStarState.PendingAbrupt != AbruptKind.None &&
                AsyncStateRef.PendingResumeKind is not ResumePayloadKind.Throw
                    and not ResumePayloadKind.Return)
            {
                var pendingKind = yieldStarState.PendingAbrupt;
                var pendingValue = yieldStarState.PendingValue;
                yieldStarState.PendingAbrupt = AbruptKind.None;
                yieldStarState.PendingValue = JsValue.Undefined;
                yieldStarState.State = null;
                yieldStarState.AwaitingResume = false;
                environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);

                switch (pendingKind)
                {
                    case AbruptKind.Throw
                        when HandleAbruptCompletion(AbruptKind.Throw, pendingValue, environment):
                        returnValue = default;
                        return InstructionResult.Continue;
                    case AbruptKind.Throw:
                        TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(pendingValue);
                    case AbruptKind.Return when HandleAbruptCompletion(AbruptKind.Return,
                        pendingValue, environment):
                        returnValue = default;
                        return InstructionResult.Continue;
                    case AbruptKind.Return:
                        returnValue = CompleteReturn(pendingValue);
                        return InstructionResult.Return;
                }
            }

            var isFirstYieldStarEntry = yieldStarState.State is null;

            if (yieldStarState.State is null)
            {
                _realmState.Logger?.LogInformation("YieldStar: Creating new DelegatedState");
                var yieldStarIterableValue =
                    instruction.IterableExpression.EvaluateExpression(environment, context);
                if (_isAsync && TryHandlePendingAwait(context, out var pendingYieldStarResult, environment))
                {
                    returnValue = pendingYieldStarResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                yieldStarState.State = CreateDelegatedState(yieldStarIterableValue, context);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                yieldStarState.AwaitingResume = false;
            }
            else
            {
                _realmState.Logger?.LogInformation(
                    "YieldStar: Reusing existing DelegatedState, AwaitingResume={Awaiting}",
                    yieldStarState.AwaitingResume);
            }

            while (true)
            {
                var sendValue = JsValue.Undefined;
                var propagateThrow = false;
                var propagateReturn = false;

                if (isFirstYieldStarEntry)
                {
                    sendValue = JsValue.Undefined;
                    isFirstYieldStarEntry = false;
                }
                else if (yieldStarState.AwaitingResume)
                {
                    var (delegatedResumeKind, delegatedResumePayload) = ConsumeResumeValue();
                    switch (delegatedResumeKind)
                    {
                        case ResumePayloadKind.Throw:
                            propagateThrow = true;
                            sendValue = delegatedResumePayload;
                            break;
                        case ResumePayloadKind.Return:
                            propagateReturn = true;
                            sendValue = delegatedResumePayload;
                            break;
                        default:
                            sendValue = delegatedResumePayload;
                            break;
                    }
                }

                var iteratorResult = yieldStarState.State!.MoveNext(
                    sendValue,
                    propagateThrow,
                    propagateReturn,
                    context,
                    out _);

                if (context.IsThrow)
                {
                    var thrown = context.FlowValue;
                    context.Clear();
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
                    {
                        break;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(thrown);
                }

                if (iteratorResult.IsDelegatedCompletion)
                {
                    var isThrowCompletion = propagateThrow || iteratorResult.PropagateThrow;
                    var pendingKind = isThrowCompletion ? AbruptKind.Throw : AbruptKind.Return;
                    var abruptValue = iteratorResult.Value;

                    if (!iteratorResult.Done)
                    {
                        yieldStarState.PendingAbrupt = pendingKind;
                        yieldStarState.PendingValue = sendValue;
                        yieldStarState.AwaitingResume = true;
                        _programCounter = currentIndex;
                        RecordYield(context, environment);
                        _state = GeneratorState.Suspended;
                        returnValue = iteratorResult.IteratorResultObject is not null
                            ? JsValue.FromObjectUnsafe(iteratorResult.IteratorResultObject)
                            : CreateIteratorResult(iteratorResult.Value, false);
                        return InstructionResult.Return;
                    }

                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);

                    if (pendingKind == AbruptKind.Throw)
                    {
                        if (HandleAbruptCompletion(AbruptKind.Throw, abruptValue, environment))
                        {
                            break;
                        }

                        TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(abruptValue);
                    }

                    if (HandleAbruptCompletion(AbruptKind.Return, abruptValue, environment))
                    {
                        break;
                    }

                    returnValue = CompleteReturn(abruptValue);
                    return InstructionResult.Return;
                }

                if (propagateThrow && iteratorResult.Done)
                {
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (instruction.ResultSlotSymbol is { } throwResultSlot)
                    {
                        StoreSymbolValue(environment, throwResultSlot, iteratorResult.Value);
                    }

                    _programCounter = instruction.Next;
                    break;
                }

                if (iteratorResult.Done && !propagateThrow && !propagateReturn)
                {
                    yieldStarState.State = null;
                    yieldStarState.AwaitingResume = false;
                    environment.AssignJsValue(instruction.StateSlotSymbol, JsValue.Null);
                    if (instruction.ResultSlotSymbol is { } resultSlot)
                    {
                        StoreSymbolValue(environment, resultSlot, iteratorResult.Value);
                    }

                    _programCounter = instruction.Next;
                    break;
                }

                yieldStarState.AwaitingResume = true;
                _programCounter = currentIndex;
                RecordYield(context, environment);
                _state = GeneratorState.Suspended;
                if (iteratorResult.IteratorResultObject is { } originalResult)
                {
                    returnValue = JsValue.FromObjectUnsafe(originalResult);
                    return InstructionResult.Return;
                }

                var resultDone = propagateReturn && iteratorResult.Done;
                returnValue = CreateIteratorResult(iteratorResult.Value, resultDone);
                return InstructionResult.Return;
            }

            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleIteratorInit(
            IteratorInitInstruction instruction,
            JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var iterableEnv = environment;
            if (!instruction.TdzBindings.IsDefaultOrEmpty)
            {
                iterableEnv = new JsEnvironment(environment, false, false,
                    instruction.IterableExpression.Source, "for-of-head-tdz");
                foreach (var tdzSymbol in instruction.TdzBindings)
                {
                    iterableEnv.DefineJsValue(tdzSymbol, JsValue.Uninitialized,
                        instruction.TdzIsConst, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true);
                }
            }

            var iterableValue = instruction.IterableExpression.EvaluateExpression(iterableEnv, context);
            if (_isAsync && TryHandlePendingAwait(context, out var pendingIteratorResult, environment))
            {
                returnValue = pendingIteratorResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var initThrown = context.FlowValue;
                context.Clear();
                if (HandleAbruptCompletion(AbruptKind.Throw, initThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(initThrown);
            }

            var iteratorState = CreateIteratorDriverState(iterableValue, instruction.IteratorKind, context);

            var iteratorEnv = environment;
            var walkCount = 0;
            if (instruction.IteratorSlotIndex >= 0)
            {
                while (iteratorEnv is not null &&
                       (iteratorEnv.ScopeId != _plan.RootScopeId ||
                        !iteratorEnv.HasSlots ||
                        iteratorEnv._slots!.Length <= instruction.IteratorSlotIndex))
                {
                    iteratorEnv = iteratorEnv.Enclosing;
                    walkCount++;
                    if (walkCount > 1000)
                    {
                        break;
                    }
                }

                iteratorEnv ??= environment;
            }

            if (instruction.IteratorSlotIndex >= 0 && iteratorEnv.HasSlots)
            {
                iteratorState.IteratorVariable = new JsVariable(iteratorEnv, instruction.IteratorSlotIndex);
            }

            iteratorState.LoopScopeEnvironment = environment;
            IteratorStateRef.CurrentDriverState = iteratorState;

            StoreValueBySlot(iteratorEnv, instruction.IteratorSlot,
                instruction.IteratorSlotIndex,
                JsValue.FromObjectUnsafe(iteratorState));

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleIteratorMoveNext(
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var iteratorIndex = _programCounter;

            // Use cached driver state for scope-correct access from child scopes
            // (The iterator slot is in the loop scope, but we may be in a per-iteration child scope)
            var driverState = IteratorStateRef.CurrentDriverState;

            if (driverState is null)
            {
                // Fallback: try to get iterator state from the correct scope.
                // The iterator slot is stored in function/module scope (not per-iteration envs),
                // so we need to walk up the chain to find it, similar to IteratorInit.
                var slotEnv = environment;
                var slotIdx = instruction.IteratorSlotIndex;

                // Walk up to find the scope with the right slots
                // Skip per-iteration envs since iterator temps are stored
                // in the function's root scope (RootScopeId), not per-iteration envs
                if (slotIdx >= 0)
                {
                    var slotWalkCount = 0;
                    while (slotEnv != null &&
                           (slotEnv.ScopeId != _plan.RootScopeId ||
                            !slotEnv.HasSlots ||
                            slotEnv._slots!.Length <= slotIdx))
                    {
                        slotEnv = slotEnv.Enclosing;
                        slotWalkCount++;
                        if (slotWalkCount > 100)
                        {
                            break;
                        }
                    }

                    slotEnv ??= environment;
                }

                if (slotEnv is null || !TryGetValueBySlot(slotEnv,
                        instruction.IteratorSlot,
                        slotIdx, out var iteratorStateValue))
                {
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (!iteratorStateValue.TryGetObject(out driverState))
                {
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                IteratorStateRef.CurrentDriverState = driverState;
            }

            // Get JsVariables directly from driverState (O(1) access, no dictionary lookup)
            var iterVar = driverState.IteratorVariable;
            var valueVar = driverState.ValueVariable;

            // Capture value JsVariable on first execution (while still in loop scope)
            // IMPORTANT: Use the loop scope environment (from iterVar) rather than the current
            // environment, which may be a stale per-iteration environment from a previous outer
            // loop iteration. The value slot is allocated in the same scope as the iterator.
            if (!valueVar.IsValid && instruction.ValueSlotIndex >= 0)
            {
                // Use the iterator's environment since value slot is in the same scope
                var loopScopeEnv = iterVar.IsValid ? iterVar.Environment : environment;
                if (loopScopeEnv.HasSlots && loopScopeEnv._slots!.Length > instruction.ValueSlotIndex)
                {
                    valueVar = new JsVariable(loopScopeEnv, instruction.ValueSlotIndex);
                    driverState.ValueVariable = valueVar;
                }
            }

            if (!driverState.IsAsyncIterator)
            {
                return HandleSyncIteratorMoveNext(instruction, ref environment, context, driverState, iterVar, valueVar, out returnValue);
            }

            return HandleAsyncIteratorMoveNext(instruction, ref environment, context, driverState, iterVar, valueVar, iteratorIndex, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleSyncIteratorMoveNext(
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            IteratorDriverState driverState,
            JsVariable iterVar,
            JsVariable valueVar,
            out JsValue returnValue)
        {
            // If we're resuming this iterator site with an abrupt completion (return/throw),
            // propagate it immediately instead of calling iterator.next() again.
            var pendingResumeKind = AsyncStateRef.PendingResumeKind;
            if (pendingResumeKind is ResumePayloadKind.Throw or ResumePayloadKind.Return)
            {
                var (kind, payload) = ConsumeResumeValue();
                var abruptKind = kind == ResumePayloadKind.Return
                    ? AbruptKind.Return
                    : AbruptKind.Throw;

                if (HandleAbruptCompletion(abruptKind, payload, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                if (abruptKind == AbruptKind.Throw)
                {
                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(payload);
                }

                returnValue = CompleteReturn(payload);
                return InstructionResult.Return;
            }

            JsValue currentValue;
            if (driverState.IteratorObject is { } iteratorObj)
            {
                driverState.NextMethod ??= iteratorObj.GetIteratorNextCallable(context);
                var nextResult = iteratorObj.InvokeIteratorNext(
                    driverState.NextMethod,
                    context: context,
                    callingEnvironment: environment);
                // Handle case where nextResult is already a boxed JsValue
                if (!nextResult.TryGetObject<IJsPropertyAccessor>(out var resultObj))
                {
                    // Per ES spec 7.4.2: if result is not an object, throw TypeError
                    var typeError = StandardLibrary.CreateTypeError(
                        "Iterator result is not an object",
                        context, context.RealmState);
                    if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(typeError);
                }

                var done = resultObj.TryGetProperty("done", out var doneValue) &&
                           JsOps.ToBoolean(doneValue);
                if (done)
                {
                    // When breaking out of iterator, restore environment to enclosing scope.
                    // This is critical for nested loops: after async resume, environment was
                    // reset to function scope, and we need to restore it to the loop scope
                    // so that variable lookups (like loop counter increments) work correctly.
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv)
                    {
                        environment = enclosingEnv;
                    }

                    // Clear driver state to prevent outer loop's CreateIterationEnv from
                    // incorrectly updating this driver's CurrentIterationEnvironment.
                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                // yielded is already a JsValue from TryGetProperty
                currentValue = resultObj.TryGetProperty("value", out var yielded)
                    ? yielded
                    : JsValue.Undefined;

                // Mark that we've successfully entered the loop (next() succeeded).
                // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
                // if we've entered the loop body, not if next() itself throws.
                driverState.HasEnteredLoop = true;
            }
            else if (driverState.Enumerator is { } enumerator)
            {
                if (!enumerator.MoveNext())
                {
                    // Restore environment to enclosing scope when iterator exhausted
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv2)
                    {
                        environment = enclosingEnv2;
                    }

                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                currentValue = enumerator.Current;

                // Mark that we've successfully entered the loop (enumerator succeeded).
                driverState.HasEnteredLoop = true;
            }
            else
            {
                // Restore environment to enclosing scope when no iterator
                if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv3)
                {
                    environment = enclosingEnv3;
                }

                IteratorStateRef.CurrentDriverState = null;
                _programCounter = instruction.BreakIndex;
                returnValue = default;
                return InstructionResult.Continue;
            }

            // Use JsVariable for scope-correct access (value slot is in loop scope)
            _realmState.Logger?.LogInformation(
                "SyncIterator StoreValue: valueVar.IsValid={Valid} currentEnv.ScopeId={CurScope} slot={Slot} value={Value}",
                valueVar.IsValid,
                environment.ScopeId,
                instruction.ValueSlot.Name,
                currentValue.Kind);
            if (valueVar.IsValid)
            {
                valueVar.Write(currentValue);
                // Also create binding for symbol-based identifier lookup in loop body
                valueVar.Environment.DefineOrAssignJsValue(
                    instruction.ValueSlot, currentValue);
                _realmState.Logger?.LogInformation(
                    "SyncIterator StoreValue: wrote to valueVar.Environment.ScopeId={Scope}",
                    valueVar.Environment.ScopeId);
            }
            else
            {
                StoreValueBySlot(environment, instruction.ValueSlot,
                    instruction.ValueSlotIndex, currentValue);
                _realmState.Logger?.LogInformation(
                    "SyncIterator StoreValue: wrote via StoreValueBySlot to env.ScopeId={Scope}",
                    environment.ScopeId);
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private InstructionResult HandleAsyncIteratorMoveNext(
            IteratorMoveNextInstruction instruction,
            ref JsEnvironment environment,
            EvaluationContext context,
            IteratorDriverState driverState,
            JsVariable iterVar,
            JsVariable valueVar,
            int iteratorIndex,
            out JsValue returnValue)
        {
            var awaitedValue = JsValue.Undefined;
            var awaitedNextResult = JsValue.Undefined;
            var hasAwaitedNextResult = false;
            var skipToStoreValue = false;

            // If we're resuming after a pending await from this
            // iterator site, consume the resume payload and treat
            // it as the awaited result instead of calling into the
            // iterator again.
            if (driverState.AwaitingNextResult || driverState.AwaitingValue)
            {
                var awaitingValue = driverState.AwaitingValue;
                driverState.AwaitingNextResult = false;
                driverState.AwaitingValue = false;
                var (forAwaitResumeKind, forAwaitResumePayload) = ConsumeResumeValue();
                // Use JsVariable for scope-correct access (iterator slot is in loop scope)
                var iterStateValue = driverState.AsJsValue;
                if (iterVar.IsValid)
                {
                    iterVar.Write(iterStateValue);
                }
                else
                {
                    StoreValueBySlot(environment, instruction.IteratorSlot,
                        instruction.IteratorSlotIndex, iterStateValue);
                }

                if (forAwaitResumeKind == ResumePayloadKind.Throw)
                {
                    // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                    if (HandleAbruptCompletion(AbruptKind.Throw, forAwaitResumePayload,
                            environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(forAwaitResumePayload);
                }

                if (forAwaitResumeKind == ResumePayloadKind.Return)
                {
                    // forAwaitResumePayload is already JsValue, no need to box with .ToObject()
                    if (HandleAbruptCompletion(AbruptKind.Return, forAwaitResumePayload,
                            environment))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    returnValue = CompleteReturn(forAwaitResumePayload);
                    return InstructionResult.Return;
                }

                if (awaitingValue)
                {
                    awaitedValue = forAwaitResumePayload;
                    skipToStoreValue = true;
                }
                else
                {
                    awaitedNextResult = forAwaitResumePayload;
                    hasAwaitedNextResult = true;
                }
            }

            if (!skipToStoreValue)
            {
                if (driverState.IteratorObject is { } awaitIteratorObj)
                {
                    if (!hasAwaitedNextResult)
                    {
                        driverState.NextMethod ??= awaitIteratorObj.GetIteratorNextCallable(context);
                        var nextResult = awaitIteratorObj.InvokeIteratorNext(
                            driverState.NextMethod,
                            context: context,
                            callingEnvironment: environment);
                        if (!TryResolvePromiseOrYield(nextResult, context, out var awaitedNext))
                        {
                            if (AsyncStateRef.AsyncStepMode &&
                                AsyncStateRef.PendingPromise.TryGetPropertyAccessor(out _))
                            {
                                driverState.AwaitingNextResult = true;
                                // Use JsVariable for scope-correct access
                                var iterState = driverState.AsJsValue;
                                if (iterVar.IsValid)
                                {
                                    iterVar.Write(iterState);
                                }
                                else
                                {
                                    StoreValueBySlot(environment,
                                        instruction.IteratorSlot,
                                        instruction.IteratorSlotIndex, iterState);
                                }

                                // Save environment before suspending so we restore it on resume
                                _executionEnvironment = environment;
                                _state = GeneratorState.Suspended;
                                _programCounter = iteratorIndex;
                                returnValue = CreateIteratorResult(JsValue.Undefined, false);
                                return InstructionResult.Return;
                            }

                            if (context.IsThrow)
                            {
                                var thrownAwait = context.FlowValue;
                                context.Clear();
                                if (HandleAbruptCompletion(AbruptKind.Throw, thrownAwait, environment))
                                {
                                    returnValue = default;
                                    return InstructionResult.Continue;
                                }

                                TryCatchStateRef.TryStack.Clear();
                                throw new ThrowSignal(thrownAwait);
                            }

                            // Restore environment to enclosing scope when breaking
                            if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv4)
                            {
                                environment = enclosingEnv4;
                            }

                            IteratorStateRef.CurrentDriverState = null;
                            _programCounter = instruction.BreakIndex;
                            returnValue = default;
                            return InstructionResult.Continue;
                        }

                        awaitedNextResult = awaitedNext;
                    }

                    if (!awaitedNextResult.TryGetObject<IJsPropertyAccessor>(out var awaitResultObj))
                    {
                        // Per ES spec 7.4.2: if result is not an object, throw TypeError
                        var typeError = StandardLibrary.CreateTypeError(
                            "Iterator result is not an object", context,
                            context.RealmState);
                        if (HandleAbruptCompletion(AbruptKind.Throw, typeError, environment))
                        {
                            returnValue = default;
                            return InstructionResult.Continue;
                        }

                        TryCatchStateRef.TryStack.Clear();
                        throw new ThrowSignal(typeError);
                    }

                    var doneAwait = awaitResultObj.TryGetProperty("done", out var awaitDoneValue) &&
                                    JsOps.ToBoolean(awaitDoneValue);
                    if (doneAwait)
                    {
                        // Restore environment to enclosing scope when async iterator exhausted
                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv5)
                        {
                            environment = enclosingEnv5;
                        }

                        IteratorStateRef.CurrentDriverState = null;
                        _programCounter = instruction.BreakIndex;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    var rawValue = awaitResultObj.TryGetProperty("value", out var yieldedAwait)
                        ? yieldedAwait
                        : JsValue.Undefined;
                    if (!TryResolvePromiseOrYield(rawValue, context, out var fullyAwaitedValue))
                    {
                        if (TryHandleAwaitSuspension(driverState, iterVar,
                                instruction, ref environment, context,
                                iteratorIndex, out var suspendResult))
                        {
                            returnValue = suspendResult;
                            return InstructionResult.Return;
                        }

                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    awaitedValue = fullyAwaitedValue;
                }
                else if (driverState.Enumerator is { } awaitEnumerator)
                {
                    if (!awaitEnumerator.MoveNext())
                    {
                        // Restore environment to enclosing scope when enumerator exhausted
                        if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv7)
                        {
                            environment = enclosingEnv7;
                        }

                        // Clear the driver state since this iterator loop is done.
                        // This prevents outer loop's CreateIterationEnv from incorrectly
                        // updating this driver's CurrentIterationEnvironment.
                        IteratorStateRef.CurrentDriverState = null;
                        _programCounter = instruction.BreakIndex;
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    // enumerated is already JsValue from IEnumerator<JsValue>.Current
                    var enumerated = awaitEnumerator.Current;
                    if (!TryResolvePromiseOrYield(enumerated, context, out var awaitedEnumerated))
                    {
                        if (TryHandleAwaitSuspension(driverState, iterVar,
                                instruction, ref environment, context,
                                iteratorIndex, out var suspendResult))
                        {
                            returnValue = suspendResult;
                            return InstructionResult.Return;
                        }

                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    awaitedValue = awaitedEnumerated;
                }
                else
                {
                    // Restore environment to enclosing scope
                    if (driverState.CurrentIterationEnvironment?.Enclosing is { } enclosingEnv9)
                    {
                        environment = enclosingEnv9;
                    }

                    IteratorStateRef.CurrentDriverState = null;
                    _programCounter = instruction.BreakIndex;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // StoreIteratorValue:
            // Mark that we've successfully entered the loop (next() succeeded for async iterator).
            // Per ES spec 13.6.4.13 step 5.d, IteratorClose should only be called
            // if we've entered the loop body, not if next() itself throws.
            driverState.HasEnteredLoop = true;

            // Use JsVariable for scope-correct access (value slot is in loop scope)
            _realmState.Logger?.LogInformation(
                "StoreIteratorValue: valueVar.IsValid={Valid} slot={Slot} value={Value} envHash={Env}",
                valueVar.IsValid,
                instruction.ValueSlot.Name,
                awaitedValue.Kind,
                environment.GetHashCode());
            if (valueVar.IsValid)
            {
                valueVar.Write(awaitedValue);
                // Also create binding for symbol-based identifier lookup in loop body
                valueVar.Environment.DefineOrAssignJsValue(
                    instruction.ValueSlot, awaitedValue);
                _realmState.Logger?.LogInformation(
                    "StoreIteratorValue: wrote to valueVar.Environment={Env}",
                    valueVar.Environment.GetHashCode());
            }
            else
            {
                StoreValueBySlot(environment, instruction.ValueSlot,
                    instruction.ValueSlotIndex, awaitedValue);
                _realmState.Logger?.LogInformation(
                    "StoreIteratorValue: wrote via StoreValueBySlot to env={Env}",
                    environment.GetHashCode());
            }

            // For async iterators, clear any pending completion flags that would
            // prevent subsequent iterations after continue.
            if (_isAsync)
            {
                TryCatchStateRef.TryStack.Clear();
            }

            _programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

    }
}
