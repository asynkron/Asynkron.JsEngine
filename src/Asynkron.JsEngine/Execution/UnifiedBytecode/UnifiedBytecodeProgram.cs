using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal enum UnifiedBytecodeOpCode : byte
{
    LoadSlot,
    LoadDynamicIdentifier,
    LoadThis,
    LoadNewTarget,
    LoadLiteral,
    StoreSlot,
    InitializeSlot,
    DeclareDynamicVar,
    StoreDynamicIdentifier,
    ResolveDynamicIdentifierReference,
    LoadDynamicIdentifierReference,
    StoreDynamicIdentifierReference,
    PopDynamicIdentifierReference,
    Binary,
    RequireObjectCoercible,
    ResolvePropertyKey,
    GetNamedProperty,
    GetComputedProperty,
    GetNamedPropertyForCompoundSet,
    GetComputedPropertyForCompoundSet,
    SetNamedProperty,
    SetComputedProperty,
    UpdateNamedProperty,
    UpdateComputedProperty,
    UpdateDynamicIdentifier,
    TypeOf,
    TypeOfIdentifier,
    TypeOfDynamicIdentifier,
    DeleteDynamicIdentifier,
    UnaryPlus,
    UnaryMinus,
    UnaryLogicalNot,
    UnaryBitwiseNot,
    UnaryVoid,
    ToString,
    Pop,
    CreateArray,
    ArrayPush,
    ArrayPushHole,
    CreateObject,
    DefineObjectProperty,
    DefineComputedObjectProperty,
    Jump,
    JumpWithDriverCleanup,
    JumpIfFalse,
    Return,
    ReturnUndefined,
    Throw,
    Break,
    Continue,
    PushEnvironment,
    PopEnvironment,
    EnterTry,
    EnterCatch,
    LeaveTry,
    EndFinally,
    EnterWith,
    LeaveWith,
    IteratorInit,
    IteratorMoveNext,
    IteratorClose,
    ForInInit,
    ForInMoveNext,
    ArrayDestructuringInit,
    ArrayDestructuringElement,
    ArrayDestructuringRest,
    ArrayDestructuringClose,
    PrepareIdentifierCallTarget,
    PrepareDynamicIdentifierCallTarget,
    PrepareNamedCallTarget,
    PrepareComputedCallTarget,
    CallInvocationBoundary,
    Yield,
    StoreResumeValue,
    AwaitAndDiscard,
    AwaitedReturn,
    YieldStar
}

internal readonly record struct UnifiedBytecodeInstruction(
    UnifiedBytecodeOpCode OpCode,
    int Operand = 0);

internal enum UnifiedBytecodeCallTargetKind : byte
{
    Identifier,
    NamedMember,
    ComputedMember
}

internal readonly record struct UnifiedBytecodeCallTarget(
    UnifiedBytecodeCallTargetKind Kind,
    int SlotIndex = -1,
    int NameConstantIndex = -1);

internal readonly record struct UnifiedBytecodeScopeDescriptor(
    int ScopeId,
    ImmutableArray<int> LexicalSlotIndices);

internal readonly record struct UnifiedBytecodeTryDescriptor(
    int HandlerTarget,
    int FinallyTarget,
    int EndFinallyTarget,
    int LeaveTryTarget,
    int LoopContinueTarget = -1,
    int LoopBreakTarget = -1);

internal readonly record struct UnifiedBytecodeCatchDescriptor(
    int ScopeId,
    ImmutableArray<int> SlotIndices,
    Symbol? BindingName,
    int BindingSlot = -1);

internal readonly record struct UnifiedBytecodeDriverDescriptor(
    int StateSlot,
    int ValueSlot = -1,
    int TargetSlot = -1,
    int BreakTarget = -1,
    int NextTarget = -1,
    IteratorDriverKind IteratorKind = IteratorDriverKind.Sync);

