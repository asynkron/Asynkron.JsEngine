using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Intermediate representation for generator functions. The plan contains a flat list of instructions
/// that model sequential execution, branching, and yield points. The interpreter maintains a program counter
/// and executes the instructions synchronously, allowing .next/.throw/.return to resume exactly where the generator paused.
/// </summary>
internal sealed record GeneratorPlan(
    ImmutableArray<GeneratorInstruction> Instructions,
    int EntryPoint);

internal abstract record GeneratorInstruction(int Next);

/// <summary>
/// Evaluates a statement node and then jumps to <see cref="GeneratorInstruction.Next"/>.
/// </summary>
internal sealed record StatementInstruction(int Next, StatementNode Statement) : GeneratorInstruction(Next);

/// <summary>
/// Evaluates an expression and exposes the result.
/// </summary>
internal sealed record ExpressionInstruction(int Next, ExpressionNode Expression) : GeneratorInstruction(Next);

/// <summary>
/// Represents a yield expression. When executed, the generator returns control to the caller.
/// </summary>
internal sealed record YieldInstruction(int Next, ExpressionNode? YieldExpression) : GeneratorInstruction(Next);

/// <summary>
/// Represents a delegated <c>yield*</c> expression that iterates another iterable.
/// </summary>
internal sealed record YieldStarInstruction(
    int Next,
    ExpressionNode IterableExpression,
    Symbol StateSlotSymbol,
    Symbol? ResultSlotSymbol) : GeneratorInstruction(Next);

/// <summary>
/// Represents a return statement in the generator.
/// </summary>
internal sealed record ReturnInstruction(ExpressionNode? ReturnExpression) : GeneratorInstruction(-1);

/// <summary>
/// Represents a conditional branch.
/// </summary>
internal sealed record BranchInstruction(ExpressionNode Condition, int ConsequentIndex, int AlternateIndex)
    : GeneratorInstruction(-1);

/// <summary>
/// Represents an unconditional jump to another instruction index.
/// </summary>
internal sealed record JumpInstruction(int TargetIndex) : GeneratorInstruction(TargetIndex);

/// <summary>
/// Stores the most recent <c>.next(value)</c> payload into a synthetic slot (or discards it) before execution continues.
/// </summary>
internal sealed record StoreResumeValueInstruction(int Next, Symbol? TargetSymbol) : GeneratorInstruction(Next);

/// <summary>
/// Represents a <c>break</c> statement.
/// </summary>
internal sealed record BreakInstruction(int TargetIndex) : GeneratorInstruction(TargetIndex);

/// <summary>
/// Represents a <c>continue</c> statement.
/// </summary>
internal sealed record ContinueInstruction(int TargetIndex) : GeneratorInstruction(TargetIndex);

/// <summary>
/// Marks the beginning of a <c>try</c> region.
/// </summary>
internal sealed record EnterTryInstruction(int Next, int HandlerIndex, Symbol? CatchSlotSymbol, int FinallyIndex)
    : GeneratorInstruction(Next);

/// <summary>
/// Marks normal completion of a <c>try</c> or <c>catch</c> block.
/// </summary>
internal sealed record LeaveTryInstruction(int Next) : GeneratorInstruction(Next);

/// <summary>
/// Marks the end of a <c>finally</c> block so pending completions can resume.
/// </summary>
internal sealed record EndFinallyInstruction(int Next) : GeneratorInstruction(Next);

/// <summary>
/// Initializes the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
internal sealed record IteratorInitInstruction(IteratorDriverKind Kind, ExpressionNode IterableExpression, Symbol IteratorSlot, int Next)
    : GeneratorInstruction(Next);

/// <summary>
/// Advances the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
internal sealed record IteratorMoveNextInstruction(IteratorDriverKind Kind, Symbol IteratorSlot, Symbol ValueSlot, int BreakIndex, int Next)
    : GeneratorInstruction(Next);
