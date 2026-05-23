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

internal readonly record struct CompactStatementInstruction(
    CompactStatementHeader Header,
    CompactStatementPayload Payload)
{
    public long EstimatedCompactByteSize => Header.EstimatedCompactByteSize + Payload.EstimatedCompactByteSize;
}

internal readonly record struct CompactStatementHeader(
    EncodedStatementOpcode Opcode,
    int NextOrTarget,
    int Operand,
    int Extra)
{
    public const int FixedByteSize = 16;

    public long EstimatedCompactByteSize => FixedByteSize;
}

internal readonly record struct CompactStatementPayload(
    int PrimaryExpressionProgramReferenceId = -1,
    int SecondaryExpressionProgramReferenceId = -1,
    int BindingTargetProgramReferenceId = -1,
    ExpressionProgram? PrimaryExpressionProgram = null,
    ExpressionProgram? SecondaryExpressionProgram = null,
    Symbol? PrimarySymbol = null,
    Symbol? SecondarySymbol = null,
    BindingTargetProgram? BindingTargetProgram = null,
    int ScopeId = -1,
    int FlatSlotId = -1,
    bool HasAssignmentMetadata = false)
{
    public static CompactStatementPayload Empty => new(
        PrimaryExpressionProgramReferenceId: -1,
        SecondaryExpressionProgramReferenceId: -1,
        BindingTargetProgramReferenceId: -1);

    private const int ReferencePayloadByteSize = 8;
    private const int AssignmentMetadataByteSize = 8;

    public long EstimatedCompactByteSize =>
        ((PrimaryExpressionProgramReferenceId < 0 && !PrimaryExpressionProgram.HasValue) ? 0 : ReferencePayloadByteSize) +
        ((SecondaryExpressionProgramReferenceId < 0 && !SecondaryExpressionProgram.HasValue) ? 0 : ReferencePayloadByteSize) +
        ((BindingTargetProgramReferenceId < 0 && BindingTargetProgram is null) ? 0 : ReferencePayloadByteSize) +
        (PrimarySymbol is null ? 0 : ReferencePayloadByteSize) +
        (SecondarySymbol is null ? 0 : ReferencePayloadByteSize) +
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

internal sealed class StatementDiagnosticsBindingTargetProgramTable
{
    private readonly Dictionary<BindingTargetProgram, int> _indices = [];
    private readonly List<BindingTargetProgram> _programs = [];

    public int Count => _programs.Count;

    public int GetOrAdd(BindingTargetProgram? program)
    {
        if (program is null)
        {
            return -1;
        }

        if (_indices.TryGetValue(program, out var existing))
        {
            return existing;
        }

        var created = _programs.Count;
        _programs.Add(program);
        _indices.Add(program, created);
        return created;
    }

    public BindingTargetProgram? Resolve(int id)
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
        StatementDiagnosticsBindingTargetProgramTable bindingTargets,
        out CompactStatementInstruction encoded)
    {
        switch (instruction)
        {
            case JumpInstruction jump:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.Jump, jump.TargetIndex, 0, 0),
                    CompactStatementPayload.Empty);
                return true;
            case BreakInstruction @break:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.Break, @break.TargetIndex, @break.TargetScopeId, 0),
                    CompactStatementPayload.Empty);
                return true;
            case ContinueInstruction @continue:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.Continue, @continue.TargetIndex, @continue.TargetScopeId, 0),
                    CompactStatementPayload.Empty);
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.SetCompletionValue, setCompletion.Next, 0, 0),
                    CompactStatementPayload.Empty);
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.BreakableExit, breakableExit.Next, 0, 0),
                    CompactStatementPayload.Empty);
                return true;
            case EvaluateAndDiscardInstruction evaluateAndDiscard:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.EvaluateAndDiscard, evaluateAndDiscard.Next, evaluateAndDiscard.SuppressCompletionValue ? 1 : 0, 0),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(evaluateAndDiscard.ExpressionProgram)));
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.AwaitAndDiscard, awaitAndDiscard.Next, awaitAndDiscard.SuppressCompletionValue ? 1 : 0, 0),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(awaitAndDiscard.AwaitedProgram),
                        PrimarySymbol: awaitAndDiscard.AwaitStateKey));
                return true;
            case ThrowInstruction throwInstruction:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.Throw, -1, 0, 0),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(throwInstruction.ThrowProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(throwInstruction.AwaitedProgram),
                        PrimarySymbol: throwInstruction.AwaitStateKey));
                return true;
            case ReturnInstruction returnInstruction:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.Return, returnInstruction.Next, 0, 0),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(returnInstruction.ReturnProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(returnInstruction.AwaitedProgram),
                        PrimarySymbol: returnInstruction.AwaitStateKey));
                return true;
            case AssignmentSlotInstruction assignmentSlot:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.AssignmentSlot, assignmentSlot.Next, GetAssignmentSlotFlags(assignmentSlot), assignmentSlot.SlotIndex),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(assignmentSlot.ValueProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(assignmentSlot.AwaitedProgram),
                        PrimarySymbol: assignmentSlot.AwaitStateKey,
                        SecondarySymbol: assignmentSlot.TargetSymbol,
                        ScopeId: assignmentSlot.ScopeId,
                        FlatSlotId: assignmentSlot.FlatSlotId,
                        HasAssignmentMetadata: true));
                return true;
            case SimpleVariableDeclarationInstruction simpleVariableDeclaration:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.SimpleVariableDeclaration, simpleVariableDeclaration.Next, (int)simpleVariableDeclaration.VarKind, GetSimpleVariableDeclarationFlags(simpleVariableDeclaration)),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(simpleVariableDeclaration.InitializerProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(simpleVariableDeclaration.AwaitedProgram),
                        PrimarySymbol: simpleVariableDeclaration.AwaitStateKey,
                        SecondarySymbol: simpleVariableDeclaration.TargetSymbol));
                return true;
            case BindingVariableDeclarationInstruction bindingVariableDeclaration:
                encoded = new CompactStatementInstruction(
                    new CompactStatementHeader(EncodedStatementOpcode.BindingVariableDeclaration, bindingVariableDeclaration.Next, (int)bindingVariableDeclaration.VarKind, 0),
                    new CompactStatementPayload(
                        PrimaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(bindingVariableDeclaration.InitializerProgram),
                        SecondaryExpressionProgramReferenceId: expressionPrograms.GetOrAdd(bindingVariableDeclaration.AwaitedProgram),
                        BindingTargetProgramReferenceId: bindingTargets.GetOrAdd(bindingVariableDeclaration.TargetProgram),
                        PrimarySymbol: bindingVariableDeclaration.AwaitStateKey,
                        BindingTargetProgram: bindingVariableDeclaration.TargetProgram));
                return true;
            default:
                encoded = default;
                return false;
        }
    }

    public static bool TryEncode(
        ExecutionInstruction instruction,
        StatementDiagnosticsExpressionProgramTable expressionPrograms,
        out CompactStatementInstruction encoded)
    {
        return TryEncode(
            instruction,
            expressionPrograms,
            new StatementDiagnosticsBindingTargetProgramTable(),
            out encoded);
    }

    public static bool TryEncode(ExecutionInstruction instruction, out CompactStatementInstruction encoded)
    {
        if (!TryEncode(
            instruction,
            new StatementDiagnosticsExpressionProgramTable(),
            new StatementDiagnosticsBindingTargetProgramTable(),
            out encoded))
        {
            return false;
        }

        encoded = instruction switch
        {
            EvaluateAndDiscardInstruction evaluateAndDiscard => encoded with
            {
                Payload = encoded.Payload with { PrimaryExpressionProgram = evaluateAndDiscard.ExpressionProgram }
            },
            AwaitAndDiscardInstruction awaitAndDiscard => encoded with
            {
                Payload = encoded.Payload with { PrimaryExpressionProgram = awaitAndDiscard.AwaitedProgram }
            },
            ThrowInstruction throwInstruction => encoded with
            {
                Payload = encoded.Payload with
                {
                    PrimaryExpressionProgram = throwInstruction.ThrowProgram,
                    SecondaryExpressionProgram = throwInstruction.AwaitedProgram
                }
            },
            ReturnInstruction returnInstruction => encoded with
            {
                Payload = encoded.Payload with
                {
                    PrimaryExpressionProgram = returnInstruction.ReturnProgram,
                    SecondaryExpressionProgram = returnInstruction.AwaitedProgram
                }
            },
            AssignmentSlotInstruction assignmentSlot => encoded with
            {
                Payload = encoded.Payload with
                {
                    PrimaryExpressionProgram = assignmentSlot.ValueProgram,
                    SecondaryExpressionProgram = assignmentSlot.AwaitedProgram
                }
            },
            SimpleVariableDeclarationInstruction simpleVariable => encoded with
            {
                Payload = encoded.Payload with
                {
                    PrimaryExpressionProgram = simpleVariable.InitializerProgram,
                    SecondaryExpressionProgram = simpleVariable.AwaitedProgram
                }
            },
            BindingVariableDeclarationInstruction bindingVariable => encoded with
            {
                Payload = encoded.Payload with
                {
                    PrimaryExpressionProgram = bindingVariable.InitializerProgram,
                    SecondaryExpressionProgram = bindingVariable.AwaitedProgram
                }
            },
            _ => encoded
        };

        return true;
    }

    public static ExecutionInstruction Decode(
        CompactStatementInstruction encoded,
        StatementDiagnosticsExpressionProgramTable expressionPrograms,
        StatementDiagnosticsBindingTargetProgramTable bindingTargets)
    {
        var header = encoded.Header;
        return header.Opcode switch
        {
            EncodedStatementOpcode.Jump => new JumpInstruction(header.NextOrTarget),
            EncodedStatementOpcode.Break => new BreakInstruction(header.NextOrTarget, header.Operand),
            EncodedStatementOpcode.Continue => new ContinueInstruction(header.NextOrTarget, header.Operand),
            EncodedStatementOpcode.SetCompletionValue => new SetCompletionValueInstruction(header.NextOrTarget),
            EncodedStatementOpcode.BreakableExit => new BreakableExitInstruction(header.NextOrTarget),
            EncodedStatementOpcode.EvaluateAndDiscard => new EvaluateAndDiscardInstruction(
                header.NextOrTarget,
                ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms) ?? ExpressionProgram.Empty,
                SuppressCompletionValue: header.Operand != 0),
            EncodedStatementOpcode.AwaitAndDiscard => new AwaitAndDiscardInstruction(
                header.NextOrTarget,
                encoded.Payload.PrimarySymbol ?? Symbol.Intern("__await_state"),
                ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms) ?? ExpressionProgram.Empty,
                SuppressCompletionValue: header.Operand != 0),
            EncodedStatementOpcode.Throw => new ThrowInstruction(
                ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
                encoded.Payload.PrimarySymbol,
                ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms)),
            EncodedStatementOpcode.Return => new ReturnInstruction(
                header.NextOrTarget,
                ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
                encoded.Payload.PrimarySymbol,
                ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms)),
            EncodedStatementOpcode.AssignmentSlot => new AssignmentSlotInstruction(
                header.NextOrTarget,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__assignment_target"),
                ValueProgram: ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms),
                SuppressCompletionValue: (header.Operand & AssignmentSlotSuppressCompletionBit) != 0,
                AllowNameInference: (header.Operand & AssignmentSlotAllowNameInferenceBit) != 0,
                ScopeId: encoded.Payload.HasAssignmentMetadata ? encoded.Payload.ScopeId : -1,
                SlotIndex: header.Extra,
                FlatSlotId: encoded.Payload.HasAssignmentMetadata ? encoded.Payload.FlatSlotId : -1),
            EncodedStatementOpcode.SimpleVariableDeclaration => new SimpleVariableDeclarationInstruction(
                header.NextOrTarget,
                (VariableKind)header.Operand,
                encoded.Payload.SecondarySymbol ?? Symbol.Intern("__declaration_target"),
                InitializerProgram: ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms),
                AllowNameInference: (header.Extra & SimpleVariableAllowNameInferenceBit) != 0,
                IsScriptLevel: (header.Extra & SimpleVariableIsScriptLevelBit) != 0),
            EncodedStatementOpcode.BindingVariableDeclaration => new BindingVariableDeclarationInstruction(
                header.NextOrTarget,
                (VariableKind)header.Operand,
                ResolveBindingTargetProgram(encoded.Payload.BindingTargetProgramReferenceId, encoded.Payload.BindingTargetProgram, bindingTargets) ??
                    new IdentifierBindingTargetProgram(Symbol.Intern("__binding_target")),
                InitializerProgram: ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
                AwaitStateKey: encoded.Payload.PrimarySymbol,
                AwaitedProgram: ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms)),
            _ => throw new ArgumentOutOfRangeException(nameof(encoded), header.Opcode, "Unsupported diagnostic opcode")
        };
    }

    public static ExecutionInstruction Decode(
        CompactStatementInstruction encoded,
        StatementDiagnosticsExpressionProgramTable expressionPrograms)
    {
        return Decode(encoded, expressionPrograms, new StatementDiagnosticsBindingTargetProgramTable());
    }

    public static ExecutionInstruction Decode(CompactStatementInstruction encoded)
    {
        return Decode(
            encoded,
            new StatementDiagnosticsExpressionProgramTable(),
            new StatementDiagnosticsBindingTargetProgramTable());
    }

    public static ExecutionInstruction Decode(
        CompactStatementInstruction encoded,
        IReadOnlyList<ExpressionProgram?> expressionPrograms)
    {
        var payload = encoded.Payload with
        {
            PrimaryExpressionProgram = ResolveExpressionProgram(encoded.Payload.PrimaryExpressionProgramReferenceId, encoded.Payload.PrimaryExpressionProgram, expressionPrograms),
            SecondaryExpressionProgram = ResolveExpressionProgram(encoded.Payload.SecondaryExpressionProgramReferenceId, encoded.Payload.SecondaryExpressionProgram, expressionPrograms)
        };

        return Decode(encoded with { Payload = payload });
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

    private static ExpressionProgram? ResolveExpressionProgram(
        int id,
        ExpressionProgram? embeddedProgram,
        StatementDiagnosticsExpressionProgramTable expressionPrograms)
    {
        return expressionPrograms.Resolve(id) ?? embeddedProgram;
    }

    private static ExpressionProgram? ResolveExpressionProgram(
        int id,
        ExpressionProgram? embeddedProgram,
        IReadOnlyList<ExpressionProgram?> expressionPrograms)
    {
        return id >= 0 && id < expressionPrograms.Count ? expressionPrograms[id] ?? embeddedProgram : embeddedProgram;
    }

    private static BindingTargetProgram? ResolveBindingTargetProgram(
        int id,
        BindingTargetProgram? embeddedProgram,
        StatementDiagnosticsBindingTargetProgramTable bindingTargets)
    {
        return bindingTargets.Resolve(id) ?? embeddedProgram;
    }
}
