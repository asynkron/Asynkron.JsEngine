#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Evaluates a statement node and then jumps to <see cref="ExecutionInstruction.Next" />.
/// </summary>
internal sealed record StatementInstruction(int Next, StatementNode Statement)
    : ExecutionInstruction(InstructionKind.Statement, Next);

/// <summary>
///     Represents a throw statement in the generator.
///     Evaluates the expression and throws it as an exception.
/// </summary>
internal sealed record ThrowInstruction(ExpressionNode Expression)
    : ExecutionInstruction(InstructionKind.Throw, -1);

/// <summary>
///     Represents an expression statement in the generator.
///     Evaluates the expression and discards the result.
/// </summary>
internal sealed record EvaluateAndDiscardInstruction(int Next, ExpressionNode Expression)
    : ExecutionInstruction(InstructionKind.EvaluateAndDiscard, Next);

/// <summary>
///     Evaluates a binary operation directly without going through the generic
///     BinaryExpression AST evaluator. Stores the result in the specified slot
///     (if provided) or discards it.
/// </summary>
/// <remarks>
///     This instruction provides a fast path for common arithmetic and comparison
///     operations in generators/async functions by avoiding the AST dispatch overhead.
///     Only non-short-circuiting operators are supported (logical operators still
///     need BranchInstruction for correct semantics).
/// </remarks>
internal sealed record BinaryOpInstruction(
    int Next,
    BinaryOperator Operator,
    ExpressionNode Left,
    ExpressionNode Right,
    Symbol? ResultSlot = null)
    : ExecutionInstruction(InstructionKind.BinaryOp, Next);

/// <summary>
///     Increments or decrements a variable stored in a slot directly, without
///     going through the generic UnaryExpression AST evaluator.
/// </summary>
/// <remarks>
///     This instruction provides a fast path for <c>i++</c>, <c>++i</c>, <c>i--</c>,
///     and <c>--i</c> operations on simple identifiers in generators/async functions.
///     For the common case of loop counters, this avoids identifier lookup and
///     ToNumber conversion overhead when the value is already a number.
/// </remarks>
internal sealed record IncrementSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    bool IsIncrement,
    bool IsPrefix)
    : ExecutionInstruction(InstructionKind.IncrementSlot, Next);

/// <summary>
///     Represents a function declaration in the generator.
///     Function declarations are hoisted, so this instruction is a no-op at runtime
///     that simply advances to the next instruction.
/// </summary>
internal sealed record FunctionDeclarationInstruction(int Next)
    : ExecutionInstruction(InstructionKind.FunctionDeclaration, Next);

/// <summary>
///     Evaluates a class declaration and binds the class constructor to the class name.
///     This instruction is used for class declarations that don't contain yields in
///     computed property names or extends clause.
/// </summary>
internal sealed record ClassDeclarationInstruction(int Next, ClassDeclaration Declaration)
    : ExecutionInstruction(InstructionKind.ClassDeclaration, Next);

/// <summary>
///     Represents a simple variable declaration with an identifier binding (no destructuring).
///     Handles declarations like: let x = expr; const y = 5; var z = value;
/// </summary>
/// <param name="IsScriptLevel">
///     When true, indicates this is a top-level script var declaration.
///     Script-level var declarations must update the global object (via AssignJsValue),
///     while function-level var declarations only update local bindings (via DefineOrAssignJsValue).
/// </param>
internal sealed record SimpleVariableDeclarationInstruction(
    int Next,
    VariableKind VarKind,
    Symbol TargetSymbol,
    ExpressionNode? Initializer,
    bool IsScriptLevel = false)
    : ExecutionInstruction(InstructionKind.SimpleVariableDeclaration, Next);

