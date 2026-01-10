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
        [MethodImpl(JsEngineConstants.Inlining)]
        get => Environment.IsSlotConst(SlotIndex);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public JsValue Read()
    {
        return Environment.GetSlotRef(SlotIndex);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    public void Write(JsValue value)
    {
        Environment.SetSlotDirect(SlotIndex, value);
    }
}
