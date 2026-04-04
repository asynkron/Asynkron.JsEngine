#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Represents a throw statement in the generator.
///     Evaluates the expression and throws it as an exception.
/// </summary>
/// <param name="ThrowProgram">Lowered bytecode representation of the expression to throw.</param>
internal sealed record ThrowInstruction(
    ExpressionProgram? ThrowProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null)
    : ExecutionInstruction(InstructionKind.Throw, -1)
{
    // The AST payload has been removed; this stays null so AST-leak assertions remain explicit.
    public ExpressionNode? Expression => null;
}

/// <summary>
///     Represents an expression statement in the generator.
///     Evaluates the expression and discards the result.
/// </summary>
/// <param name="Next">Next instruction index.</param>
/// <param name="Expression">AST representation of the expression (nullable during migration).</param>
/// <param name="ExpressionProgram">Bytecode representation of the expression (nullable during migration).</param>
/// <param name="SuppressCompletionValue">
///     When true, the completion value is NOT updated (used for loop update expressions).
///     Per ES spec, for-loop update expressions don't contribute to the loop's completion value.
/// </param>
internal sealed record EvaluateAndDiscardInstruction(
    int Next,
    ExpressionProgram ExpressionProgram,
    bool SuppressCompletionValue = false)
    : ExecutionInstruction(InstructionKind.EvaluateAndDiscard, Next)
{
    public ExpressionNode? Expression => null;
}

/// <summary>
///     Evaluates an await expression statement and discards the result.
/// </summary>
internal sealed record AwaitAndDiscardInstruction(
    int Next,
    Symbol AwaitStateKey,
    ExpressionProgram AwaitedProgram,
    bool SuppressCompletionValue = false)
    : ExecutionInstruction(InstructionKind.AwaitAndDiscard, Next);

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
/// <param name="SuppressCompletionValue">
///     When true, the completion value is NOT updated (used for loop update expressions).
///     Per ES spec, for-loop update expressions don't contribute to the loop's completion value.
/// </param>
/// <param name="ScopeId">Scope ID where the variable is declared. -1 means unresolved.</param>
/// <param name="SlotIndex">Slot index within the scope. -1 means unresolved.</param>
/// <param name="FlatSlotId">Index into flat slots array for O(1) access. -1 means unresolved.</param>
internal sealed record IncrementSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    bool IsIncrement,
    bool IsPrefix,
    bool SuppressCompletionValue = false,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1)
    : ExecutionInstruction(InstructionKind.IncrementSlot, Next);

/// <summary>
///     Performs a direct assignment to an identifier binding (e.g., x = expr) without
///     routing through the generic AssignmentExpression AST evaluator.
/// </summary>
internal sealed record AssignmentSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    ExpressionProgram? ValueProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    bool SuppressCompletionValue = false,
    bool AllowNameInference = true,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1)
    : ExecutionInstruction(InstructionKind.AssignmentSlot, Next)
{
    public ExpressionNode? ValueExpression => null;
}

/// <summary>
///     Performs a logical compound assignment on an identifier slot (e.g., x ||= y).
///     The runner preserves short-circuit semantics and only evaluates the RHS when needed.
/// </summary>
internal sealed record LogicalCompoundAssignmentSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    BinaryOperator Operator,
    ExpressionProgram? RhsProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    bool SuppressCompletionValue = false,
    bool AllowNameInference = true,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1)
    : ExecutionInstruction(InstructionKind.LogicalCompoundAssignmentSlot, Next)
{
    public ExpressionNode? RhsExpression => null;
}

