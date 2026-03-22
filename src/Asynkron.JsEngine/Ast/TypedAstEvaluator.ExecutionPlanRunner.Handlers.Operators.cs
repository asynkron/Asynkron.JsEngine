#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleIncrementSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<IncrementSlotInstruction>(instr);
            var flatSlotId = instruction.FlatSlotId;

            // Super-fast path: flat slot with number value (covers most loop counters)
            if (flatSlotId >= 0)
            {
                ref var targetVar = ref runner._flatSlots![flatSlotId];

                // Check for const assignment - must throw TypeError
                if (targetVar.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                        realm: runner._realmState));
                }

                var currentValue = targetVar.Read();

                if (currentValue.Kind == JsValueKind.Number)
                {
                    var numValue = currentValue.NumberValue;
                    var newValue = instruction.IsIncrement ? numValue + 1.0 : numValue - 1.0;
                    targetVar.Write(newValue);
                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // Delegate to slow path for non-number cases
            return HandleIncrementSlotSlow(runner, instruction, flatSlotId, ref environment, context, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleIncrementSlotSlow(
            ExecutionPlanRunner runner,
            IncrementSlotInstruction instruction,
            int flatSlotId,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Regular path: use ref ternary for variable access
            JsValue incCurrentValue;
            var variable = FlatSlotAccessor.Create(runner, flatSlotId);
            variable.EnsureAssignable(instruction.TargetSymbol, runner._realmState);
            var useFlatSlot = variable.UseFlatSlot;

            if (useFlatSlot)
            {
                incCurrentValue = variable.Variable.Read();
            }
            else
            {
                incCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            }

            if (context.IsThrow)
            {
                var incThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, incThrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
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
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, incFlowThrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
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

            // Fast path: use flat slot for O(1) write when available
            if (useFlatSlot)
            {
                variable.Variable.Write(incNewJsValue);
            }
            else
            {
                ProfileAssignJsValue(environment, instruction.TargetSymbol, incNewJsValue);
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = instruction.IsPrefix ? incNewJsValue : incOldNumericValue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleLogicalCompoundAssignmentSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<LogicalCompoundAssignmentSlotInstruction>(instr);
            var variable = FlatSlotAccessor.Create(runner, instruction.FlatSlotId);
            var useFlatSlot = variable.UseFlatSlot;

            JsValue currentValue;
            if (useFlatSlot)
            {
                currentValue = variable.Variable.Read();
            }
            else
            {
                currentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            }

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(thrown);
            }

            var shouldAssign = instruction.Operator switch
            {
                BinaryOperator.LogicalAnd => currentValue.IsTruthy,
                BinaryOperator.LogicalOr => !currentValue.IsTruthy,
                BinaryOperator.NullishCoalescing => currentValue.IsNullish,
                _ => throw new InvalidOperationException(
                    $"Unsupported logical compound operator '{instruction.Operator}'.")
            };

            var resultValue = currentValue;
            if (shouldAssign)
            {
                var isAnonymousFunctionDefinition =
                    instruction.AllowNameInference &&
                    instruction.RhsExpression?.IsAnonymousFunctionDefinitionNode() == true;

                using var functionNameHint = isAnonymousFunctionDefinition
                    ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                    : null;

                var rhsValue = instruction.RhsProgram is { } rhsProgram
                    ? runner.EvaluateExpressionProgram(rhsProgram, environment, context)
                    : runner.TryEvaluateSimpleExpression(instruction.RhsExpression!, environment, context, out var simpleValue)
                        ? simpleValue
                        : ProfileEvaluateExpression(instruction.RhsExpression!, environment, context);

                if (!context.ShouldStopEvaluation &&
                    isAnonymousFunctionDefinition &&
                    rhsValue is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
                {
                    nameTarget.EnsureHasName(instruction.TargetSymbol.Name);
                }

                var (signalAction, signalResult) = runner.HandleContextSignals(context, environment, instruction.Next);
                switch (signalAction)
                {
                    case SignalAction.Return:
                        returnValue = signalResult;
                        return InstructionResult.Return;
                    case SignalAction.Continue:
                        returnValue = default;
                        return InstructionResult.Continue;
                }

                if (useFlatSlot)
                {
                    variable.EnsureAssignable(instruction.TargetSymbol, runner._realmState);
                    variable.Variable.Write(rhsValue);
                }
                else if (instruction.ScopeId >= 0 && instruction.SlotIndex >= 0)
                {
                    var targetIdentifier = new IdentifierExpression(
                        instruction.RhsExpression?.Source,
                        instruction.TargetSymbol,
                        SlotIndex: instruction.SlotIndex,
                        ScopeId: instruction.ScopeId,
                        FlatSlotId: instruction.FlatSlotId);
                    environment.TryWriteIdentifierWithSlot(targetIdentifier, rhsValue, context);
                }
                else
                {
                    environment.SetIdentifierJsValue(instruction.TargetSymbol, rhsValue, context);
                }

                resultValue = rhsValue;
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = resultValue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleCompoundAssignmentSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<CompoundAssignmentSlotInstruction>(instr);
            var flatSlotId = instruction.FlatSlotId;
            var rhsFlatSlotId = instruction.RhsFlatSlotId;

            // Super-fast path: both operands use flat slots, operator is Add, both are numbers
            // This covers the common loop case like: sum = sum + prev
            if (flatSlotId >= 0 &&
                rhsFlatSlotId >= 0 &&
                instruction.Operator == BinaryOperator.Add)
            {
                ref var targetVar = ref runner._flatSlots![flatSlotId];

                // Check for const assignment - must throw TypeError
                if (targetVar.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{instruction.TargetSymbol.Name}'.",
                        realm: runner._realmState));
                }

                var leftValue = targetVar.Read();
                var rightValue = runner._flatSlots[rhsFlatSlotId].Read();

                if (leftValue.Kind == JsValueKind.Number && rightValue.Kind == JsValueKind.Number)
                {
                    var result = leftValue.NumberValue + rightValue.NumberValue;
                    targetVar.Write(result);
                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
                }
            }

            // Delegate to slow path for non-fast cases
            return HandleCompoundAssignmentSlotSlow(runner, instruction, flatSlotId, ref environment, context, out returnValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleCompoundAssignmentSlotSlow(
            ExecutionPlanRunner runner,
            CompoundAssignmentSlotInstruction instruction,
            int flatSlotId,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            // Regular path: use ref ternary for variable access
            JsValue compCurrentValue;
            var variable = FlatSlotAccessor.Create(runner, flatSlotId);
            variable.EnsureAssignable(instruction.TargetSymbol, runner._realmState);
            var useFlatSlot = variable.UseFlatSlot;

            if (useFlatSlot)
            {
                compCurrentValue = variable.Variable.Read();
            }
            else
            {
                compCurrentValue = ProfileGetIdentifier(environment, instruction.TargetSymbol, context);
            }

            if (context.IsThrow)
            {
                var compThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, compThrown))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(compThrown);
            }

            switch (instruction.Operator)
            {
                case BinaryOperator.LogicalAnd when !compCurrentValue.IsTruthy:
                    if (runner._isScriptMode && !instruction.SuppressCompletionValue)
                    {
                        runner._scriptCompletionValue = compCurrentValue;
                    }

                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;

                case BinaryOperator.LogicalOr when compCurrentValue.IsTruthy:
                    if (runner._isScriptMode && !instruction.SuppressCompletionValue)
                    {
                        runner._scriptCompletionValue = compCurrentValue;
                    }

                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;

                case BinaryOperator.NullishCoalescing when !compCurrentValue.IsNullish:
                    if (runner._isScriptMode && !instruction.SuppressCompletionValue)
                    {
                        runner._scriptCompletionValue = compCurrentValue;
                    }

                    runner._programCounter = instruction.Next;
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            JsValue compRhsValue;
            if (instruction.RhsExpressionOps is { } rhsExpressionOps)
            {
                compRhsValue = runner.EvaluateExpressionProgram(
                    new ExpressionProgram(rhsExpressionOps),
                    environment,
                    context);
            }
            else
            {
                switch (instruction.RhsExpression)
                {
                    case LiteralExpression { Value: var literalValue }:
                        compRhsValue = literalValue;
                        break;
                    case IdentifierExpression { FlatSlotId: >= 0 } rhsIdent when runner._flatSlots is not null:
                        compRhsValue = runner._flatSlots[rhsIdent.FlatSlotId].Read();
                        break;
                    case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } rhsIdent:
                        if (!environment.TryReadIdentifierWithSlot(rhsIdent, context, out compRhsValue))
                        {
                            compRhsValue = rhsIdent.EvaluateExpression(environment, context);
                        }
                        break;
                    default:
                        compRhsValue =
                            instruction.RhsExpression is not null &&
                            runner.TryEvaluateSimpleExpression(instruction.RhsExpression, environment, context, out var simpleRhsValue)
                                ? simpleRhsValue
                                : instruction.RhsExpression!.EvaluateExpression(environment, context);
                        break;
                }
            }

            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingCompResult, environment))
                {
                    returnValue = pendingCompResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var compRhsThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, compRhsThrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
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

            // Fast path: use flat slot for O(1) write when available
            if (useFlatSlot)
            {
                variable.Variable.Write(compResult);
            }
            else
            {
                ProfileAssignJsValue(environment, instruction.TargetSymbol, compResult);
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = compResult;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private ref struct FlatSlotAccessor
        {
            private FlatSlotAccessor(ref JsVariable variable) => _variable = ref variable;

            private readonly ref JsVariable _variable;

            public static FlatSlotAccessor Create(ExecutionPlanRunner runner, int flatSlotId)
            {
                ref var variable = ref (flatSlotId >= 0 && runner._flatSlots is not null
                    ? ref runner._flatSlots[flatSlotId]
                    : ref Unsafe.NullRef<JsVariable>());
                return new FlatSlotAccessor(ref variable);
            }

            public bool UseFlatSlot => !Unsafe.IsNullRef(ref _variable) && _variable.IsValid;

            public ref JsVariable Variable => ref _variable;

            public void EnsureAssignable(Symbol targetSymbol, RealmState realmState)
            {
                if (UseFlatSlot && _variable.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{targetSymbol.Name}'.",
                        realm: realmState));
                }
            }
        }
    }
}
