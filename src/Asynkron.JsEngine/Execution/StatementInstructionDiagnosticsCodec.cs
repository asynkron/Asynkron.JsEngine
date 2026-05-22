using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal enum EncodedStatementOpcode : byte
{
    Jump = 1,
    Break = 2,
    Continue = 3,
    SetCompletionValue = 4,
    BreakableExit = 5,
    EvaluateAndDiscard = 6,
    AwaitAndDiscard = 7,
    Throw = 8,
    Return = 9,
    AssignmentSlot = 10,
    SimpleVariableDeclaration = 11,
    BindingVariableDeclaration = 12
}

internal readonly record struct EncodedStatementInstruction(
    EncodedStatementOpcode Opcode,
    int NextOrTarget,
    int Operand,
    int Extra,
    EncodedStatementSidePayload Payload)
{
    public const int FixedHeaderByteSize = 16;

    public long EstimatedCompactByteSize => FixedHeaderByteSize + Payload.EstimatedCompactByteSize;
}

internal readonly record struct EncodedStatementSidePayload(
    ExpressionProgram? PrimaryExpressionProgram = null,
    ExpressionProgram? SecondaryExpressionProgram = null,
    Symbol? PrimarySymbol = null,
    Symbol? SecondarySymbol = null,
    BindingTargetProgram? BindingTargetProgram = null,
    int ScopeId = -1,
    int FlatSlotId = -1)
{
    private const int ReferencePayloadByteSize = 8;
    private const int AssignmentMetadataByteSize = 8;

    public long EstimatedCompactByteSize =>
        (PrimaryExpressionProgram is null ? 0 : ReferencePayloadByteSize) +
        (SecondaryExpressionProgram is null ? 0 : ReferencePayloadByteSize) +
        (PrimarySymbol is null ? 0 : ReferencePayloadByteSize) +
        (SecondarySymbol is null ? 0 : ReferencePayloadByteSize) +
        (BindingTargetProgram is null ? 0 : ReferencePayloadByteSize) +
        ((ScopeId >= 0 || FlatSlotId >= 0) ? AssignmentMetadataByteSize : 0);
}

/// <summary>
/// Diagnostic-only codec for a small, stable subset of statement instructions.
/// This is intentionally scoped to parity testing and does not alter runtime execution.
/// </summary>
internal static class StatementInstructionDiagnosticsCodec
{
    private const int AssignmentSlotSuppressCompletionBit = 1 << 0;
    private const int AssignmentSlotAllowNameInferenceBit = 1 << 1;
    private const int SimpleVariableAllowNameInferenceBit = 1 << 0;
    private const int SimpleVariableIsScriptLevelBit = 1 << 1;

    public static bool IsSupportedKind(InstructionKind kind)
    {
        return kind is
            InstructionKind.Jump or
            InstructionKind.Break or
            InstructionKind.Continue or
            InstructionKind.SetCompletionValue or
            InstructionKind.BreakableExit or
            InstructionKind.EvaluateAndDiscard or
            InstructionKind.AwaitAndDiscard or
            InstructionKind.Throw or
            InstructionKind.Return or
            InstructionKind.AssignmentSlot or
            InstructionKind.SimpleVariableDeclaration or
            InstructionKind.BindingVariableDeclaration;
    }