/// <summary>
///     Performs a compound assignment operation directly on an identifier slot (e.g., s += i).
///     This avoids going through the generic AssignmentExpression AST evaluator.
/// </summary>
/// <remarks>
///     This instruction provides a fast path for compound assignments like <c>s += i</c>,
///     <c>s -= value</c>, <c>s *= expr</c>, etc. on simple identifiers. It reads the
///     current value from the target slot, applies the binary operation with the RHS,
///     and writes the result back to the slot.
/// </remarks>
/// <param name="Next">Next instruction index.</param>
/// <param name="TargetSymbol">The target identifier symbol.</param>
/// <param name="Operator">The compound assignment operator (Add, Subtract, etc.).</param>
/// <param name="RhsProgram">Bytecode representation of the right-hand side expression.</param>
/// <param name="SuppressCompletionValue">When true, the completion value is NOT updated.</param>
/// <param name="ScopeId">Scope ID where the variable is declared. -1 means unresolved.</param>
/// <param name="SlotIndex">Slot index within the scope. -1 means unresolved.</param>
/// <param name="FlatSlotId">Index into flat slots array for O(1) access. -1 means unresolved.</param>
/// <param name="RhsFlatSlotId">Flat slot ID for RHS when it's an identifier. -1 means not applicable.</param>
internal sealed record CompoundAssignmentSlotInstruction(
    int Next,
    Symbol TargetSymbol,
    BinaryOperator Operator,
    ExpressionProgram? RhsProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    bool SuppressCompletionValue = false,
    int ScopeId = -1,
    int SlotIndex = -1,
    int FlatSlotId = -1,
    int RhsFlatSlotId = -1)
    : ExecutionInstruction(InstructionKind.CompoundAssignmentSlot, Next)
{
    public ExpressionNode? RhsExpression => null;
}

internal readonly record struct FunctionDeclarationDescriptor(Symbol Name, FunctionExpression Function);

/// <summary>
///     Represents a function declaration in the generator.
///     For block-scoped function declarations (strict mode), the function is created
///     and bound to the environment at runtime. For function-scoped declarations,
///     this is a no-op as they are hoisted.
/// </summary>
internal sealed record FunctionDeclarationInstruction(int Next, FunctionDeclarationDescriptor? Descriptor = null)
    : ExecutionInstruction(InstructionKind.FunctionDeclaration, Next);

/// <summary>
///     Evaluates a class declaration and binds the class constructor to the class name.
///     This instruction is used for class declarations that don't contain yields in
///     computed property names or extends clause.
/// </summary>
internal readonly record struct ClassDeclarationDescriptor(Symbol Name, ClassDefinition Definition);

internal sealed record ClassDeclarationInstruction(int Next, ClassDeclarationDescriptor Descriptor)
    : ExecutionInstruction(InstructionKind.ClassDeclaration, Next);

/// <summary>
///     Represents a simple variable declaration with an identifier binding (no destructuring).
///     Handles declarations like: let x = expr; const y = 5; var z = value;
/// </summary>
/// <param name="Next">Next instruction index.</param>
/// <param name="VarKind">The variable declaration kind (var/let/const).</param>
/// <param name="TargetSymbol">The identifier being declared.</param>
/// <param name="InitializerProgram">Lowered bytecode representation of the initializer expression.</param>
/// <param name="IsScriptLevel">
///     When true, indicates this is a top-level script var declaration.
///     Script-level var declarations must update the global object (via AssignJsValue),
///     while function-level var declarations only update local bindings (via DefineOrAssignJsValue).
/// </param>
internal sealed record SimpleVariableDeclarationInstruction(
    int Next,
    VariableKind VarKind,
    Symbol TargetSymbol,
    ExpressionProgram? InitializerProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    bool AllowNameInference = false,
    bool IsScriptLevel = false)
    : ExecutionInstruction(InstructionKind.SimpleVariableDeclaration, Next)
{
    public ExpressionNode? Initializer => null;
}

internal sealed record SuspendingSimpleVariableDeclarationInstruction(
    int Next,
    VariableKind VarKind,
    Symbol TargetSymbol,
    ExpressionNode Initializer,
    bool AllowNameInference = false,
    bool IsScriptLevel = false)
    : ExecutionInstruction(InstructionKind.SuspendingSimpleVariableDeclaration, Next)
{
    public ExpressionProgram? InitializerProgram => null;
}

