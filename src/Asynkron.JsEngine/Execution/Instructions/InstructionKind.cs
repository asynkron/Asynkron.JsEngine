namespace Asynkron.JsEngine.Execution.Instructions;

/// <summary>
///     Discriminator enum for fast instruction dispatch.
///     Using enum-based dispatch instead of type pattern matching
///     avoids runtime type checks and enables jump table optimization.
/// </summary>
internal enum InstructionKind : byte
{
    Statement,
    Throw,
    EvaluateAndDiscard,
    BinaryOp,
    IncrementSlot,
    FunctionDeclaration,
    ClassDeclaration,
    SimpleVariableDeclaration,
    PushEnvironment,
    PopEnvironment,
    Yield,
    YieldStar,
    StoreResumeValue,
    EnterTry,
    EnterCatch,
    LeaveTry,
    LoopEnter,
    LoopExit,
    EndFinally,
    IteratorInit,
    IteratorMoveNext,
    IteratorClose,
    Jump,
    Branch,
    Break,
    Continue,
    Return,
    EnterWith,
    LeaveWith,
    Expression,
}
