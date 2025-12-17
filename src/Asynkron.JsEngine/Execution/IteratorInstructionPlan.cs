using Asynkron.JsEngine.Ast;

namespace Asynkron.JsEngine.Execution;

internal readonly record struct IteratorInstructionPlan(
    Symbol IteratorSlot,
    Symbol ValueSlot,
    int InitIndex,
    int MoveNextIndex);
