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
    string? SymbolName);

/// <summary>
/// Diagnostic-only codec for a small, stable subset of statement instructions.
/// This is intentionally scoped to parity testing and does not alter runtime execution.
/// </summary>
internal static class StatementInstructionDiagnosticsCodec
{
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
                    null);
                return true;
            case BreakInstruction @break:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Break,
                    @break.TargetIndex,
                    @break.TargetScopeId,
                    0,
                    null);
                return true;
            case ContinueInstruction @continue:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Continue,
                    @continue.TargetIndex,
                    @continue.TargetScopeId,
                    0,
                    null);
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.SetCompletionValue,
                    setCompletion.Next,
                    0,
                    0,
                    null);
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.BreakableExit,
                    breakableExit.Next,
                    0,
                    0,
                    null);
                return true;
            case EvaluateAndDiscardInstruction evaluateAndDiscard:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.EvaluateAndDiscard,
                    evaluateAndDiscard.Next,
                    evaluateAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    null);
                return true;
            case AwaitAndDiscardInstruction awaitAndDiscard:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.AwaitAndDiscard,
                    awaitAndDiscard.Next,
                    awaitAndDiscard.SuppressCompletionValue ? 1 : 0,
                    0,
                    awaitAndDiscard.AwaitStateKey.Name);
                return true;
            case ThrowInstruction throwInstruction:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Throw,
                    -1,
                    throwInstruction.ThrowProgram is null ? 0 : 1,
                    throwInstruction.AwaitedProgram is null ? 0 : 1,
                    throwInstruction.AwaitStateKey?.Name);
                return true;
            case ReturnInstruction returnInstruction:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Return,
                    returnInstruction.Next,
                    returnInstruction.ReturnProgram is null ? 0 : 1,
                    returnInstruction.AwaitedProgram is null ? 0 : 1,
                    returnInstruction.AwaitStateKey?.Name);
                return true;
            case AssignmentSlotInstruction assignmentSlot:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.AssignmentSlot,
                    assignmentSlot.Next,
                    assignmentSlot.SuppressCompletionValue ? 1 : 0,
                    assignmentSlot.AllowNameInference ? 1 : 0,
                    assignmentSlot.TargetSymbol.Name);
                return true;
            case SimpleVariableDeclarationInstruction simpleVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.SimpleVariableDeclaration,
                    simpleVariableDeclaration.Next,
                    (int)simpleVariableDeclaration.VarKind,
                    simpleVariableDeclaration.IsScriptLevel ? 1 : 0,
                    simpleVariableDeclaration.TargetSymbol.Name);
                return true;
            case BindingVariableDeclarationInstruction bindingVariableDeclaration:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.BindingVariableDeclaration,
                    bindingVariableDeclaration.Next,
                    (int)bindingVariableDeclaration.VarKind,
                    bindingVariableDeclaration.InitializerProgram is null ? 0 : 1,
                    null);
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
                ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            StatementDiagnosticOpcode.AwaitAndDiscard => new AwaitAndDiscardInstruction(
                encoded.NextOrTarget,
                Symbol.Intern(encoded.SymbolName ?? "__await_state"),
                ExpressionProgram.Empty,
                SuppressCompletionValue: encoded.Operand != 0),
            StatementDiagnosticOpcode.Throw => new ThrowInstruction(
                encoded.Operand != 0 ? ExpressionProgram.Empty : null,
                encoded.SymbolName is null ? null : Symbol.Intern(encoded.SymbolName),
                encoded.Extra != 0 ? ExpressionProgram.Empty : null),
            StatementDiagnosticOpcode.Return => new ReturnInstruction(
                encoded.NextOrTarget,
                encoded.Operand != 0 ? ExpressionProgram.Empty : null,
                encoded.SymbolName is null ? null : Symbol.Intern(encoded.SymbolName),
                encoded.Extra != 0 ? ExpressionProgram.Empty : null),
            StatementDiagnosticOpcode.AssignmentSlot => new AssignmentSlotInstruction(
                encoded.NextOrTarget,
                Symbol.Intern(encoded.SymbolName ?? "__assignment_target"),
                ValueProgram: null,
                AwaitStateKey: null,
                AwaitedProgram: null,
                SuppressCompletionValue: encoded.Operand != 0,
                AllowNameInference: encoded.Extra != 0),
            StatementDiagnosticOpcode.SimpleVariableDeclaration => new SimpleVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                Symbol.Intern(encoded.SymbolName ?? "__declaration_target"),
                InitializerProgram: null,
                AwaitStateKey: null,
                AwaitedProgram: null,
                AllowNameInference: false,
                IsScriptLevel: encoded.Extra != 0),
            StatementDiagnosticOpcode.BindingVariableDeclaration => new BindingVariableDeclarationInstruction(
                encoded.NextOrTarget,
                (VariableKind)encoded.Operand,
                new IdentifierBindingTargetProgram(Symbol.Intern("__binding_target")),
                InitializerProgram: encoded.Extra != 0 ? ExpressionProgram.Empty : null,
                AwaitStateKey: null,
                AwaitedProgram: null),
            _ => throw new ArgumentOutOfRangeException(nameof(encoded), encoded.Opcode, "Unsupported diagnostic opcode")
        };
    }
}