/// <summary>
///     Pushes a new environment onto the scope stack.
///     Used for block scopes, loop iterations, and other lexical scopes.
///     For loop iterations, this ensures closures capture separate values per iteration.
/// </summary>
/// <param name="Next">Next instruction index.</param>
/// <param name="PerIterationBindings">
///     For loop iterations: symbols that need copying from previous iteration.
///     Empty for regular block scopes.
/// </param>
/// <param name="ScopeId">The scope ID for this environment.</param>
/// <param name="SlotCount">Number of slots in the environment.</param>
/// <param name="SlotMap">Mapping from symbols to slot indices.</param>
/// <param name="AllowPooling">Whether environment pooling is allowed (no closures capture this env).</param>
internal sealed record PushEnvironmentInstruction(
    int Next,
    ImmutableArray<Symbol> PerIterationBindings,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap,
    bool AllowPooling = false)
    : ExecutionInstruction(InstructionKind.PushEnvironment, Next);

/// <summary>
///     Pops an environment from the scope stack.
///     If the current environment's ScopeId matches, sets environment = environment.Enclosing.
///     If ScopeId doesn't match (scope was never entered, e.g., loop ran 0 times), this is a no-op.
/// </summary>
/// <param name="ScopeId">The scope ID to pop. Only pops if current env matches.</param>
/// <param name="AllowPooling">Whether to return the popped environment to pool.</param>
/// <param name="Next">Next instruction index.</param>
internal sealed record PopEnvironmentInstruction(int ScopeId, bool AllowPooling, int Next)
    : ExecutionInstruction(InstructionKind.PopEnvironment, Next);

/// <summary>
///     Represents a yield expression. When executed, the generator returns control to the caller.
/// </summary>
internal sealed record YieldInstruction(int Next, ExpressionNode? YieldExpression)
    : ExecutionInstruction(InstructionKind.Yield, Next);

/// <summary>
///     Represents a delegated <c>yield*</c> expression that iterates another iterable.
/// </summary>
internal sealed record YieldStarInstruction(
    int Next,
    ExpressionNode IterableExpression,
    Symbol StateSlotSymbol,
    Symbol? ResultSlotSymbol)
    : ExecutionInstruction(InstructionKind.YieldStar, Next);

/// <summary>
///     Stores the most recent <c>.next(value)</c> payload into a synthetic slot (or discards it) before execution
///     continues.
/// </summary>
internal sealed record StoreResumeValueInstruction(int Next, Symbol? TargetSymbol)
    : ExecutionInstruction(InstructionKind.StoreResumeValue, Next);

/// <summary>
///     Marks the beginning of a <c>try</c> region.
/// </summary>
internal sealed record EnterTryInstruction(int Next, int HandlerIndex, Symbol? CatchSlotSymbol, int FinallyIndex)
    : ExecutionInstruction(InstructionKind.EnterTry, Next);

/// <summary>
///     Marks normal completion of a <c>try</c> or <c>catch</c> block.
/// </summary>
internal sealed record LeaveTryInstruction(int Next)
    : ExecutionInstruction(InstructionKind.LeaveTry, Next);

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
    int ContinueTarget)
    : ExecutionInstruction(InstructionKind.LoopEnter, Next);

/// <summary>
///     Marks exit from a loop. Pops the loop context from the loop stack.
/// </summary>
/// <param name="Next">The next instruction index after the loop.</param>
internal sealed record LoopExitInstruction(int Next)
    : ExecutionInstruction(InstructionKind.LoopExit, Next);

/// <summary>
///     Marks the end of a <c>finally</c> block so pending completions can resume.
/// </summary>
internal sealed record EndFinallyInstruction(int Next)
    : ExecutionInstruction(InstructionKind.EndFinally, Next);

/// <summary>
///     Initializes the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
/// <param name="IteratorKind">Whether this is a sync or async iterator.</param>
/// <param name="IterableExpression">Expression that produces the iterable.</param>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="Next">Jump target after initialization.</param>
/// <param name="TdzBindings">
///     Symbols that need TDZ bindings during iterable evaluation (for let/const declarations).
///     When non-empty, a TDZ environment is created before evaluating the iterable expression.
///     This ensures `for (const x of [x])` throws ReferenceError for accessing x before initialization.
/// </param>
/// <param name="TdzIsConst">Whether the TDZ bindings are const (true) or let (false).</param>
internal sealed record IteratorInitInstruction(
    IteratorDriverKind IteratorKind,
    ExpressionNode IterableExpression,
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next,
    ImmutableArray<Symbol> TdzBindings = default,
    bool TdzIsConst = false)
    : ExecutionInstruction(InstructionKind.IteratorInit, Next);

