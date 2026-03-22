#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

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
            var evaluatedValue = instruction.ExpressionProgram is { } expressionProgram
                ? runner.EvaluateExpressionProgram(expressionProgram, environment, context)
                : ProfileEvaluateExpression(instruction.Expression!, environment, context);

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
                instruction.AwaitedExpression,
                instruction.AwaitedProgram,
                environment,
                context);

            if (runner._isScriptMode && !instruction.SuppressCompletionValue)
            {
                runner._scriptCompletionValue = awaitedValue;
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
            var isAnonymousFunctionDefinition =
                instruction.AllowNameInference &&
                instruction.ValueExpression?.IsAnonymousFunctionDefinitionNode() == true;

            using var functionNameHint = isAnonymousFunctionDefinition
                ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                : null;

            var assignedValue = instruction.ValueProgram is { } valueProgram
                ? runner.EvaluateExpressionProgram(valueProgram, environment, context)
                : runner.TryEvaluateSimpleExpression(instruction.ValueExpression!, environment, context, out var simpleValue)
                    ? simpleValue
                    : ProfileEvaluateExpression(instruction.ValueExpression!, environment, context);

            if (!context.ShouldStopEvaluation &&
                isAnonymousFunctionDefinition &&
                assignedValue is { Kind: JsValueKind.Object, ObjectValue: IFunctionNameTarget nameTarget })
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

            var variable = FlatSlotAccessor.Create(runner, instruction.FlatSlotId);
            if (variable.UseFlatSlot)
            {
                variable.EnsureAssignable(instruction.TargetSymbol, runner._realmState);
                variable.Variable.Write(assignedValue);
            }
                else if (instruction.ScopeId >= 0 && instruction.SlotIndex >= 0)
                {
                var targetIdentifier = new IdentifierExpression(
                        instruction.ValueExpression?.Source,
                        instruction.TargetSymbol,
                        SlotIndex: instruction.SlotIndex,
                        ScopeId: instruction.ScopeId,
                    FlatSlotId: instruction.FlatSlotId);
                environment.TryWriteIdentifierWithSlot(targetIdentifier, assignedValue, context);
            }
            else
            {
                environment.SetIdentifierJsValue(instruction.TargetSymbol, assignedValue, context);
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