/// <summary>
///     Represents a single variable declarator that uses a binding target.
///     Handles declarations like: let {a, b} = obj; const [x, y] = arr; var {x = 1} = source;
/// </summary>
internal sealed record BindingVariableDeclarationInstruction(
    int Next,
    VariableKind VarKind,
    BindingTargetProgram TargetProgram,
    ExpressionProgram? InitializerProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null)
    : ExecutionInstruction(InstructionKind.BindingVariableDeclaration, Next)
{
    public ExpressionNode? Initializer => null;
}

internal sealed record SuspendingBindingVariableDeclarationInstruction(
    int Next,
    VariableKind VarKind,
    BindingTargetProgram TargetProgram,
    ExpressionNode Initializer)
    : ExecutionInstruction(InstructionKind.SuspendingBindingVariableDeclaration, Next)
{
    public ExpressionProgram? InitializerProgram => null;
}

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
/// <param name="SlotNames">Pre-computed array of (Symbol, SlotIndex) for fast slot name initialization.
///     When non-empty, this is used instead of iterating SlotMap (which is slow for ImmutableDictionary).</param>
internal sealed record PushEnvironmentInstruction(
    int Next,
    ImmutableArray<Symbol> PerIterationBindings,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap,
    bool AllowPooling = false,
    ImmutableHashSet<Symbol>? LexicalBindings = null,
    ImmutableArray<(int SlotIndex, int FlatSlotId)> FlatSlotMappings = default,
    ImmutableArray<(Symbol Name, int SlotIndex)> SlotNames = default,
    BlockStatement? SourceBlock = null)
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
/// <param name="Next">Next instruction index after the yield.</param>
/// <param name="YieldProgram">Lowered bytecode representation of the yielded expression.</param>
internal sealed record YieldInstruction(
    int Next,
    ExpressionProgram? YieldProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null)
    : ExecutionInstruction(InstructionKind.Yield, Next);

/// <summary>
///     Represents a yield expression whose operand still requires AST evaluation because it contains nested
///     suspension points.
/// </summary>
internal sealed record SuspendingYieldInstruction(
    int Next,
    ExpressionNode YieldExpression)
    : ExecutionInstruction(InstructionKind.SuspendingYield, Next);

/// <summary>
///     Represents a delegated <c>yield*</c> expression that iterates another iterable.
/// </summary>
/// <param name="Next">Next instruction index.</param>
/// <param name="IterableProgram">Lowered bytecode representation of the iterable expression.</param>
/// <param name="StateSlotSymbol">Symbol for tracking the yield* state.</param>
/// <param name="ResultSlotSymbol">Symbol for storing the result, or null.</param>
internal sealed record YieldStarInstruction(
    int Next,
    ExpressionProgram? IterableProgram = null,
    Symbol? StateSlotSymbol = null,
    Symbol? ResultSlotSymbol = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null)
    : ExecutionInstruction(InstructionKind.YieldStar, Next);

/// <summary>
///     Represents a delegated <c>yield*</c> whose iterable still requires AST evaluation because it contains
///     nested suspension points.
/// </summary>
internal sealed record SuspendingYieldStarInstruction(
    int Next,
    ExpressionNode IterableExpression,
    Symbol StateSlotSymbol,
    Symbol? ResultSlotSymbol = null)
    : ExecutionInstruction(InstructionKind.SuspendingYieldStar, Next);

/// <summary>
///     Stores the most recent <c>.next(value)</c> payload into a synthetic slot (or discards it) before execution
///     continues.
/// </summary>
internal sealed record StoreResumeValueInstruction(int Next, Symbol? TargetSymbol)
    : ExecutionInstruction(InstructionKind.StoreResumeValue, Next);

