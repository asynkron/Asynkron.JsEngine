using System.Globalization;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal enum StatementDiagnosticOpcode : byte
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
    StatementDiagnosticOpcode Opcode,
    int NextOrTarget,
    int Operand,
    int Extra,
    string? SymbolName,
    ExpressionProgram? ExpressionProgram,
    ExpressionProgram? SecondaryExpressionProgram,
    Symbol? Symbol,
    Symbol? SecondarySymbol,
    BindingTargetProgram? BindingTargetProgram);

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
                    StatementDiagnosticOpcode.Jump,
                    jump.TargetIndex,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;
            case BreakInstruction @break:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Break,
                    @break.TargetIndex,
                    @break.TargetScopeId,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;
            case ContinueInstruction @continue:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Continue,
                    @continue.TargetIndex,
                    @continue.TargetScopeId,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.SetCompletionValue,
                    setCompletion.Next,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.BreakableExit,
                    breakableExit.Next,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                return true;
            case EvaluateAndDiscardInstruction evaluateAndDiscard:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.EvaluateAndDiscard,
                    evaluateAndDiscard.Next,
                    evaluateAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    null,
                    evaluateAndDiscard.ExpressionProgram,
                    null,
                    null,
                    null,
                    null);
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.AwaitAndDiscard,
                    awaitAndDiscard.Next,
                    awaitAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    null,
                    awaitAndDiscard.AwaitedProgram,
                    null,
                    awaitAndDiscard.AwaitStateKey,
                    null,
                    null);
                return true;
            case ThrowInstruction throwInstruction:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Throw,
                    -1,
                    0,
                    0,
                    null,
                    throwInstruction.ThrowProgram,
                    throwInstruction.AwaitedProgram,
                    throwInstruction.AwaitStateKey,
                    null,
                    null);
                return true;
            case ReturnInstruction returnInstruction:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Return,
                    returnInstruction.Next,
                    0,
                    0,
                    null,
                    returnInstruction.ReturnProgram,
                    returnInstruction.AwaitedProgram,
                    returnInstruction.AwaitStateKey,
                    null,
                    null);
                return true;
            case AssignmentSlotInstruction assignmentSlot:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.AssignmentSlot,
                    assignmentSlot.Next,
                    GetAssignmentSlotFlags(assignmentSlot),
                    assignmentSlot.SlotIndex,
                    string.Concat(
                        assignmentSlot.ScopeId.ToString(CultureInfo.InvariantCulture),
                        "|",
                        assignmentSlot.FlatSlotId.ToString(CultureInfo.InvariantCulture)),
                    assignmentSlot.ValueProgram,
                    assignmentSlot.AwaitedProgram,
                    assignmentSlot.AwaitStateKey,
                    assignmentSlot.TargetSymbol,
                    null);
                return true;
            case SimpleVariableDeclarationInstruction simpleVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.SimpleVariableDeclaration,
                    simpleVariableDeclaration.Next,
                    (int)simpleVariableDeclaration.VarKind,
                    GetSimpleVariableDeclarationFlags(simpleVariableDeclaration),
                    null,
                    simpleVariableDeclaration.InitializerProgram,
                    simpleVariableDeclaration.AwaitedProgram,
                    simpleVariableDeclaration.AwaitStateKey,
                    simpleVariableDeclaration.TargetSymbol,
                    null);
                return true;
            case BindingVariableDeclarationInstruction bindingVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.BindingVariableDeclaration,
                    bindingVariableDeclaration.Next,
                    (int)bindingVariableDeclaration.VarKind,
                    0,
                    null,
                    bindingVariableDeclaration.InitializerProgram,
                    bindingVariableDeclaration.AwaitedProgram,
                    bindingVariableDeclaration.AwaitStateKey,
                    null,
                    bindingVariableDeclaration.TargetProgram);
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
            StatementDiagnosticOpcode.Jump => new JumpInstruction(encoded.NextOrTarget),
            StatementDiagnosticOpcode.Break => new BreakInstruction(encoded.NextOrTarget, encoded.Operand),
            StatementDiagnosticOpcode.Continue => new ContinueInstruction(encoded.NextOrTarget, encoded.Operand),
            StatementDiagnosticOpcode.SetCompletionValue => new SetCompletionValueInstruction(encoded.NextOrTarget),
            StatementDiagnosticOpcode.BreakableExit => new BreakableExitInstruction(encoded.NextOrTarget),
            StatementDiagnosticOpcode.EvaluateAndDiscard => new EvaluateAndDiscardInstruction(
                encoded.NextOrTarget,
                encoded.ExpressionProgram ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            StatementDiagnosticOpcode.AwaitAndDiscard => new AwaitAndDiscardInstruction(
                encoded.NextOrTarget,
                encoded.Symbol ?? Symbol.Intern(encoded.SymbolName ?? "__await_state"),
                encoded.ExpressionProgram ?? ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            StatementDiagnosticOpcode.Throw => new ThrowInstruction(
                encoded.ExpressionProgram,
                encoded.Symbol,
                encoded.SecondaryExpressionProgram),
            StatementDiagnosticOpcode.Return => new ReturnInstruction(
                encoded.NextOrTarget,
                encoded.ExpressionProgram,
                encoded.Symbol,
                encoded.SecondaryExpressionProgram),
            StatementDiagnosticOpcode.AssignmentSlot => new AssignmentSlotInstruction(
                encoded.NextOrTarget,
                encoded.SecondarySymbol ?? Symbol.Intern(encoded.SymbolName ?? "__assignment_target"),
                ValueProgram: encoded.ExpressionProgram,
                AwaitStateKey: encoded.Symbol,
                AwaitedProgram: encoded.SecondaryExpressionProgram,
                SuppressCompletionValue: (encoded.Operand & AssignmentSlotSuppressCompletionBit) != 0,
                AllowNameInference: (encoded.Operand & AssignmentSlotAllowNameInferenceBit) != 0,
                ScopeId: ParseDelimitedIntOrDefault(encoded.SymbolName, 0, -1),
                SlotIndex: encoded.Extra,
                FlatSlotId: ParseDelimitedIntOrDefault(encoded.SymbolName, 1, -1)),
            StatementDiagnosticOpcode.SimpleVariableDeclaration => new SimpleVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.SecondarySymbol ?? Symbol.Intern(encoded.SymbolName ?? "__declaration_target"),
                InitializerProgram: encoded.ExpressionProgram,
                AwaitStateKey: encoded.Symbol,
                AwaitedProgram: encoded.SecondaryExpressionProgram,
                AllowNameInference: (encoded.Extra & SimpleVariableAllowNameInferenceBit) != 0,
                IsScriptLevel: (encoded.Extra & SimpleVariableIsScriptLevelBit) != 0),
            StatementDiagnosticOpcode.BindingVariableDeclaration => new BindingVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                encoded.BindingTargetProgram ?? new IdentifierBindingTargetProgram(Symbol.Intern("__binding_target")),
                InitializerProgram: encoded.ExpressionProgram,
                AwaitStateKey: encoded.Symbol,
                AwaitedProgram: encoded.SecondaryExpressionProgram),
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

    private static int ParseDelimitedIntOrDefault(string? value, int partIndex, int defaultValue)
    {
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        var parts = value.Split('|');
        if (partIndex < 0 || partIndex >= parts.Length)
        {
            return defaultValue;
        }

        return int.TryParse(parts[partIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }
}
