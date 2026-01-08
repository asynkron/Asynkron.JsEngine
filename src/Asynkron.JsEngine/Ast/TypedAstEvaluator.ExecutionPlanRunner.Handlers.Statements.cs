#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleStatement(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<StatementInstruction>(instr);
            var stmtResult = ProfileEvaluateStatement(instruction.Statement, environment, context);

            if (runner._isScriptMode)
            {
                if (!stmtResult.IsUnit)
                {
                    runner._scriptCompletionValue = stmtResult;
                }
                else if (ShouldResetScriptCompletion(instruction.Statement))
                {
                    runner._scriptCompletionValue = JsValue.Undefined;
                }
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

            if (context.IsBreak || context.IsContinue)
            {
                if (runner._isScriptMode)
                {
                    runner._scriptCompletionValue = JsValue.Undefined;
                }

                var isBreak = context.IsBreak;
                var label = (context.CurrentSignal as BreakCompletionSignal)?.Label
                            ?? (context.CurrentSignal as ContinueCompletionSignal)?.Label;
                context.Clear();

                var target = runner.FindBreakableTarget(label, isBreak);
                if (target >= 0)
                {
                    runner._programCounter = target;
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                throw new InvalidOperationException(
                    $"No loop target found for {(isBreak ? "break" : "continue")}{(label is not null ? $" {label.Name}" : "")}");
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleEvaluateAndDiscard(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<EvaluateAndDiscardInstruction>(instr);
            var evaluatedValue = ProfileEvaluateExpression(instruction.Expression, environment, context);

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = evaluatedValue;
            }

            var (evalSignalAction, evalSignalResult) = runner.HandleContextSignals(context, environment, instruction.Next);
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