/// <summary>
///     Marks the beginning of a <c>try</c> region.
/// </summary>
/// <param name="Next">Next instruction index (try body entry).</param>
/// <param name="HandlerIndex">Index of catch handler, or -1 if no catch.</param>
/// <param name="CatchSlotSymbol">Symbol for catch parameter binding (legacy).</param>
/// <param name="FinallyIndex">Index of finally block entry, or -1 if no finally.</param>
/// <param name="EndFinallyIndex">Index of EndFinally instruction, or -1 if no finally.</param>
/// <param name="LoopContinueTarget">For for-of loops: the continue target index. Continue to this target skips the finally.</param>
/// <param name="LoopBreakTarget">For for-of loops: the break target index (BreakableExit). Break to this target triggers the finally.</param>
internal sealed record EnterTryInstruction(
    int Next,
    int HandlerIndex,
    Symbol? CatchSlotSymbol,
    int FinallyIndex,
    int EndFinallyIndex = -1,
    int LoopContinueTarget = -1,
    int LoopBreakTarget = -1)
    : ExecutionInstruction(InstructionKind.EnterTry, Next);

/// <summary>
///     Marks the beginning of a <c>catch</c> block.
///     Creates a new lexical environment and binds the catch parameter to the thrown value.
/// </summary>
/// <param name="Next">Next instruction index (catch body entry).</param>
/// <param name="CatchParameterSymbol">The catch parameter symbol (e.g., 'e' in catch(e)). Null for ES2019 optional catch binding.</param>
/// <param name="ScopeId">The scope ID for this catch environment.</param>
/// <param name="SlotCount">Number of slots in the catch environment.</param>
/// <param name="SlotMap">Mapping from symbols to slot indices.</param>
internal sealed record EnterCatchInstruction(
    int Next,
    Symbol? CatchParameterSymbol,
    int ScopeId,
    int SlotCount,
    ImmutableDictionary<Symbol, int> SlotMap)
    : ExecutionInstruction(InstructionKind.EnterCatch, Next);

/// <summary>
///     Marks normal completion of a <c>try</c> or <c>catch</c> block.
/// </summary>
internal sealed record LeaveTryInstruction(int Next)
    : ExecutionInstruction(InstructionKind.LeaveTry, Next);

/// <summary>
///     Marks entry into a breakable construct (loop or switch).
///     Pushes context onto the breakable stack so that break/continue statements
///     from AST-evaluated code (via StatementInstruction) can resolve their jump targets.
/// </summary>
/// <param name="Next">The next instruction index (body entry).</param>
/// <param name="Label">The label (null for unlabeled constructs).</param>
/// <param name="BreakTarget">The instruction index to jump to for break.</param>
/// <param name="ContinueTarget">The instruction index to jump to for continue (-1 for switch).</param>
/// <param name="ConstructKind">How this construct handles completion values.</param>
internal sealed record BreakableEnterInstruction(
    int Next,
    Symbol? Label,
    int BreakTarget,
    int ContinueTarget,
    BreakableKind ConstructKind = BreakableKind.ResetsCompletionValue)
    : ExecutionInstruction(InstructionKind.BreakableEnter, Next);

/// <summary>
///     Marks exit from a breakable construct. Pops the context from the breakable stack.
/// </summary>
/// <param name="Next">The next instruction index after the construct.</param>
internal sealed record BreakableExitInstruction(int Next)
    : ExecutionInstruction(InstructionKind.BreakableExit, Next);

/// <summary>
///     Marks the end of a <c>finally</c> block so pending completions can resume.
/// </summary>
internal sealed record EndFinallyInstruction(int Next)
    : ExecutionInstruction(InstructionKind.EndFinally, Next);

