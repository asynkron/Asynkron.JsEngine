using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution;

internal enum StatementDiagnosticOpcode : byte
{
    Jump = 1,
    Break = 2,
    Continue = 3,
    SetCompletionValue = 4,
    BreakableExit = 5
}

internal readonly record struct EncodedStatementInstruction(
    StatementDiagnosticOpcode Opcode,
    int NextOrTarget,
    int Operand);

/// <summary>
/// Diagnostic-only codec for a small, stable subset of statement instructions.
/// This is intentionally scoped to parity testing and does not alter runtime execution.
/// </summary>
internal static class StatementInstructionDiagnosticsCodec
{
    public static bool TryEncode(ExecutionInstruction instruction, out EncodedStatementInstruction encoded)
    {
        switch (instruction)
        {
            case JumpInstruction jump:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Jump,
                    jump.TargetIndex,
                    0);
                return true;
            case BreakInstruction @break:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Break,
                    @break.TargetIndex,
                    @break.TargetScopeId);
                return true;
            case ContinueInstruction @continue:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.Continue,
                    @continue.TargetIndex,
                    @continue.TargetScopeId);
                return true;
            case SetCompletionValueInstruction setCompletion:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.SetCompletionValue,
                    setCompletion.Next,
                    0);
                return true;
            case BreakableExitInstruction breakableExit:
                encoded = new EncodedStatementInstruction(
                    StatementDiagnosticOpcode.BreakableExit,
                    breakableExit.Next,
                    0);
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
            _ => throw new ArgumentOutOfRangeException(nameof(encoded), encoded.Opcode, "Unsupported diagnostic opcode")
        };
    }
}
