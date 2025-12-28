using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
///     Marks entry into a loop. Pushes loop context onto the loop stack so that
///     break/continue statements from AST-evaluated code (via StatementInstruction)
///     can resolve their jump targets.
/// </summary>
/// <param name="Next">The next instruction index (loop body entry).</param>
/// <param name="Label">The loop label (null for unlabeled loops).</param>
/// <param name="BreakTarget">The instruction index to jump to for break.</param>
/// <param name="ContinueTarget">The instruction index to jump to for continue.</param>
internal sealed record LoopEnterInstruction(
    int Next,
    Symbol? Label,
    int BreakTarget,
    int ContinueTarget) : ExecutionInstruction(Next);
