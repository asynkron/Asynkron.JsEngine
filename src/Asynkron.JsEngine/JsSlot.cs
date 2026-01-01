using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine;

/// <summary>
/// Flags for slot binding metadata, packed into a single byte.
/// </summary>
[Flags]
public enum SlotFlags : byte
{
    None = 0,
    Const = 1,
    Lexical = 2,
    Uninitialized = 4,
    GlobalConstant = 8,
    BlocksFunctionScopeOverride = 16,
    CanDelete = 32,
    ImmutableBinding = 64,
    HasSpecialBinding = 128,
}

/// <summary>
/// A slot in the environment that holds a binding's name, value, and metadata.
/// Uses struct layout for cache-friendly access - name, value, and flags are co-located.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct JsSlot
{
    /// <summary>
    /// The symbol name for this binding. Set once during initialization.
    /// </summary>
    public Symbol Name;

    /// <summary>
    /// The current value of this binding.
    /// </summary>
    public JsValue Value;

    /// <summary>
    /// Packed binding flags (const, lexical, uninitialized, etc.).
    /// </summary>
    public SlotFlags Flags;

    /// <summary>
    /// Creates a new slot with the given name, value, and flags.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsSlot(Symbol name, JsValue value, SlotFlags flags)
    {
        Name = name;
        Value = value;
        Flags = flags;
    }

    /// <summary>
    /// Creates an uninitialized slot (for let/const before initialization).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsSlot Uninitialized(Symbol name, SlotFlags flags)
    {
        return new JsSlot(name, JsValue.Undefined, flags | SlotFlags.Uninitialized);
    }

    /// <summary>
    /// Returns true if this slot has not been initialized yet (TDZ).
    /// </summary>
    public readonly bool IsUninitialized
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.Uninitialized) != 0;
    }

    /// <summary>
    /// Returns true if this is a const binding.
    /// </summary>
    public readonly bool IsConst
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.Const) != 0;
    }

    /// <summary>
    /// Returns true if this is a lexical binding (let/const).
    /// </summary>
    public readonly bool IsLexical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.Lexical) != 0;
    }

    /// <summary>
    /// Returns true if this is a global constant (built-in like undefined, NaN).
    /// </summary>
    public readonly bool IsGlobalConstant
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.GlobalConstant) != 0;
    }

    /// <summary>
    /// Returns true if this binding blocks function-scope override.
    /// </summary>
    public readonly bool BlocksFunctionScopeOverride
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.BlocksFunctionScopeOverride) != 0;
    }

    /// <summary>
    /// Returns true if this binding can be deleted.
    /// </summary>
    public readonly bool CanDelete
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.CanDelete) != 0;
    }

    /// <summary>
    /// Returns true if this binding is immutable (imports).
    /// </summary>
    public readonly bool IsImmutableBinding
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.ImmutableBinding) != 0;
    }

    /// <summary>
    /// Returns true if this slot holds a special binding (import/export wrapper).
    /// When true, Value.ObjectValue is an ISpecialBinding.
    /// </summary>
    public readonly bool HasSpecialBinding
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Flags & SlotFlags.HasSpecialBinding) != 0;
    }

    /// <summary>
    /// Clears the uninitialized flag (called when binding is initialized).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkInitialized()
    {
        Flags &= ~SlotFlags.Uninitialized;
    }

    /// <summary>
    /// Sets the value and clears the uninitialized flag.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Initialize(JsValue value)
    {
        Value = value;
        Flags &= ~SlotFlags.Uninitialized;
    }

    /// <summary>
    /// Sets the value and clears the TDZ flag if the value is not uninitialized.
    /// Use this when assigning to a binding that might be in TDZ.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueAndClearTdz(JsValue value)
    {
        Value = value;
        if (!value.IsUninitialized)
        {
            Flags &= ~SlotFlags.Uninitialized;
        }
    }

    /// <summary>
    /// Returns true if this slot is empty (no name assigned).
    /// </summary>
    public readonly bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Name is null;
    }
}