/// <summary>
///     Advances the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
/// <param name="IteratorKind">Whether this is a sync or async iterator.</param>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="ValueSlot">Symbol for the current iteration value.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="ValueSlotIndex">Pre-resolved slot index for fast value access (-1 if not resolved).</param>
/// <param name="BreakIndex">Jump target when iteration completes.</param>
/// <param name="Next">Jump target for the loop body.</param>
internal sealed record IteratorMoveNextInstruction(
    IteratorDriverKind IteratorKind,
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int IteratorSlotIndex,
    int ValueSlotIndex,
    int BreakIndex,
    int Next)
    : ExecutionInstruction(InstructionKind.IteratorMoveNext, Next);

/// <summary>
///     Closes an iterator stored in the given slot. Used in finally blocks for for-of loops.
/// </summary>
internal sealed record IteratorCloseInstruction(Symbol IteratorSlot, int Next)
    : ExecutionInstruction(InstructionKind.IteratorClose, Next);

/// <summary>
///     Represents an unconditional jump to another instruction index.
/// </summary>
internal sealed record JumpInstruction(int TargetIndex)
    : ExecutionInstruction(InstructionKind.Jump, TargetIndex);

/// <summary>
///     Represents a conditional branch.
/// </summary>
internal sealed record BranchInstruction(ExpressionNode Condition, int ConsequentIndex, int AlternateIndex)
    : ExecutionInstruction(InstructionKind.Branch, -1);

/// <summary>
///     Represents a <c>break</c> statement.
///     Pops environments until reaching TargetScopeId before jumping.
/// </summary>
/// <param name="TargetIndex">The instruction index to jump to.</param>
/// <param name="TargetScopeId">The scope ID to pop to before jumping.</param>
internal sealed record BreakInstruction(int TargetIndex, int TargetScopeId = -1)
    : ExecutionInstruction(InstructionKind.Break, TargetIndex);

/// <summary>
///     Represents a <c>continue</c> statement.
///     Pops environments until reaching TargetScopeId before jumping.
/// </summary>
/// <param name="TargetIndex">The instruction index to jump to.</param>
/// <param name="TargetScopeId">The scope ID to pop to before jumping.</param>
internal sealed record ContinueInstruction(int TargetIndex, int TargetScopeId = -1)
    : ExecutionInstruction(InstructionKind.Continue, TargetIndex);

/// <summary>
///     Represents a return statement in the generator.
/// </summary>
/// <remarks>
///     The Next parameter is important for returns inside try/finally blocks.
///     When a return occurs inside a finally block, we need to continue to
///     EndFinallyInstruction to properly process the pending completion.
/// </remarks>
internal sealed record ReturnInstruction(int Next, ExpressionNode? ReturnExpression)
    : ExecutionInstruction(InstructionKind.Return, Next);

/// <summary>
///     Marks the beginning of a <c>with</c> statement. Evaluates the object expression
///     and pushes a with-environment onto the scope chain.
/// </summary>
internal sealed record EnterWithInstruction(ExpressionNode ObjectExpression, Symbol WithScopeSlot, int Next)
    : ExecutionInstruction(InstructionKind.EnterWith, Next);

/// <summary>
///     Marks the end of a <c>with</c> statement. Pops the with-environment from the scope chain.
/// </summary>
internal sealed record LeaveWithInstruction(Symbol WithScopeSlot, int Next)
    : ExecutionInstruction(InstructionKind.LeaveWith, Next);

/// <summary>
///     Evaluates an expression and exposes the result.
/// </summary>
internal sealed record ExpressionInstruction(int Next, ExpressionNode Expression)
    : ExecutionInstruction(InstructionKind.Expression, Next);