/// <summary>
///     Initializes the iterator for a <c>for...of</c> or <c>for await...of</c> loop.
/// </summary>
/// <param name="IteratorKind">Whether this is a sync or async iterator.</param>
/// <param name="IterableProgram">Bytecode representation of the iterable expression.</param>
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
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next,
    ExpressionProgram? IterableProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    ImmutableArray<Symbol> TdzBindings = default,
    bool TdzIsConst = false,
    SourceReference? IterableSource = null)
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
/// <param name="ConditionProgram">Lowered bytecode representation of the condition expression.</param>
/// <param name="ConsequentIndex">Jump target when condition is true.</param>
/// <param name="AlternateIndex">Jump target when condition is false.</param>
internal sealed record BranchInstruction(
    int ConsequentIndex,
    int AlternateIndex,
    ExpressionProgram ConditionProgram)
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
/// <param name="Next">
///     Next instruction index. Important for returns inside try/finally blocks.
///     When a return occurs inside a finally block, we need to continue to
///     EndFinallyInstruction to properly process the pending completion.
/// </param>
/// <param name="ReturnProgram">Lowered bytecode representation of the return expression.</param>
/// <remarks>
///     The Next parameter is important for returns inside try/finally blocks.
///     When a return occurs inside a finally block, we need to continue to
///     EndFinallyInstruction to properly process the pending completion.
/// </remarks>
internal sealed record ReturnInstruction(
    int Next,
    ExpressionProgram? ReturnProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null)
    : ExecutionInstruction(InstructionKind.Return, Next)
{
    // The AST payload has been removed; this stays null so AST-leak assertions remain explicit.
    public ExpressionNode? ReturnExpression => null;
}

/// <summary>
///     Marks the beginning of a <c>with</c> statement. Evaluates the object expression
///     and pushes a with-environment onto the scope chain.
/// </summary>
/// <param name="ObjectProgram">Lowered bytecode representation of the object expression.</param>
/// <param name="WithScopeSlot">Symbol for storing the with scope.</param>
/// <param name="Next">Next instruction index.</param>
internal sealed record EnterWithInstruction(
    Symbol WithScopeSlot,
    int Next,
    ExpressionProgram? ObjectProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    SourceReference? ObjectSource = null)
    : ExecutionInstruction(InstructionKind.EnterWith, Next);

/// <summary>
///     Marks the end of a <c>with</c> statement. Pops the with-environment from the scope chain.
/// </summary>
internal sealed record LeaveWithInstruction(Symbol WithScopeSlot, int Next)
    : ExecutionInstruction(InstructionKind.LeaveWith, Next);

/// <summary>
///     Sets the script completion value to undefined.
///     Used for empty blocks/branches per ES spec UpdateEmpty semantics.
/// </summary>
internal sealed record SetCompletionValueInstruction(int Next)
    : ExecutionInstruction(InstructionKind.SetCompletionValue, Next);

/// <summary>
///     Initializes the for-in loop by evaluating the object expression and collecting
///     enumerable property keys.
/// </summary>
/// <param name="ObjectProgram">Lowered bytecode representation of the object expression.</param>
/// <param name="StateSlot">Symbol for the for-in state.</param>
/// <param name="StateSlotIndex">Pre-resolved slot index for fast state access (-1 if not resolved).</param>
/// <param name="ValueSlot">Symbol for the current property key value.</param>
/// <param name="ValueSlotIndex">Pre-resolved slot index for fast value access (-1 if not resolved).</param>
/// <param name="Next">Jump target after initialization.</param>
/// <param name="TdzBindings">
///     Symbols that need TDZ bindings during object evaluation (for let/const declarations).
///     This ensures `for (const x in {[x]: 1})` throws ReferenceError for accessing x before initialization.
/// </param>
/// <param name="TdzIsConst">Whether the TDZ bindings are const (true) or let (false).</param>
internal sealed record ForInInitInstruction(
    Symbol StateSlot,
    int StateSlotIndex,
    Symbol ValueSlot,
    int ValueSlotIndex,
    int Next,
    ExpressionProgram? ObjectProgram = null,
    Symbol? AwaitStateKey = null,
    ExpressionProgram? AwaitedProgram = null,
    ImmutableArray<Symbol> TdzBindings = default,
    bool TdzIsConst = false,
    SourceReference? ObjectSource = null)
    : ExecutionInstruction(InstructionKind.ForInInit, Next);

