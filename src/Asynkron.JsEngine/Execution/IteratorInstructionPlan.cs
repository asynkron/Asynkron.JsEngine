#region

using Asynkron.JsEngine.Ast;

#endregion

namespace Asynkron.JsEngine.Execution;

internal readonly record struct IteratorInstructionPlan(
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int IteratorSlotIndex,
    int ValueSlotIndex,
    int InitIndex,
    int MoveNextIndex);
