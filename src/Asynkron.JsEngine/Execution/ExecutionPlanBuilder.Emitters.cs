#region

using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Emitters;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Partial class exposing internal members for emitters.
/// </summary>
internal sealed partial class ExecutionPlanBuilder
{
    // Cached EmitContext instance
    private EmitContext? _emitContext;

    /// <summary>
    /// Access to the instruction list for emitters.
    /// </summary>
    internal List<ExecutionInstruction> Instructions { get; } = [];

    /// <summary>
    /// Get or create the EmitContext for this builder.
    /// </summary>
    private EmitContext GetEmitContext()
    {
        return _emitContext ??= new EmitContext(this, Instructions, _loopScopes, _analysisRootScopeId);
    }

    /// <summary>
    /// Sets the failure reason if not already set.
    /// </summary>
    internal void SetFailureReason(string reason, ExecutionPlanFailureCode code = ExecutionPlanFailureCode.UnsupportedConstruct)
    {
        if (_failureReason is not null)
        {
            return;
        }

        _failureReason = reason;
        _failureCode = code;
    }

    internal void SetExpressionProgramFailure(string instructionName, ExpressionNode expression, string? failureReason)
    {
        if (_failureReason is not null)
        {
            return;
        }

        var classifiedFailure = ExpressionProgramCompiler.ClassifyFailure(expression, failureReason);
        _failureReason =
            $"{instructionName} could not lower expression '{expression.GetType().Name}' to bytecode [{classifiedFailure.Code}]: {classifiedFailure.Detail}.";
        _failureCode = ExecutionPlanFailureCode.UnsupportedExpressionProgram;
        _expressionFailureCode = classifiedFailure.Code;
    }

    /// <summary>
    /// Loop scope structure for break/continue resolution.
    /// </summary>
    internal readonly record struct LoopScope(
        Symbol? Label,
        int ContinueTarget,
        int BreakTarget,
        ScopeExitBoundary TargetBoundary);
}
