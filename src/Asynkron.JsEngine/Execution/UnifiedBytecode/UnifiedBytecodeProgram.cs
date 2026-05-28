using System.Collections.Immutable;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal enum UnifiedBytecodeOpCode : byte
{
    LoadSlot,
    LoadLiteral,
    StoreSlot,
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
    Jump,
    JumpIfFalse,
    Return,
    PrepareIdentifierCallTarget,
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

internal sealed record UnifiedBytecodeProgram(
    ImmutableArray<UnifiedBytecodeInstruction> Instructions,
    int MaxStackDepth,
    ImmutableArray<JsTypes.JsValue> LiteralConstants,
    ImmutableArray<string> StringConstants,
    ImmutableArray<UnifiedBytecodeCallTarget> CallTargetConstants);
