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
    int PrimaryExpressionProgramReferenceId = -1,
    int SecondaryExpressionProgramReferenceId = -1,
    Symbol? PrimarySymbol = null,
    Symbol? SecondarySymbol = null,
    BindingTargetProgram? BindingTargetProgram = null,
    int ScopeId = -1,
    int FlatSlotId = -1,
    bool HasAssignmentMetadata = false)
{
    private const int ReferencePayloadByteSize = 8;
    private const int AssignmentMetadataByteSize = 8;

    public long EstimatedCompactByteSize =>
        (PrimaryExpressionProgramReferenceId < 0 ? 0 : ReferencePayloadByteSize) +
        (SecondaryExpressionProgramReferenceId < 0 ? 0 : ReferencePayloadByteSize) +
        (PrimarySymbol is null ? 0 : ReferencePayloadByteSize) +
        (SecondarySymbol is null ? 0 : ReferencePayloadByteSize) +
        (BindingTargetProgram is null ? 0 : ReferencePayloadByteSize) +
        (HasAssignmentMetadata ? AssignmentMetadataByteSize : 0);
}

internal sealed class StatementDiagnosticsExpressionProgramTable
{
    private readonly Dictionary<ExpressionProgram, int> _indices = [];
    private readonly List<ExpressionProgram> _programs = [];

    public int Count => _programs.Count;

    public int GetOrAdd(ExpressionProgram? program)
    {
        if (!program.HasValue)
        {
            return -1;
        }

        return GetOrAdd(program.Value);
    }

    public int GetOrAdd(ExpressionProgram program)
    {
        if (_indices.TryGetValue(program, out var existing))
        {
            return existing;
        }

        var created = _programs.Count;
        _programs.Add(program);
        _indices.Add(program, created);
        return created;
    }

    public ExpressionProgram? Resolve(int id)
    {
        return id >= 0 && id < _programs.Count ? _programs[id] : null;
    }
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

