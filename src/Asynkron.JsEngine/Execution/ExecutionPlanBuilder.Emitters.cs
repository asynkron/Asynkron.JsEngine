using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Emitters;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Partial class exposing internal members for emitters.
/// </summary>
internal sealed partial class ExecutionPlanBuilder
{
    // Cached EmitContext instance
    private EmitContext? _emitContext;

    /// <summary>
    /// Get or create the EmitContext for this builder.
    /// </summary>
    private EmitContext GetEmitContext()
    {
        return _emitContext ??= new EmitContext(this, _instructions, _loopScopes);
    }

    /// <summary>
    /// Sets the failure reason if not already set.
    /// </summary>
    internal void SetFailureReason(string reason)
    {
        _failureReason ??= reason;
    }

    /// <summary>
    /// Access to the instruction list for emitters.
    /// </summary>
    internal List<ExecutionInstruction> Instructions => _instructions;

    /// <summary>
    /// Loop scope structure for break/continue resolution.
    /// </summary>
    internal readonly record struct LoopScope(Symbol? Label, int ContinueTarget, int BreakTarget, int TargetScopeId);
}
