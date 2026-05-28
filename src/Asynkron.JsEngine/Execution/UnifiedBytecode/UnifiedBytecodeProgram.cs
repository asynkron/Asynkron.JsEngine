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
    CallInvocationBoundary
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
