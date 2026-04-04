namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Discriminator enum for fast instruction dispatch.
///     Using enum-based dispatch instead of type pattern matching
///     avoids runtime type checks and enables jump table optimization.
/// </summary>
internal enum InstructionKind : byte
{
    Throw,
    EvaluateAndDiscard,
    AwaitAndDiscard,
    IncrementSlot,
    AssignmentSlot,
    LogicalCompoundAssignmentSlot,
    FunctionDeclaration,
    ClassDeclaration,
    SimpleVariableDeclaration,
    BindingVariableDeclaration,
    PushEnvironment,
    PopEnvironment,
    Yield,
    YieldStar,
    SuspendingYieldStar,
    StoreResumeValue,
    EnterTry,
    EnterCatch,
    LeaveTry,
    BreakableEnter,
    BreakableExit,
    EndFinally,
    IteratorInit,
    SuspendingIteratorInit,
    IteratorMoveNext,
    IteratorClose,
    Jump,
    Branch,
    Break,
    Continue,
    Return,
    EnterWith,
    SuspendingEnterWith,
    LeaveWith,
    SetCompletionValue,
    CompoundAssignmentSlot,
    ForInInit,
    SuspendingForInInit,
    ForInMoveNext,

    // Array destructuring
    ArrayDestructuringInit,
    ArrayDestructuringElement,
    ArrayDestructuringRest,
    ArrayDestructuringClose
}

/// <summary>
///     Distinguishes how breakable constructs handle completion values.
/// </summary>
internal enum BreakableKind : byte
{
    /// <summary>
    ///     Resets completion value to Unit on entry, finalizes on exit.
    ///     Used by: loops (for, while, do-while, for-of, for-in) and labeled non-loop statements.
    ///     These constructs need the runtime to initialize their completion value.
    /// </summary>
    ResetsCompletionValue,

    /// <summary>
    ///     Does NOT reset completion value on entry.
    ///     Used by: switch statements.
    ///     The switch emitter handles completion value initialization explicitly
    ///     with an `undefined;` statement per ES spec 13.12.9.
    /// </summary>
    HandlesCompletionInternally
}
