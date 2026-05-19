#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleEvaluateAndDiscard(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EvaluateAndDiscardInstruction>(instr);
            var evaluatedValue = runner.EvaluateExpressionProgram(instruction.ExpressionProgram, environment, context);

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = evaluatedValue;
            }

            var (evalSignalAction, evalSignalResult) = runner.HandleContextSignals(context, ref environment, instruction.Next);
            switch (evalSignalAction)
            {
                case SignalAction.Return:
                    returnValue = evalSignalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleAwaitAndDiscard(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<AwaitAndDiscardInstruction>(instr);
            var awaitedValue = runner.EvaluateAwaitInGenerator(
                instruction.AwaitStateKey,
                instruction.AwaitedProgram,
                environment,
                context);

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = awaitedValue;
            }

            var (signalAction, signalResult) = runner.HandleContextSignals(context, ref environment, instruction.Next);
            switch (signalAction)
            {
                case SignalAction.Return:
                    returnValue = signalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleAssignmentSlot(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<AssignmentSlotInstruction>(instr);
            var isAnonymousFunctionDefinition = instruction.AllowNameInference;

            // §13.15.2: When no static slot resolution is available,
            // resolve the LHS assignment reference BEFORE evaluating the RHS.
            // This ensures RHS side effects cannot create/delete a global object
            // property and change whether the LHS was originally resolvable.
            var usePreResolvedRef = instruction.FlatSlotId < 0
                && instruction.ScopeId < 0;

            var lhsRef = usePreResolvedRef
                ? environment.ResolveIdentifierAssignmentReference(instruction.TargetSymbol, context)
                : default;

            using var functionNameHint = isAnonymousFunctionDefinition
                ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                : null;

            var assignedValue = instruction.AwaitedProgram is { } awaitedProgram
                ? runner.EvaluateAwaitInGenerator(
                    instruction.AwaitStateKey!,
                    awaitedProgram,
                    environment,
                    context)
                : runner.EvaluateExpressionProgram(instruction.ValueProgram!.Value, environment, context);

            if (!context.ShouldStopEvaluation &&
                isAnonymousFunctionDefinition &&
                assignedValue is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
            {
                nameTarget.EnsureHasName(instruction.TargetSymbol.Name);
            }

            var (signalAction, signalResult) = runner.HandleContextSignals(context, ref environment, instruction.Next);
            switch (signalAction)
            {
                case SignalAction.Return:
                    returnValue = signalResult;
                    return InstructionResult.Return;
                case SignalAction.Continue:
                    returnValue = default;
                    return InstructionResult.Continue;
            }

            if (usePreResolvedRef)
            {
                lhsRef.SetValue(assignedValue);
            }
            else
            {
                var variable = FlatSlotAccessor.Create(runner, instruction.FlatSlotId);
                if (variable.UseFlatSlot)
                {
                    variable.EnsureAssignable(instruction.TargetSymbol, runner._realmState);
                    variable.Variable.Write(assignedValue);
                }
                else if (instruction.ScopeId >= 0 && instruction.SlotIndex >= 0)
                {
                    environment.TryWriteIdentifierWithSlot(
                        instruction.TargetSymbol,
                        instruction.ScopeId,
                        instruction.SlotIndex,
                        assignedValue,
                        context);
                }
                else
                {
                    environment.SetIdentifierJsValue(instruction.TargetSymbol, assignedValue, context);
                }
            }

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = assignedValue;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleSetCompletionValue(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment _,
            EvaluationContext __,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<SetCompletionValueInstruction>(instr);
            if (runner._isScriptMode)
            {
                runner._scriptCompletionValue = JsValue.Undefined;
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }
    }
}
