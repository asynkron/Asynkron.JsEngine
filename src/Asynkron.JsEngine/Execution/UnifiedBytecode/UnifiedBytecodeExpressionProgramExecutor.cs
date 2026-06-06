using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

namespace Asynkron.JsEngine.Execution.UnifiedBytecode;

internal static class UnifiedBytecodeExpressionProgramExecutor
{
    private static readonly ConditionalWeakTable<ExpressionNode, ExpressionProgramCache>
        ExpressionPrograms = new();

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

    internal static JsValue ExecuteDynamic(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        string failureLabel)
    {
        var cache = ExpressionPrograms.GetValue(expression, static node =>
        {
            if (ExpressionProgramCompiler.TryCompile(node, out var program, out var failureReason))
            {
                return ExpressionProgramCache.Success(program);
            }

            return ExpressionProgramCache.Failure(failureReason ?? "unknown failure");
        });

        if (!cache.Succeeded)
        {
            throw new NotSupportedException(
                $"{failureLabel} could not be lowered to expression bytecode: {cache.FailureReason}");
        }

        return ExecuteStandalone(cache.Program, environment, context);
    }

    private sealed class ExpressionProgramCache
    {
        private ExpressionProgramCache(
            bool succeeded,
            ExpressionProgram program,
            string? failureReason)
        {
            Succeeded = succeeded;
            Program = program;
            FailureReason = failureReason;
        }

        public bool Succeeded { get; }
        public ExpressionProgram Program { get; }
        public string? FailureReason { get; }

        public static ExpressionProgramCache Success(ExpressionProgram program) =>
            new(true, program, failureReason: null);

        public static ExpressionProgramCache Failure(string failureReason) =>
            new(false, default, failureReason);
    }
}
