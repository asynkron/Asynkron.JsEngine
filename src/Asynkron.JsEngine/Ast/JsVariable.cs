using System.Runtime.CompilerServices;

namespace Asynkron.JsEngine.Ast;

/// <summary>
/// Represents a resolved reference to a JavaScript variable within its lexical scope.
/// Provides fast read/write access by holding the environment and slot index.
/// </summary>
internal readonly struct JsVariable(JsEnvironment environment, int slotIndex)
{
    public readonly JsEnvironment Environment = environment;
    public readonly int SlotIndex = slotIndex;

    public bool IsValid => Environment is not null && SlotIndex >= 0;

    /// <summary>
    /// Returns true if this variable is a const binding.
    /// </summary>
    public bool IsConst
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Environment.IsSlotConst(SlotIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue Read()
    {
        return Environment.GetSlotRef(SlotIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(JsValue value)
    {
        Environment.SetSlotDirect(SlotIndex, value);
    }
}
