#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    public sealed partial class SyncFunctionInvoker
    {
        private static class SyncIrCallTrampoline
        {
            private const int InitialFrameCapacity = 64;
            private const byte TrampolineEligibilityUnknown = 0;
            private const byte TrampolineEligibilityRejected = 1;
            private const byte TrampolineEligibilityAccepted = 2;

            internal static bool TryInvoke<TArgs>(
                SyncFunctionInvoker invoker,
                TArgs arguments,
                JsValue thisValue,
                EvaluationContext context,
                JsValue newTarget,
                ExecutionPlan plan,
                out JsValue result)
                where TArgs : IReadOnlyList<JsValue>
            {
                result = JsValue.Undefined;
                if (!CanUseTrampoline(invoker, plan, newTarget))
                {
                    return false;
                }

                var originalCallDepth = context.CallDepth;
                var frames = new SyncIrFrame[InitialFrameCapacity];
                var depth = 0;
                var maxDepth = 0;

                try
                {
                    PushFrame(ref frames, ref depth, ref maxDepth, invoker, arguments, thisValue, plan);

                    var stepCount = 0;
                    while (depth > 0)
                    {
                        if ((++stepCount & 0x3FF) == 0)
                        {
                            context.ThrowIfCancellationRequested();
                        }

                        ref var frame = ref frames[depth - 1];
                        if (frame.ExpressionActive)
                        {
                            var expressionResult = StepExpression(ref frames, ref depth, ref maxDepth, context);
                            if (expressionResult == StepResult.PushedFrame)
                            {
                                continue;
                            }

                            if (expressionResult == StepResult.Bail)
                            {
                                result = JsValue.Undefined;
                                return false;
                            }

                            ref var completedFrame = ref frames[depth - 1];
                            var value = completedFrame.ExpressionStackIndex > 0
                                ? completedFrame.ExpressionStack![completedFrame.ExpressionStackIndex - 1]
                                : JsValue.Undefined;
                            completedFrame.ExpressionActive = false;
                            completedFrame.ExpressionStackIndex = 0;

                            if (completedFrame.ExpressionPurpose == ExpressionPurpose.Branch)
                            {
                                completedFrame.ProgramCounter = value.IsTruthy
                                    ? completedFrame.BranchConsequent
                                    : completedFrame.BranchAlternate;
                                completedFrame.ExpressionPurpose = ExpressionPurpose.None;
                                continue;
                            }

                            completedFrame.ExpressionPurpose = ExpressionPurpose.None;
                            if (!ReturnFromFrame(ref frames, ref depth, value, context, out result))
                            {
                                return true;
                            }

                            continue;
                        }

                        var instructions = frame.Plan.Instructions;
                        if ((uint)frame.ProgramCounter >= (uint)instructions.Length)
                        {
                            if (!ReturnFromFrame(ref frames, ref depth, JsValue.Undefined, context, out result))
                            {
                                return true;
                            }

                            continue;
                        }

                        var instruction = instructions[frame.ProgramCounter];
                        switch (instruction.Kind)
                        {
                            case InstructionKind.Branch:
                                {
                                    var branch = (BranchInstruction)instruction;
                                    StartExpression(
                                        ref frame,
                                        branch.ConditionProgram,
                                        ExpressionPurpose.Branch,
                                        branch.ConsequentIndex,
                                        branch.AlternateIndex);
                                    break;
                                }

                            case InstructionKind.Jump:
                                frame.ProgramCounter = ((JumpInstruction)instruction).TargetIndex;
                                break;

                            case InstructionKind.SetCompletionValue:
                                frame.ProgramCounter = ((SetCompletionValueInstruction)instruction).Next;
                                break;

                            case InstructionKind.Return:
                                {
                                    var returnInstruction = (ReturnInstruction)instruction;
                                    if (returnInstruction.ReturnProgram is { } returnProgram)
                                    {
                                        StartExpression(
                                            ref frame,
                                            returnProgram,
                                            ExpressionPurpose.Return,
                                            -1,
                                            -1);
                                    }
                                    else if (!ReturnFromFrame(ref frames, ref depth, JsValue.Undefined, context, out result))
                                    {
                                        return true;
                                    }

                                    break;
                                }

                            default:
                                result = JsValue.Undefined;
                                return false;
                        }
                    }

                    return true;
                }
                finally
                {
                    context.CallDepth = originalCallDepth;
                    ClearFrameStorage(frames, maxDepth);
                }
            }

            private static bool CanUseTrampoline(SyncFunctionInvoker invoker, ExecutionPlan plan, JsValue newTarget)
            {
                if (!newTarget.IsUndefined ||
                    invoker.IsClassConstructor ||
                    invoker.IsAsyncLike ||
                    invoker._function.IsGenerator ||
                    invoker._function.IsDefaultDerivedConstructor ||
                    invoker._hasParameterExpressions ||
                    !HasOnlySimpleIdentifierParameters(invoker._function) ||
                    invoker._usesArguments ||
                    invoker._needsArgumentsBinding ||
                    !invoker._allowIdentifierCache ||
                    invoker._homeObject is not null ||
                    invoker.PrivateNameScope is not null ||
                    !invoker._capturedPrivateNameScopes.IsDefaultOrEmpty ||
                    invoker._superConstructor is not null ||
                    invoker._superPrototype is not null ||
                    !invoker._instanceFields.IsDefaultOrEmpty ||
                    invoker._function.Name is { } functionName && HasParameterNamed(invoker, functionName) ||
                    !invoker.CanUseSimpleIrActivationPlanShape(plan) ||
                    !plan.CanUseRawSyncReturn ||
                    plan.ActivationSlots is not { } activationSlots)
                {
                    return false;
                }

                if (ReferenceEquals(invoker._syncIrTrampolineEligibilityPlan, plan) &&
                    invoker._syncIrTrampolineEligibility != TrampolineEligibilityUnknown)
                {
                    return invoker._syncIrTrampolineEligibility == TrampolineEligibilityAccepted;
                }

                var canRun = CanRunPlan(invoker, plan, activationSlots);
                invoker._syncIrTrampolineEligibilityPlan = plan;
                invoker._syncIrTrampolineEligibility = canRun
                    ? TrampolineEligibilityAccepted
                    : TrampolineEligibilityRejected;
                return canRun;
            }

            private static bool HasParameterNamed(SyncFunctionInvoker invoker, Symbol name)
            {
                var parameters = invoker._parameterNames;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (string.Equals(parameters[i].Name, name.Name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool CanRunPlan(
                SyncFunctionInvoker invoker,
                ExecutionPlan plan,
                ActivationSlotShape activationSlots)
            {
                foreach (var instruction in plan.Instructions)
                {
                    switch (instruction)
                    {
                        case BranchInstruction branch:
                            if (!CanRunExpression(invoker, branch.ConditionProgram, activationSlots))
                            {
                                return false;
                            }

                            break;

                        case ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null }:
                            if (!CanRunExpression(invoker, returnProgram, activationSlots))
                            {
                                return false;
                            }

                            break;

                        case ReturnInstruction { ReturnProgram: null, AwaitedProgram: null }:
                        case JumpInstruction:
                        case SetCompletionValueInstruction:
                            break;

                        default:
                            return false;
                    }
                }

                return true;
            }

            private static bool CanRunExpression(
                SyncFunctionInvoker invoker,
                ExpressionProgram program,
                ActivationSlotShape activationSlots)
            {
                if (program.IsEmpty)
                {
                    return true;
                }

                Span<ExpressionStackTag> tags = stackalloc ExpressionStackTag[Math.Max(program.MaxStackDepth, 1)];
                var tagIndex = 0;
                var identifierConstants = program.IdentifierConstants.AsSpan();
                for (var pc = 0; pc < program.OperationCount; pc++)
                {
                    var operation = program.GetOperation(pc);
                    switch (operation.Kind)
                    {
                        case ExpressionOpKind.LoadLiteral:
                            tags[tagIndex++] = ExpressionStackTag.Value;
                            break;

                        case ExpressionOpKind.LoadIdentifier:
                            if (!CanReadIdentifier(
                                    invoker,
                                    operation.GetIdentifier(identifierConstants),
                                    activationSlots))
                            {
                                return false;
                            }

                            tags[tagIndex++] = ExpressionStackTag.Value;
                            break;

                        case ExpressionOpKind.LoadIdentifierCallTarget:
                            if (!CanReadSelfIdentifier(
                                    invoker,
                                    operation.GetIdentifier(identifierConstants),
                                    activationSlots))
                            {
                                return false;
                            }

                            tags[tagIndex++] = ExpressionStackTag.SelfReceiver;
                            tags[tagIndex++] = ExpressionStackTag.SelfCallee;
                            break;

                        case ExpressionOpKind.Binary:
                            if (tagIndex < 2)
                            {
                                return false;
                            }

                            tagIndex--;
                            tags[tagIndex - 1] = ExpressionStackTag.Value;
                            break;

                        case ExpressionOpKind.Call:
                            if (operation.SpreadMaskConstantIndex >= 0 ||
                                !operation.HasExplicitThis ||
                                tagIndex < operation.ArgumentCount + 2)
                            {
                                return false;
                            }

                            var calleeIndex = tagIndex - operation.ArgumentCount - 1;
                            var receiverIndex = calleeIndex - 1;
                            if (tags[receiverIndex] != ExpressionStackTag.SelfReceiver ||
                                tags[calleeIndex] != ExpressionStackTag.SelfCallee)
                            {
                                return false;
                            }

                            tagIndex = receiverIndex + 1;
                            tags[receiverIndex] = ExpressionStackTag.Value;
                            break;

                        default:
                            return false;
                    }
                }

                return tagIndex == 1;
            }

            private static bool CanReadIdentifier(
                SyncFunctionInvoker invoker,
                IdentifierOperand identifier,
                ActivationSlotShape activationSlots) =>
                IsParameterSlot(identifier, activationSlots) ||
                CanReadSelfIdentifier(invoker, identifier, activationSlots);

            private static bool CanReadSelfIdentifier(
                SyncFunctionInvoker invoker,
                IdentifierOperand identifier,
                ActivationSlotShape activationSlots) =>
                invoker._function.Name is { } functionName &&
                !IsParameterSlot(identifier, activationSlots) &&
                string.Equals(identifier.Name.Name, functionName.Name, StringComparison.Ordinal);

            private static bool IsParameterSlot(
                IdentifierOperand identifier,
                ActivationSlotShape activationSlots)
            {
                if (identifier.ScopeId != activationSlots.ScopeId || identifier.SlotIndex < 0)
                {
                    return false;
                }

                var parameterSlotIndices = activationSlots.ParameterSlotIndices;
                for (var i = 0; i < parameterSlotIndices.Length; i++)
                {
                    if (parameterSlotIndices[i] == identifier.SlotIndex)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static void PushFrame<TArgs>(
                ref SyncIrFrame[] frames,
                ref int depth,
                ref int maxDepth,
                SyncFunctionInvoker invoker,
                TArgs arguments,
                JsValue thisValue,
                ExecutionPlan plan)
                where TArgs : IReadOnlyList<JsValue>
            {
                EnsureFrameCapacity(ref frames, depth + 1);
                ref var frame = ref frames[depth];
                InitializeFrame(ref frame, invoker, thisValue, plan);

                var activationSlots = plan.ActivationSlots!;
                for (var i = 0; i < activationSlots.ParameterSlotIndices.Length; i++)
                {
                    var slotIndex = activationSlots.ParameterSlotIndices[i];
                    frame.Slots![slotIndex] = i < arguments.Count
                        ? arguments[i]
                        : JsValue.Undefined;
                }

                depth++;
                maxDepth = Math.Max(maxDepth, depth);
            }

            private static void PushFrameFromExpression(
                ref SyncIrFrame[] frames,
                ref int depth,
                ref int maxDepth,
                SyncFunctionInvoker invoker,
                ReadOnlySpan<JsValue> arguments,
                JsValue thisValue,
                ExecutionPlan plan)
            {
                EnsureFrameCapacity(ref frames, depth + 1);
                ref var frame = ref frames[depth];
                InitializeFrame(ref frame, invoker, thisValue, plan);

                var activationSlots = plan.ActivationSlots!;
                for (var i = 0; i < activationSlots.ParameterSlotIndices.Length; i++)
                {
                    var slotIndex = activationSlots.ParameterSlotIndices[i];
                    frame.Slots![slotIndex] = i < arguments.Length
                        ? arguments[i]
                        : JsValue.Undefined;
                }

                depth++;
                maxDepth = Math.Max(maxDepth, depth);
            }

            private static void InitializeFrame(
                ref SyncIrFrame frame,
                SyncFunctionInvoker invoker,
                JsValue thisValue,
                ExecutionPlan plan)
            {
                var activationSlots = plan.ActivationSlots!;
                frame.Invoker = invoker;
                frame.Plan = plan;
                frame.ActivationSlots = activationSlots;
                frame.ThisValue = thisValue;
                frame.ProgramCounter = plan.EntryPoint;
                frame.ExpressionActive = false;
                frame.ExpressionPurpose = ExpressionPurpose.None;
                frame.ExpressionStackIndex = 0;
                frame.ExpressionProgram = ExpressionProgram.Empty;
                frame.ExpressionProgramCounter = 0;
                frame.BranchConsequent = -1;
                frame.BranchAlternate = -1;
                EnsureSlotCapacity(ref frame, activationSlots.SlotCount);
                frame.Slots.AsSpan(0, activationSlots.SlotCount).Clear();
            }

            private static StepResult StepExpression(
                ref SyncIrFrame[] frames,
                ref int depth,
                ref int maxDepth,
                EvaluationContext context)
            {
                ref var frame = ref frames[depth - 1];
                var program = frame.ExpressionProgram;
                var literalConstants = program.LiteralConstants.AsSpan();
                var identifierConstants = program.IdentifierConstants.AsSpan();
                var operationCount = program.OperationCount;
                var stack = frame.ExpressionStack!;

                while ((uint)frame.ExpressionProgramCounter < (uint)operationCount)
                {
                    var operation = program.GetOperation(frame.ExpressionProgramCounter);
                    switch (operation.Kind)
                    {
                        case ExpressionOpKind.LoadLiteral:
                            stack[frame.ExpressionStackIndex++] = operation.GetLiteral(literalConstants);
                            frame.ExpressionProgramCounter++;
                            break;

                        case ExpressionOpKind.LoadIdentifier:
                            if (!TryReadIdentifier(
                                    frame,
                                    operation.GetIdentifier(identifierConstants),
                                    out stack[frame.ExpressionStackIndex]))
                            {
                                return StepResult.Bail;
                            }

                            frame.ExpressionStackIndex++;
                            frame.ExpressionProgramCounter++;
                            break;

                        case ExpressionOpKind.LoadIdentifierCallTarget:
                            {
                                var identifier = operation.GetIdentifier(identifierConstants);
                                if (!CanReadSelfIdentifier(frame.Invoker!, identifier, frame.ActivationSlots!))
                                {
                                    return StepResult.Bail;
                                }

                                stack[frame.ExpressionStackIndex++] = JsValue.Undefined;
                                stack[frame.ExpressionStackIndex++] = frame.Invoker!._cachedJsValue;
                                frame.ExpressionProgramCounter++;
                                break;
                            }

                        case ExpressionOpKind.Binary:
                            {
                                var right = stack[--frame.ExpressionStackIndex];
                                var left = stack[frame.ExpressionStackIndex - 1];
                                stack[frame.ExpressionStackIndex - 1] = ApplyTrampolineBinary(
                                    operation.Operator,
                                    left,
                                    right,
                                    context);
                                frame.ExpressionProgramCounter++;
                                if (context.ShouldStopEvaluation)
                                {
                                    return StepResult.Completed;
                                }

                                break;
                            }

                        case ExpressionOpKind.Call:
                            {
                                if (operation.SpreadMaskConstantIndex >= 0 ||
                                    !operation.HasExplicitThis)
                                {
                                    return StepResult.Bail;
                                }

                                var argumentCount = operation.ArgumentCount;
                                var calleeIndex = frame.ExpressionStackIndex - argumentCount - 1;
                                var receiverIndex = calleeIndex - 1;
                                if (receiverIndex < 0 ||
                                    stack[calleeIndex].ObjectValue is not SyncFunctionInvoker callee ||
                                    !ReferenceEquals(callee, frame.Invoker))
                                {
                                    return StepResult.Bail;
                                }

                                if (++context.CallDepth > context.MaxCallDepth)
                                {
                                    context.CallDepth--;
                                    throw new InvalidOperationException(
                                        $"Exceeded maximum call depth of {context.MaxCallDepth}.");
                                }

                                frame.ExpressionProgramCounter++;
                                frame.ExpressionStackIndex = receiverIndex;
                                PushFrameFromExpression(
                                    ref frames,
                                    ref depth,
                                    ref maxDepth,
                                    callee,
                                    stack.AsSpan(calleeIndex + 1, argumentCount),
                                    stack[receiverIndex],
                                    frame.Plan);
                                return StepResult.PushedFrame;
                            }

                        default:
                            return StepResult.Bail;
                    }
                }

                return StepResult.Completed;
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            private static JsValue ApplyTrampolineBinary(
                BinaryOperator op,
                JsValue left,
                JsValue right,
                EvaluationContext context)
            {
                if (left.IsNumber && right.IsNumber)
                {
                    var leftNumber = left.NumberValue;
                    var rightNumber = right.NumberValue;
                    return op switch
                    {
                        BinaryOperator.Add => JsValue.FromDouble(leftNumber + rightNumber),
                        BinaryOperator.Subtract => JsValue.FromDouble(leftNumber - rightNumber),
                        BinaryOperator.Multiply => JsValue.FromDouble(leftNumber * rightNumber),
                        BinaryOperator.Divide => JsValue.FromDouble(leftNumber / rightNumber),
                        BinaryOperator.LessThan => leftNumber < rightNumber ? JsValue.True : JsValue.False,
                        BinaryOperator.LessThanOrEqual => leftNumber <= rightNumber ? JsValue.True : JsValue.False,
                        BinaryOperator.GreaterThan => leftNumber > rightNumber ? JsValue.True : JsValue.False,
                        BinaryOperator.GreaterThanOrEqual => leftNumber >= rightNumber ? JsValue.True : JsValue.False,
                        _ => ApplyBinaryOperator(op, left, right, context)
                    };
                }

                return ApplyBinaryOperator(op, left, right, context);
            }

            private static bool TryReadIdentifier(
                SyncIrFrame frame,
                IdentifierOperand identifier,
                out JsValue value)
            {
                if (identifier.ScopeId == frame.ActivationSlots!.ScopeId &&
                    identifier.SlotIndex >= 0 &&
                    IsParameterSlot(identifier, frame.ActivationSlots))
                {
                    value = frame.Slots![identifier.SlotIndex];
                    return true;
                }

                if (CanReadSelfIdentifier(frame.Invoker!, identifier, frame.ActivationSlots!))
                {
                    value = frame.Invoker!._cachedJsValue;
                    return true;
                }

                value = JsValue.Undefined;
                return false;
            }

            private static bool ReturnFromFrame(
                ref SyncIrFrame[] frames,
                ref int depth,
                JsValue value,
                EvaluationContext context,
                out JsValue result)
            {
                result = value;
                depth--;

                if (depth == 0)
                {
                    return false;
                }

                context.CallDepth--;
                ref var caller = ref frames[depth - 1];
                caller.ExpressionStack![caller.ExpressionStackIndex++] = value;
                return true;
            }

            private static void StartExpression(
                ref SyncIrFrame frame,
                ExpressionProgram program,
                ExpressionPurpose purpose,
                int branchConsequent,
                int branchAlternate)
            {
                frame.ExpressionProgram = program;
                frame.ExpressionProgramCounter = 0;
                frame.ExpressionStackIndex = 0;
                frame.ExpressionPurpose = purpose;
                frame.BranchConsequent = branchConsequent;
                frame.BranchAlternate = branchAlternate;
                frame.ExpressionActive = true;
                EnsureExpressionStackCapacity(ref frame, Math.Max(program.MaxStackDepth, 1));
            }

            private static void EnsureFrameCapacity(ref SyncIrFrame[] frames, int required)
            {
                if (required <= frames.Length)
                {
                    return;
                }

                var newLength = checked(frames.Length * 2);
                while (newLength < required)
                {
                    newLength = checked(newLength * 2);
                }

                Array.Resize(ref frames, newLength);
            }

            private static void EnsureSlotCapacity(ref SyncIrFrame frame, int required)
            {
                if (frame.Slots is { } slots && slots.Length >= required)
                {
                    return;
                }

                frame.Slots = new JsValue[Math.Max(required, 1)];
            }

            private static void EnsureExpressionStackCapacity(ref SyncIrFrame frame, int required)
            {
                if (frame.ExpressionStack is { } stack && stack.Length >= required)
                {
                    return;
                }

                frame.ExpressionStack = new JsValue[Math.Max(required, 1)];
            }

            private static void ClearFrameStorage(SyncIrFrame[] frames, int maxDepth)
            {
                for (var i = 0; i < maxDepth; i++)
                {
                    if (frames[i].Slots is { } slots)
                    {
                        Array.Clear(slots);
                    }

                    if (frames[i].ExpressionStack is { } stack)
                    {
                        Array.Clear(stack);
                    }

                    frames[i] = default;
                }
            }

            private enum ExpressionPurpose : byte
            {
                None,
                Branch,
                Return
            }

            private enum ExpressionStackTag : byte
            {
                Value,
                SelfReceiver,
                SelfCallee
            }

            private enum StepResult : byte
            {
                Completed,
                PushedFrame,
                Bail
            }

            private struct SyncIrFrame
            {
                public SyncFunctionInvoker? Invoker;
                public ExecutionPlan Plan;
                public ActivationSlotShape? ActivationSlots;
                public JsValue ThisValue;
                public JsValue[]? Slots;
                public int ProgramCounter;
                public bool ExpressionActive;
                public ExpressionPurpose ExpressionPurpose;
                public ExpressionProgram ExpressionProgram;
                public int ExpressionProgramCounter;
                public JsValue[]? ExpressionStack;
                public int ExpressionStackIndex;
                public int BranchConsequent;
                public int BranchAlternate;
            }
        }
    }
}