    public static bool TryEncode(
        ExecutionInstruction instruction,
        StatementDiagnosticsExpressionProgramTable expressionPrograms,
        out EncodedStatementInstruction encoded)
    {
        switch (instruction)
        {
            case JumpInstruction jump:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Jump,
                    jump.TargetIndex,
                    0,
                    0,
                    new EncodedStatementSidePayload(-1, -1));
                return true;
            case BreakInstruction @break:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Break,
                    @break.TargetIndex,
                    @break.TargetScopeId,
                    0,
                    new EncodedStatementSidePayload(-1, -1));
                return true;
            case ContinueInstruction @continue:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Continue,
                    @continue.TargetIndex,
                    @continue.TargetScopeId,
                    0,
                    new EncodedStatementSidePayload(-1, -1));
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.SetCompletionValue,
                    setCompletion.Next,
                    0,
                    0,
                    new EncodedStatementSidePayload(-1, -1));
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.BreakableExit,
                    breakableExit.Next,
                    0,
                    0,
                    new EncodedStatementSidePayload(-1, -1));
                return true;
            case EvaluateAndDiscardInstruction evaluateAndDiscard:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.EvaluateAndDiscard,
                    evaluateAndDiscard.Next,
                    evaluateAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(evaluateAndDiscard.ExpressionProgram)));
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.AwaitAndDiscard,
                    awaitAndDiscard.Next,
                    awaitAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(awaitAndDiscard.AwaitedProgram),
                        PrimarySymbol: awaitAndDiscard.AwaitStateKey));
                return true;
            case ThrowInstruction throwInstruction:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Throw,
                    -1,
                    0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(throwInstruction.ThrowProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(throwInstruction.AwaitedProgram),
                        PrimarySymbol: throwInstruction.AwaitStateKey));
                return true;
            case ReturnInstruction returnInstruction:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.Return,
                    returnInstruction.Next,
                    0,
                    0,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(returnInstruction.ReturnProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(returnInstruction.AwaitedProgram),
                        PrimarySymbol: returnInstruction.AwaitStateKey));
                return true;
            case AssignmentSlotInstruction assignmentSlot:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.AssignmentSlot,
                    assignmentSlot.Next,
                    GetAssignmentSlotFlags(assignmentSlot),
                    assignmentSlot.SlotIndex,
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(assignmentSlot.ValueProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(assignmentSlot.AwaitedProgram),
                        PrimarySymbol: assignmentSlot.AwaitStateKey,
                        SecondarySymbol: assignmentSlot.TargetSymbol,
                        ScopeId: assignmentSlot.ScopeId,
                        FlatSlotId: assignmentSlot.FlatSlotId,
                        HasAssignmentMetadata: true));
                return true;
            case SimpleVariableDeclarationInstruction simpleVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    EncodedStatementOpcode.SimpleVariableDeclaration,
                    simpleVariableDeclaration.Next,
                    (int)simpleVariableDeclaration.VarKind,
                    GetSimpleVariableDeclarationFlags(simpleVariableDeclaration),
                    new EncodedStatementSidePayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(simpleVariableDeclaration.InitializerProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(simpleVariableDeclaration.AwaitedProgram),
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
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(bindingVariableDeclaration.InitializerProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(bindingVariableDeclaration.AwaitedProgram),
                        PrimarySymbol: bindingVariableDeclaration.AwaitStateKey,
                        BindingTargetProgram: bindingVariableDeclaration.TargetProgram));
                return true;
            default:
                encoded = default;
                return false;
        }
    }

    public static bool TryEncode(ExecutionInstruction instruction, out EncodedStatementInstruction encoded)
    {
        return TryEncode(instruction, new StatementDiagnosticsExpressionProgramTable(), out encoded);
    }

    public static ExecutionInstruction Decode(
        EncodedStatementInstruction encoded,
        StatementDiagnosticsExpressionProgramTable expressionPrograms)
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
                expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId) ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            EncodedStatementOpcode.AwaitAndDiscard => new AwaitAndDiscardInstruction(
                encoded.NextOrTarget,
                encoded.Payload.PrimarySymbol ?? Symbol.Intern("__await_state"),
                expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId) ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            EncodedStatementOpcode.Throw => new ThrowInstruction(
                expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId),
                encoded.Payload.PrimarySymbol,
                expressionPrograms.Resolve(encoded.Payload.SecondaryExpressionProgramReferenceId)),
            EncodedStatementOpcode.Return => new ReturnInstruction(
                encoded.NextOrTarget,
                expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId),
                encoded.Payload.PrimarySymbol,
                expressionPrograms.Resolve(encoded.Payload.SecondaryExpressionProgramReferenceId)),
            EncodedStatementOpcode.AssignmentSlot => new AssignmentSlotInstruction(
                encoded.NextOrTarget,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__assignment_target"),
                ValueProgram: expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: expressionPrograms.Resolve(encoded.Payload.SecondaryExpressionProgramReferenceId),
                SuppressCompletionValue: (encoded.Operand & AssignmentSlotSuppressCompletionBit) != 0,
                AllowNameInference: (encoded.Operand & AssignmentSlotAllowNameInferenceBit) != 0,
                ScopeId: encoded.Payload.HasAssignmentMetadata ? encoded.Payload.ScopeId : -1,
                SlotIndex: encoded.Extra,
                FlatSlotId: encoded.Payload.HasAssignmentMetadata ? encoded.Payload.FlatSlotId : -1),
            EncodedStatementOpcode.SimpleVariableDeclaration => new SimpleVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__declaration_target"),
                InitializerProgram: expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: expressionPrograms.Resolve(encoded.Payload.SecondaryExpressionProgramReferenceId),
                AllowNameInference: (encoded.Extra & SimpleVariableAllowNameInferenceBit) != 0,
                IsScriptLevel: (encoded.Extra & SimpleVariableIsScriptLevelBit) != 0),
            EncodedStatementOpcode.BindingVariableDeclaration => new BindingVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.Payload.BindingTargetProgram ?? new IdentifierBindingTargetProgram(Symbol.Intern("__binding_target")),
                InitializerProgram: expressionPrograms.Resolve(encoded.Payload.PrimaryExpressionProgramReferenceId),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: expressionPrograms.Resolve(encoded.Payload.SecondaryExpressionProgramReferenceId)),
            _ => throw new ArgumentOutOfRangeException(nameof(encoded), encoded.Opcode, "Unsupported diagnostic opcode")
        };
    }

    public static ExecutionInstruction Decode(EncodedStatementInstruction encoded)
    {
        return Decode(encoded, new StatementDiagnosticsExpressionProgramTable());
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