internal enum UnifiedBytecodeResumeMode : byte
{
    Next,
    Throw,
    Return
}

internal enum UnifiedBytecodeResumePayloadKind : byte
{
    None,
    Value,
    Throw,
    Return
}

internal enum UnifiedBytecodeStepKind : byte
{
    Completed,
    Yield,
    PendingAwait,
    Throw
}

internal enum UnifiedBytecodeAbruptCompletionKind : byte
{
    None,
    Return,
    Throw,
    Break,
    Continue
}

internal readonly record struct UnifiedBytecodePendingAbruptCompletion(
    UnifiedBytecodeAbruptCompletionKind Kind,
    JsTypes.JsValue Value,
    int Target,
    int ResumeTarget,
    bool OriginatedInFinally)
{
    public static UnifiedBytecodePendingAbruptCompletion None { get; } =
        new(
            UnifiedBytecodeAbruptCompletionKind.None,
            JsTypes.JsValue.Undefined,
            -1,
            -1,
            false);
}

internal readonly record struct UnifiedBytecodeStepResult(
    UnifiedBytecodeStepKind Kind,
    JsTypes.JsValue Value,
    bool Done,
    JsTypes.JsValue PendingPromise)
{
    public static UnifiedBytecodeStepResult Completed(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Completed, value, true, JsTypes.JsValue.Undefined);

    public static UnifiedBytecodeStepResult Yield(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Yield, value, false, JsTypes.JsValue.Undefined);

    public static UnifiedBytecodeStepResult PendingAwait(JsTypes.JsValue promise) =>
        new(UnifiedBytecodeStepKind.PendingAwait, JsTypes.JsValue.Undefined, false, promise);

    public static UnifiedBytecodeStepResult Throw(JsTypes.JsValue value) =>
        new(UnifiedBytecodeStepKind.Throw, value, true, JsTypes.JsValue.Undefined);
}

internal sealed class UnifiedBytecodeResumeState
{
    public UnifiedBytecodeResumeState(UnifiedBytecodeProgram program, JsTypes.JsValue[] slots)
    {
        Program = program;
        Slots = slots;
        OperandStack = new JsTypes.JsValue[Math.Max(program.MaxStackDepth, 2)];
    }

    public UnifiedBytecodeProgram Program { get; }
    public JsTypes.JsValue[] Slots { get; }
    public JsTypes.JsValue[] OperandStack { get; }
    public int ProgramCounter { get; set; }
    public int StackPointer { get; set; }
    public bool IsCompleted { get; set; }
    public UnifiedBytecodeResumePayloadKind ResumePayloadKind { get; set; }
    public JsTypes.JsValue ResumePayload { get; set; } = JsTypes.JsValue.Undefined;
    public UnifiedBytecodePendingAbruptCompletion PendingAbruptCompletion { get; set; } =
        UnifiedBytecodePendingAbruptCompletion.None;
    public JsTypes.JsValue PendingAwaitPromise { get; set; } = JsTypes.JsValue.Undefined;
}

internal sealed record UnifiedBytecodeProgram(
    ImmutableArray<UnifiedBytecodeInstruction> Instructions,
    int MaxStackDepth,
    int SlotCount,
    ImmutableArray<JsTypes.JsValue> LiteralConstants,
    ImmutableArray<string> StringConstants,
    ImmutableArray<string?> SlotNames,
    ImmutableArray<int> ParameterSlotIndices,
    ImmutableArray<int> LexicalSlotIndices,
    ImmutableArray<UnifiedBytecodeCallTarget> CallTargetConstants,
    ImmutableArray<UnifiedBytecodeScopeDescriptor> ScopeDescriptors,
    ImmutableArray<UnifiedBytecodeTryDescriptor> TryDescriptors,
    ImmutableArray<UnifiedBytecodeCatchDescriptor> CatchDescriptors,
    ImmutableArray<UnifiedBytecodeDriverDescriptor> DriverDescriptors);