/// <summary>
///     Advances the for-in loop to the next enumerable property key.
/// </summary>
/// <param name="StateSlot">Symbol for the for-in state.</param>
/// <param name="ValueSlot">Symbol for the current property key value.</param>
/// <param name="StateSlotIndex">Pre-resolved slot index for fast state access (-1 if not resolved).</param>
/// <param name="ValueSlotIndex">Pre-resolved slot index for fast value access (-1 if not resolved).</param>
/// <param name="BreakIndex">Jump target when iteration completes.</param>
/// <param name="Next">Jump target for the loop body.</param>
internal sealed record ForInMoveNextInstruction(
    Symbol StateSlot,
    Symbol ValueSlot,
    int StateSlotIndex,
    int ValueSlotIndex,
    int BreakIndex,
    int Next)
    : ExecutionInstruction(InstructionKind.ForInMoveNext, Next);

// ─────────────────────────────────────────────────────────────────────────────
// Array Destructuring Instructions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     Initializes array destructuring by evaluating the source expression and
///     getting an iterator from it. Stores the iterator state in a slot.
/// </summary>
/// <param name="SourceProgram">Lowered bytecode representation of the source expression.</param>
/// <param name="IteratorSlot">Symbol for storing the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="Next">Jump target after initialization (first element instruction).</param>
internal sealed record ArrayDestructuringInitInstruction(
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next,
    ExpressionProgram SourceProgram)
    : ExecutionInstruction(InstructionKind.ArrayDestructuringInit, Next)
{
    public ExpressionNode? SourceExpression => null;
}

/// <summary>
///     Extracts the next element from the iterator and binds it to a target symbol.
///     For holes (null target), advances the iterator without binding.
/// </summary>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="TargetSymbol">The target identifier to bind to (null for holes).</param>
/// <param name="TargetSlotIndex">Pre-resolved slot index for target (-1 if not resolved or null target).</param>
/// <param name="VarKind">The variable declaration kind (var/let/const).</param>
/// <param name="Next">Jump target after this element.</param>
internal sealed record ArrayDestructuringElementInstruction(
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    Symbol? TargetSymbol,
    int TargetSlotIndex,
    VariableKind VarKind,
    int Next)
    : ExecutionInstruction(InstructionKind.ArrayDestructuringElement, Next);

/// <summary>
///     Collects all remaining iterator values into an array and binds it to the rest target.
/// </summary>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="RestSymbol">The rest target identifier to bind to.</param>
/// <param name="RestSlotIndex">Pre-resolved slot index for rest target (-1 if not resolved).</param>
/// <param name="VarKind">The variable declaration kind (var/let/const).</param>
/// <param name="Next">Jump target after rest collection.</param>
internal sealed record ArrayDestructuringRestInstruction(
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    Symbol RestSymbol,
    int RestSlotIndex,
    VariableKind VarKind,
    int Next)
    : ExecutionInstruction(InstructionKind.ArrayDestructuringRest, Next);

/// <summary>
///     Closes the iterator after array destructuring is complete.
///     Must be called even if destructuring completes normally (per ES spec).
/// </summary>
/// <param name="IteratorSlot">Symbol for the iterator state.</param>
/// <param name="IteratorSlotIndex">Pre-resolved slot index for fast iterator state access (-1 if not resolved).</param>
/// <param name="Next">Jump target after closing.</param>
internal sealed record ArrayDestructuringCloseInstruction(
    Symbol IteratorSlot,
    int IteratorSlotIndex,
    int Next)
    : ExecutionInstruction(InstructionKind.ArrayDestructuringClose, Next);
