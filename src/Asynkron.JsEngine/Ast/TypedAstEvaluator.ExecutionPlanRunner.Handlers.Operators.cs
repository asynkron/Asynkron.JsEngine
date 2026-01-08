#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleBinaryOp(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BinaryOpInstruction>(instr);
            var binLeft = instruction.Left.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBinLeftResult, environment))
                {
                    returnValue = pendingBinLeftResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, binThrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binRight = instruction.Right.EvaluateExpression(environment, context);
            if (context.ShouldStopEvaluation)
            {
                if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingBinRightResult, environment))
                {
                    returnValue = pendingBinRightResult;
                    return InstructionResult.Return;
                }

                if (context.IsThrow)
                {
                    var binThrown = context.FlowValue;
                    context.Clear();
                    if (runner.HandleAbruptCompletion(AbruptKind.Throw, binThrown))
                    {
                        returnValue = default;
                        return InstructionResult.Continue;
                    }

                    runner.TryCatchStateRef.TryStack.Clear();
                    throw new ThrowSignal(binThrown);
                }
            }

            var binResult = ApplyBinaryOperator(instruction.Operator, binLeft, binRight, context);

            if (instruction.ResultSlot is not null)
            {
                environment.AssignJsValue(instruction.ResultSlot, binResult);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            JsValue compRhsValue;
            switch (instruction.RhsExpression)
            {
                case LiteralExpression { Value: var literalValue }:
                    compRhsValue = literalValue;
                    break;
                case IdentifierExpression { FlatSlotId: >= 0 } rhsIdent when runner._flatSlots is not null:
                    // Fast path: use flat slot for O(1) RHS read
                    compRhsValue = runner._flatSlots[rhsIdent.FlatSlotId].Read();
                    break;
                case IdentifierExpression { SlotIndex: >= 0, ScopeId: >= 0 } rhsIdent:
                    if (!environment.TryReadIdentifierWithSlot(rhsIdent, context, out compRhsValue))
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