    public static bool TryEncode(ExecutionInstruction instruction, out EncodedStatementInstruction encoded)
    {
        switch (instruction)
        {
            case JumpInstruction jump:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Jump,
                    jump.TargetIndex,
                    0,
                    0,
                    default);
                return true;
            case BreakInstruction @break:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Break,
                    @break.TargetIndex,
                    @break.TargetScopeId,
                    0,
                    default);
                return true;
            case ContinueInstruction @continue:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Continue,
                    @continue.TargetIndex,
                    @continue.TargetScopeId,
                    0,
                    default);
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.SetCompletionValue,
                    setCompletion.Next,
                    0,
                    0,
                    default);
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.BreakableExit,
                    breakableExit.Next,
                    0,
                    0,
                    default);
                return true;
            case EvaluateAndDiscardInstruction evaluateAndDiscard:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.EvaluateAndDiscard,
                    evaluateAndDiscard.Next,
                    evaluateAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: evaluateAndDiscard.ExpressionProgram));
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.AwaitAndDiscard,
                    awaitAndDiscard.Next,
                    awaitAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: awaitAndDiscard.AwaitedProgram,
                        PrimarySymbol: awaitAndDiscard.AwaitStateKey));
                return true;
            case ThrowInstruction throwInstruction:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Throw,
                    -1,
                    0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: throwInstruction.ThrowProgram,
                        SecondaryExpressionProgram: throwInstruction.AwaitedProgram,
                        PrimarySymbol: throwInstruction.AwaitStateKey));
                return true;
            case ReturnInstruction returnInstruction:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Return,
                    returnInstruction.Next,
                    0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: returnInstruction.ReturnProgram,
                        SecondaryExpressionProgram: returnInstruction.AwaitedProgram,
                        PrimarySymbol: returnInstruction.AwaitStateKey));
                return true;
            case AssignmentSlotInstruction assignmentSlot:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.AssignmentSlot,
                    assignmentSlot.Next,
                    GetAssignmentSlotFlags(assignmentSlot),
                    assignmentSlot.SlotIndex,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: assignmentSlot.ValueProgram,
                        SecondaryExpressionProgram: assignmentSlot.AwaitedProgram,
                        PrimarySymbol: assignmentSlot.AwaitStateKey,
                        SecondarySymbol: assignmentSlot.TargetSymbol,
                        ScopeId: assignmentSlot.ScopeId,
                        FlatSlotId: assignmentSlot.FlatSlotId));
                return true;
            case SimpleVariableDeclarationInstruction simpleVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.SimpleVariableDeclaration,
                    simpleVariableDeclaration.Next,
                    (int)simpleVariableDeclaration.VarKind,
                    GetSimpleVariableDeclarationFlags(simpleVariableDeclaration),
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: simpleVariableDeclaration.InitializerProgram,
                        SecondaryExpressionProgram: simpleVariableDeclaration.AwaitedProgram,
                        PrimarySymbol: simpleVariableDeclaration.AwaitStateKey,
                        SecondarySymbol: simpleVariableDeclaration.TargetSymbol));
                return true;
            case BindingVariableDeclarationInstruction bindingVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.BindingVariableDeclaration,
                    bindingVariableDeclaration.Next,
                    (int)bindingVariableDeclaration.VarKind,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgram: bindingVariableDeclaration.InitializerProgram,
                        SecondaryExpressionProgram: bindingVariableDeclaration.AwaitedProgram,
                        PrimarySymbol: bindingVariableDeclaration.AwaitStateKey,
                        BindingTargetProgram: bindingVariableDeclaration.TargetProgram));
                return true;
            default:
                encoded = default;
                return false;
        }
    }

    public static ExecutionInstruction Decode(EncodedStatementInstruction encoded)
    {
        return encoded.Opcode switch
        {
            EncodedStatementOpcode.Jump => new JumpInstruction(encoded.NextOrTarget),
            EncodedStatementOpcode.Break => new BreakInstruction(encoded.NextOrTarget, encoded.Operand),
            EncodedStatementOpcode.Continue => new ContinueInstruction(encoded.NextOrTarget, encoded.Operand),
            EncodedStatementOpcode.SetCompletionValue => new SetCompletionValueInstruction(encoded.NextOrTarget),
            EncodedStatementOpcode.BreakableExit => new BreakableExitInstruction(encoded.NextOrTarget),
            EncodedStatementOpcode.EvaluateAndDiscard => new EvaluateAndDiscardInstruction(
                encoded.NextOrTarget,
                encoded.Payload.PrimaryExpressionProgram ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            EncodedStatementOpcode.AwaitAndDiscard => new AwaitAndDiscardInstruction(
                encoded.NextOrTarget,
                encoded.Payload.PrimarySymbol ?? Symbol.Intern("__await_state"),
                encoded.Payload.PrimaryExpressionProgram ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            EncodedStatementOpcode.Throw => new ThrowInstruction(
                encoded.Payload.PrimaryExpressionProgram,
                encoded.Payload.PrimarySymbol,
                encoded.Payload.SecondaryExpressionProgram),
            EncodedStatementOpcode.Return => new ReturnInstruction(
                encoded.NextOrTarget,
                encoded.Payload.PrimaryExpressionProgram,
                encoded.Payload.PrimarySymbol,
                encoded.Payload.SecondaryExpressionProgram),
            EncodedStatementOpcode.AssignmentSlot => new AssignmentSlotInstruction(
                encoded.NextOrTarget,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__assignment_target"),
                ValueProgram: encoded.Payload.PrimaryExpressionProgram,
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: encoded.Payload.SecondaryExpressionProgram,
                SuppressCompletionValue: (encoded.Operand & AssignmentSlotSuppressCompletionBit) != 0,
                AllowNameInference: (encoded.Operand & AssignmentSlotAllowNameInferenceBit) != 0,
                ScopeId: encoded.Payload.ScopeId,
                SlotIndex: encoded.Extra,
                FlatSlotId: encoded.Payload.FlatSlotId),
            EncodedStatementOpcode.SimpleVariableDeclaration => new SimpleVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__declaration_target"),
                InitializerProgram: encoded.Payload.PrimaryExpressionProgram,
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: encoded.Payload.SecondaryExpressionProgram,
                AllowNameInference: (encoded.Extra & SimpleVariableAllowNameInferenceBit) != 0,
                IsScriptLevel: (encoded.Extra & SimpleVariableIsScriptLevelBit) != 0),
            EncodedStatementOpcode.BindingVariableDeclaration => new BindingVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.Payload.BindingTargetProgram ?? new IdentifierBindingTargetProgram(Symbol.Intern("__binding_target")),
                InitializerProgram: encoded.Payload.PrimaryExpressionProgram,
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: encoded.Payload.SecondaryExpressionProgram),
            _ => throw new ArgumentOutOfRangeException(nameof(encoded), encoded.Opcode, "Unsupported diagnostic opcode")
        };
    }

    private static int GetAssignmentSlotFlags(AssignmentSlotInstruction instruction)
    {
        var flags = 0;
        if (instruction.SuppressCompletionValue)
        {
            flags |= AssignmentSlotSuppressCompletionBit;
        }

        if (instruction.AllowNameInference)
        {
            flags |= AssignmentSlotAllowNameInferenceBit;
        }

        return flags;
    }

    private static int GetSimpleVariableDeclarationFlags(SimpleVariableDeclarationInstruction instruction)
    {
        var flags = 0;
        if (instruction.AllowNameInference)
        {
            flags |= SimpleVariableAllowNameInferenceBit;
        }

        if (instruction.IsScriptLevel)
        {
            flags |= SimpleVariableIsScriptLevelBit;
        }

        return flags;
    }

}
