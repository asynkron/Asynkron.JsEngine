using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeExpressionProgramExecutor
{
    internal static JsValue ExecuteStandalone(
        ExpressionProgram program,
        JsEnvironment environment,
        EvaluationContext context,
        JsValue newTarget = default)
    {
        if (!UnifiedBytecodeCompiler.TryCompileStandaloneExpressionProgram(
                program,
                allowsDynamicIdentifiers: true,
                out var unifiedProgram,
                out var reason))
        {
            throw new NotSupportedException(
                $"Lowered expression program could not be compiled to standalone unified bytecode: {reason}");
        }

        var slots = unifiedProgram.SlotCount == 0
            ? Array.Empty<JsValue>()
            : new JsValue[unifiedProgram.SlotCount];
        return UnifiedBytecodeVirtualMachine.Execute(
            unifiedProgram,
            slots,
            context,
            environment,
            newTarget: newTarget,
            isStrict: environment.IsStrict);
    }
}
