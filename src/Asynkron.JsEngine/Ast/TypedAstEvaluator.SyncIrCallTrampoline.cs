using System.Runtime.CompilerServices;
using System.Buffers;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    public sealed partial class SyncFunctionInvoker
    {
        private static class SyncIrCallTrampoline
        {
            private const int InitialFrameCapacity = 4;
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
                if (context.DisableSyncIrCallTrampoline ||
                    !CanUseTrampoline(invoker, plan, newTarget))
                {
                    return false;
                }

                var originalCallDepth = context.CallDepth;
                if (plan.SimpleReturnLiteral is { } simpleReturnLiteral)
                {
                    result = simpleReturnLiteral.Value;
                    return true;
                }

                var frames = RentFrameStorage();
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
                            var expressionResult = StepExpression(ref frames, ref depth, context);
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
                            if (!ReturnFromFrame(ref frames, ref depth, value, out result))
                            {
                                return true;
                            }

                            continue;
                        }

                        var instructions = frame.Plan.Instructions;
                        if ((uint)frame.ProgramCounter >= (uint)instructions.Length)
                        {
                            if (!ReturnFromFrame(ref frames, ref depth, JsValue.Undefined, out result))
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

                            case InstructionKind.PushEnvironment:
                                {
                                    var push = (PushEnvironmentInstruction)instruction;
                                    if (push.SlotCount != 0 ||
                                        !push.PerIterationBindings.IsDefaultOrEmpty ||
                                        push.LexicalBindings is { Count: > 0 })
                                    {
                                        result = InvokeCurrentFrameNormally(frames[depth - 1], context);
                                        return true;
                                    }

                                    frame.ProgramCounter = push.Next;
                                    break;
                                }

                            case InstructionKind.PopEnvironment:
                                frame.ProgramCounter = ((PopEnvironmentInstruction)instruction).Next;
                                break;

                            case InstructionKind.FunctionDeclaration:
                                frame.ProgramCounter = ((FunctionDeclarationInstruction)instruction).Next;
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
                                    else if (!ReturnFromFrame(ref frames, ref depth, JsValue.Undefined, out result))
                                    {
                                        return true;
                                    }

                                    break;
                                }

                            default:
                                result = InvokeCurrentFrameNormally(frames[depth - 1], context);
                                return true;
                        }
                    }

                    return true;
                }
                finally
                {
                    context.CallDepth = originalCallDepth;
                    ClearFrameStorage(frames, maxDepth);
                    ReturnFrameStorage(frames);
                }
            }

            internal static bool CanUseDirectReturnFastPath(
                SyncFunctionInvoker invoker,
                ExecutionPlan plan,
                JsValue newTarget) =>
                CanUseTrampoline(invoker, plan, newTarget);

            private static SyncIrFrame[] RentFrameStorage()
            {
                return ArrayPool<SyncIrFrame>.Shared.Rent(InitialFrameCapacity);
            }

            private static void ReturnFrameStorage(SyncIrFrame[] frames)
            {
                ArrayPool<SyncIrFrame>.Shared.Return(frames, clearArray: false);
            }

            private static bool CanUseTrampoline(SyncFunctionInvoker invoker, ExecutionPlan plan, JsValue newTarget)
            {
                if (!newTarget.IsUndefined ||
                    invoker.IsClassConstructor ||
                    invoker.IsAsyncLike ||
                    invoker._function.IsGenerator ||
                    invoker._function.IsDefaultDerivedConstructor ||
                    invoker._hasParameterExpressions ||
                    invoker._hasNonParameterCalleeCall ||
                    !invoker._hasOnlySimpleIdentifierParameters ||
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
                            if (!CanRunExpression(
                                    invoker,
                                    branch.ConditionProgram,
                                    activationSlots,
                                    ExpressionPurpose.Branch))
                            {
                                return false;
                            }

                            break;

                        case ReturnInstruction { ReturnProgram: { } returnProgram, AwaitedProgram: null }:
                            if (!CanRunExpression(
                                    invoker,
                                    returnProgram,
                                    activationSlots,
                                    ExpressionPurpose.Return))
                            {
                                return false;
                            }

                            break;

                        case ReturnInstruction { ReturnProgram: null, AwaitedProgram: null }:
                        case FunctionDeclarationInstruction { Descriptor: null }:
                        case EvaluateAndDiscardInstruction:
                        case JumpInstruction:
                        case SetCompletionValueInstruction:
                            break;

                        case FunctionDeclarationInstruction { Descriptor: { } descriptor }:
                            if (!IsSelfReturnHelperFunction(invoker, descriptor.Function))
                            {
                                return false;
                            }

                            break;

                        case ReturnInstruction { AwaitedProgram: not null }:
                        case EnterTryInstruction:
                        case EnterCatchInstruction:
                        case LeaveTryInstruction:
                        case EndFinallyInstruction:
                            return false;
                    }
                }

                return true;
            }

            private static bool CanRunExpression(
                SyncFunctionInvoker invoker,
                ExpressionProgram program,
                ActivationSlotShape activationSlots,
                ExpressionPurpose purpose)
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

                        case ExpressionOpKind.LoadTemplateObject:
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

                            tags[tagIndex++] = CanReadSelfIdentifier(
                                invoker,
                                operation.GetIdentifier(identifierConstants),
                                activationSlots)
                                ? ExpressionStackTag.SelfCallee
                                : ExpressionStackTag.Value;
                            break;

                        case ExpressionOpKind.LoadIdentifierCallTarget:
                            var callTargetIdentifier = operation.GetIdentifier(identifierConstants);
                            if (CanReadSelfIdentifier(invoker, callTargetIdentifier, activationSlots))
                            {
                                tags[tagIndex++] = ExpressionStackTag.SelfReceiver;
                                tags[tagIndex++] = ExpressionStackTag.SelfCallee;
                                break;
                            }

                            if (!CanReadSelfReturnHelperIdentifier(
                                    invoker,
                                    callTargetIdentifier,
                                    activationSlots))
                            {
                                return false;
                            }

                            tags[tagIndex++] = ExpressionStackTag.Value;
                            tags[tagIndex++] = ExpressionStackTag.SelfReturnHelperCallee;
                            break;

                        case ExpressionOpKind.Binary:
                            if (tagIndex < 2)
                            {
                                return false;
                            }

                            tagIndex--;
                            tags[tagIndex - 1] = ExpressionStackTag.Value;
                            break;

                        case ExpressionOpKind.Pop:
                            if (tagIndex < 1)
                            {
                                return false;
                            }

                            tagIndex--;
                            break;

                        case ExpressionOpKind.Jump:
                        case ExpressionOpKind.JumpIfNullish:
                        case ExpressionOpKind.JumpIfShortCircuited:
                        case ExpressionOpKind.JumpIfTrue:
                        case ExpressionOpKind.JumpIfFalse:
                        case ExpressionOpKind.JumpIfNotNullish:
                            if (tagIndex < 1)
                            {
                                return false;
                            }

                            break;

                        case ExpressionOpKind.JumpIfConditionalFalse:
                            if (tagIndex < 1)
                            {
                                return false;
                            }

                            tagIndex--;
                            break;

                        case ExpressionOpKind.Call:
                            if (operation.SpreadMaskConstantIndex >= 0 ||
                                purpose != ExpressionPurpose.Return)
                            {
                                return false;
                            }

                            var calleeIndex = tagIndex - operation.ArgumentCount - 1;
                            if (pc != program.OperationCount - 1)
                            {
                                if (!operation.HasExplicitThis ||
                                    operation.ArgumentCount != 0 ||
                                    tagIndex < 2 ||
                                    tags[calleeIndex] != ExpressionStackTag.SelfReturnHelperCallee)
                                {
                                    return false;
                                }

                                var helperReceiverIndex = calleeIndex - 1;
                                tagIndex = helperReceiverIndex + 1;
                                tags[helperReceiverIndex] = ExpressionStackTag.SelfCallee;
                                break;
                            }

                            if (operation.HasExplicitThis)
                            {
                                if (tagIndex < operation.ArgumentCount + 2)
                                {
                                    return false;
                                }

                                var receiverIndex = calleeIndex - 1;
                                if (tags[receiverIndex] != ExpressionStackTag.SelfReceiver ||
                                    tags[calleeIndex] != ExpressionStackTag.SelfCallee)
                                {
                                    return false;
                                }

                                tagIndex = receiverIndex + 1;
                                tags[receiverIndex] = ExpressionStackTag.Value;
                                break;
                            }

                            if (!invoker._isStrict ||
                                tagIndex < operation.ArgumentCount + 1 ||
                                tags[calleeIndex] != ExpressionStackTag.SelfCallee)
                            {
                                return false;
                            }

                            tagIndex = calleeIndex + 1;
                            tags[calleeIndex] = ExpressionStackTag.Value;
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

            private static bool CanReadSelfReturnHelperIdentifier(
                SyncFunctionInvoker invoker,
                IdentifierOperand identifier,
                ActivationSlotShape activationSlots) =>
                !IsParameterSlot(identifier, activationSlots) &&
                (HasLocalSelfReturnHelperDeclaration(invoker, identifier.Name) ||
                 TryGetClosureSelfReturnHelper(invoker, identifier.Name, out _));

            private static bool HasLocalSelfReturnHelperDeclaration(
                SyncFunctionInvoker invoker,
                Symbol name)
            {
                foreach (var statement in invoker._function.Body.Statements)
                {
                    if (statement is FunctionDeclaration declaration &&
                        string.Equals(declaration.Name.Name, name.Name, StringComparison.Ordinal) &&
                        IsSelfReturnHelperFunction(invoker, declaration.Function))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryGetClosureSelfReturnHelper(
                SyncFunctionInvoker invoker,
                Symbol name,
                out SyncFunctionInvoker helper)
            {
                for (var current = invoker._closure; current is not null; current = current.Enclosing)
                {
                    if (!current.TryGetSlotIndex(name, out var slotIndex))
                    {
                        continue;
                    }

                    var value = current.GetSlotByIndex(slotIndex).Value;
                    if (value.TryGetObject<SyncFunctionInvoker>(out var candidate) &&
                        IsSelfReturnHelperFunction(invoker, candidate._function))
                    {
                        helper = candidate;
                        return true;
                    }

                    break;
                }

                helper = null!;
                return false;
            }

            private static bool IsSelfReturnHelperFunction(
                SyncFunctionInvoker invoker,
                FunctionExpression helper)
            {
                if (helper.IsAsync ||
                    helper.IsGenerator ||
                    helper.Parameters.Length != 0 ||
                    invoker._function.Name is not { } functionName ||
                    helper.Body.Statements.Length != 1 ||
                    helper.Body.Statements[0] is not ReturnStatement { Expression: IdentifierExpression identifier })
                {
                    return false;
                }

                return string.Equals(identifier.Name.Name, functionName.Name, StringComparison.Ordinal);
            }

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
                EnsureFrameCapacity(ref frames, depth + 1, depth);
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
                ClearExpressionStackFlags(ref frame);
            }

            private static StepResult StepExpression(
                ref SyncIrFrame[] frames,
                ref int depth,
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
                            SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                            frame.ExpressionProgramCounter++;
                            break;

                        case ExpressionOpKind.LoadTemplateObject:
                            {
                                var descriptor = operation.GetObject<TaggedTemplateDescriptor>(
                                    program.ObjectConstants.AsSpan());
                                stack[frame.ExpressionStackIndex++] = JsValue.FromJsArray(
                                    GetOrCreateProgramTemplateObject(descriptor, context));
                                SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                                frame.ExpressionProgramCounter++;
                                break;
                            }

                        case ExpressionOpKind.LoadIdentifier:
                            if (!TryReadIdentifier(
                                    frame,
                                    operation.GetIdentifier(identifierConstants),
                                    out stack[frame.ExpressionStackIndex]))
                            {
                                return StepResult.Bail;
                            }

                            frame.ExpressionStackIndex++;
                            SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                            frame.ExpressionProgramCounter++;
                            break;

                        case ExpressionOpKind.LoadIdentifierCallTarget:
                            {
                                var identifier = operation.GetIdentifier(identifierConstants);
                                if (CanReadSelfIdentifier(frame.Invoker!, identifier, frame.ActivationSlots!))
                                {
                                    stack[frame.ExpressionStackIndex++] = JsValue.Undefined;
                                    SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                                    stack[frame.ExpressionStackIndex++] = frame.Invoker!._cachedJsValue;
                                    SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                                    frame.ExpressionProgramCounter++;
                                    break;
                                }

                                if (!TryResolveSelfReturnHelperIdentifier(frame, identifier))
                                {
                                    return StepResult.Bail;
                                }

                                stack[frame.ExpressionStackIndex++] = JsValue.Undefined;
                                SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                                stack[frame.ExpressionStackIndex++] = frame.Invoker!._cachedJsValue;
                                SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
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
                                SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, false);
                                frame.ExpressionProgramCounter++;
                                if (context.ShouldStopEvaluation)
                                {
                                    return StepResult.Completed;
                                }

                                break;
                            }

                        case ExpressionOpKind.Pop:
                            frame.ExpressionStackIndex--;
                            frame.ExpressionProgramCounter++;
                            break;

                        case ExpressionOpKind.Jump:
                            frame.ExpressionProgramCounter = operation.Target;
                            break;

                        case ExpressionOpKind.JumpIfNullish:
                            if (GetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1) ||
                                stack[frame.ExpressionStackIndex - 1].IsNullish)
                            {
                                if (operation.ReplaceWithUndefined)
                                {
                                    stack[frame.ExpressionStackIndex - 1] = JsValue.Undefined;
                                    SetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1, true);
                                }

                                frame.ExpressionProgramCounter = operation.Target;
                            }
                            else
                            {
                                frame.ExpressionProgramCounter++;
                            }

                            break;

                        case ExpressionOpKind.JumpIfTrue:
                            frame.ExpressionProgramCounter =
                                stack[frame.ExpressionStackIndex - 1].IsTruthy
                                    ? operation.Target
                                    : frame.ExpressionProgramCounter + 1;
                            break;

                        case ExpressionOpKind.JumpIfFalse:
                            frame.ExpressionProgramCounter =
                                !stack[frame.ExpressionStackIndex - 1].IsTruthy
                                    ? operation.Target
                                    : frame.ExpressionProgramCounter + 1;
                            break;

                        case ExpressionOpKind.JumpIfConditionalFalse:
                            frame.ExpressionStackIndex--;
                            frame.ExpressionProgramCounter =
                                !stack[frame.ExpressionStackIndex].IsTruthy
                                    ? operation.Target
                                    : frame.ExpressionProgramCounter + 1;
                            break;

                        case ExpressionOpKind.JumpIfNotNullish:
                            frame.ExpressionProgramCounter =
                                !stack[frame.ExpressionStackIndex - 1].IsNullish
                                    ? operation.Target
                                    : frame.ExpressionProgramCounter + 1;
                            break;

                        case ExpressionOpKind.JumpIfShortCircuited:
                            frame.ExpressionProgramCounter =
                                GetExpressionStackFlag(frame, frame.ExpressionStackIndex - 1)
                                    ? operation.Target
                                    : frame.ExpressionProgramCounter + 1;
                            break;

                        case ExpressionOpKind.Call:
                            {
                                if (operation.SpreadMaskConstantIndex >= 0 ||
                                    frame.ExpressionPurpose != ExpressionPurpose.Return)
                                {
                                    return StepResult.Bail;
                                }

                                var argumentCount = operation.ArgumentCount;
                                var calleeIndex = frame.ExpressionStackIndex - argumentCount - 1;
                                if (frame.ExpressionProgramCounter != operationCount - 1)
                                {
                                    if (!operation.HasExplicitThis ||
                                        argumentCount != 0 ||
                                        calleeIndex <= 0 ||
                                        stack[calleeIndex].ObjectValue is not SyncFunctionInvoker helperResult ||
                                        !ReferenceEquals(helperResult, frame.Invoker))
                                    {
                                        return StepResult.Bail;
                                    }

                                    var receiverIndex = calleeIndex - 1;
                                    stack[receiverIndex] = frame.Invoker!._cachedJsValue;
                                    SetExpressionStackFlag(frame, receiverIndex, false);
                                    frame.ExpressionStackIndex = receiverIndex + 1;
                                    frame.ExpressionProgramCounter++;
                                    break;
                                }

                                if (calleeIndex < 0 ||
                                    stack[calleeIndex].ObjectValue is not SyncFunctionInvoker callee ||
                                    !ReferenceEquals(callee, frame.Invoker))
                                {
                                    return StepResult.Bail;
                                }

                                var restartThisValue = JsValue.Undefined;
                                if (operation.HasExplicitThis)
                                {
                                    var receiverIndex = calleeIndex - 1;
                                    if (receiverIndex < 0)
                                    {
                                        return StepResult.Bail;
                                    }

                                    restartThisValue = stack[receiverIndex];
                                }
                                else if (!callee._isStrict)
                                {
                                    return StepResult.Bail;
                                }

                                frame.ExpressionProgramCounter++;
                                InitializeFrame(ref frame, callee, restartThisValue, frame.Plan);

                                var activationSlots = frame.Plan.ActivationSlots!;
                                for (var i = 0; i < activationSlots.ParameterSlotIndices.Length; i++)
                                {
                                    var slotIndex = activationSlots.ParameterSlotIndices[i];
                                    frame.Slots![slotIndex] = i < argumentCount
                                        ? stack[calleeIndex + 1 + i]
                                        : JsValue.Undefined;
                                }

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

            private static bool TryResolveSelfReturnHelperIdentifier(
                SyncIrFrame frame,
                IdentifierOperand identifier)
            {
                var invoker = frame.Invoker!;
                var activationSlots = frame.ActivationSlots!;
                return !IsParameterSlot(identifier, activationSlots) &&
                       (HasLocalSelfReturnHelperDeclaration(invoker, identifier.Name) ||
                        TryGetClosureSelfReturnHelper(invoker, identifier.Name, out _));
            }

            private static bool ReturnFromFrame(
                ref SyncIrFrame[] frames,
                ref int depth,
                JsValue value,
                out JsValue result)
            {
                result = value;
                depth--;

                if (depth == 0)
                {
                    return false;
                }

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
                EnsureExpressionStackFlagCapacity(ref frame, Math.Max(program.MaxStackDepth, 1));
                ClearExpressionStackFlags(ref frame);
            }

            private static void EnsureFrameCapacity(ref SyncIrFrame[] frames, int required, int depth)
            {
                if (required <= frames.Length)
                {
                    return;
                }

                var oldFrames = frames;
                var newLength = checked(Math.Max(oldFrames.Length * 2, required));
                frames = ArrayPool<SyncIrFrame>.Shared.Rent(newLength);
                Array.Copy(oldFrames, frames, oldFrames.Length);
                ClearFrameSlots(oldFrames, depth);
                ArrayPool<SyncIrFrame>.Shared.Return(oldFrames, clearArray: false);
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

            private static void EnsureExpressionStackFlagCapacity(ref SyncIrFrame frame, int required)
            {
                var wordCount = (Math.Max(required, 1) + 63) >> 6;
                if (frame.ExpressionStackFlags is { } flags && flags.Length >= wordCount)
                {
                    return;
                }

                frame.ExpressionStackFlags = new ulong[wordCount];
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            private static bool GetExpressionStackFlag(SyncIrFrame frame, int index)
            {
                var flags = frame.ExpressionStackFlags!;
                return (flags[index >> 6] & (1UL << (index & 63))) != 0;
            }

            [MethodImpl(JsEngineConstants.Inlining)]
            private static void SetExpressionStackFlag(SyncIrFrame frame, int index, bool value)
            {
                var flags = frame.ExpressionStackFlags!;
                var wordIndex = index >> 6;
                var bit = 1UL << (index & 63);
                ref var word = ref flags[wordIndex];
                if (value)
                {
                    word |= bit;
                }
                else
                {
                    word &= ~bit;
                }
            }

            private static void ClearExpressionStackFlags(ref SyncIrFrame frame)
            {
                if (frame.ExpressionStackFlags is { } flags)
                {
                    Array.Clear(flags);
                }
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

                    if (frames[i].ExpressionStackFlags is { } flags)
                    {
                        Array.Clear(flags);
                    }
                    frames[i].Invoker = null;
                    frames[i].Plan = default!;
                    frames[i].ActivationSlots = null;
                    frames[i].ThisValue = JsValue.Undefined;
                    frames[i].ProgramCounter = 0;
                    frames[i].ExpressionActive = false;
                    frames[i].ExpressionPurpose = ExpressionPurpose.None;
                    frames[i].ExpressionProgram = ExpressionProgram.Empty;
                    frames[i].ExpressionProgramCounter = 0;
                    frames[i].ExpressionStackIndex = 0;
                    frames[i].BranchConsequent = -1;
                    frames[i].BranchAlternate = -1;
                }
            }

            private static void ClearFrameSlots(SyncIrFrame[] frames, int maxDepth)
            {
                for (var i = 0; i < maxDepth; i++)
                {
                    frames[i] = default;
                }
            }

            private static JsValue InvokeCurrentFrameNormally(
                SyncIrFrame frame,
                EvaluationContext context)
            {
                var invoker = frame.Invoker!;
                var activationSlots = frame.ActivationSlots!;
                var arguments = new JsValue[activationSlots.ParameterSlotIndices.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = frame.Slots![activationSlots.ParameterSlotIndices[i]];
                }

                var previousDisable = context.DisableSyncIrCallTrampoline;
                context.DisableSyncIrCallTrampoline = true;
                try
                {
                    return invoker.InvokeWithContext(arguments, frame.ThisValue, context);
                }
                finally
                {
                    context.DisableSyncIrCallTrampoline = previousDisable;
                }
            }

            private static JsArray GetOrCreateProgramTemplateObject(
                TaggedTemplateDescriptor descriptor,
                EvaluationContext context)
            {
                if (context.RealmState.TemplateObjectCache.TryGetValue(descriptor, out var cachedTemplate))
                {
                    return (JsArray)cachedTemplate;
                }

                var stringsArray = new JsArray(descriptor.CookedStrings, context.RealmState);
                var rawStringsArray = new JsArray(descriptor.RawStrings, context.RealmState);
                var templateObject = (JsArray)stringsArray.CreateTemplateObject(rawStringsArray);
                context.RealmState.TemplateObjectCache[descriptor] = templateObject;
                return templateObject;
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
                SelfCallee,
                SelfReturnHelperCallee
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
                public ulong[]? ExpressionStackFlags;
                public int ExpressionStackIndex;
                public int BranchConsequent;
                public int BranchAlternate;
            }
        }
    }
}
