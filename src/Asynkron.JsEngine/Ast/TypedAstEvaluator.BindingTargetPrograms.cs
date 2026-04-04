#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static readonly ConditionalWeakTable<BindingTarget, BindingTargetProgramCache>
        BindingTargetPrograms = new();

    private static void ApplyCompiledBindingTarget(
        BindingTarget target,
        JsValue value,
        JsEnvironment environment,
        EvaluationContext context,
        BindingMode mode,
        bool hasInitializer = true,
        bool allowNameInference = true,
        bool skipBlockedBindingLookup = false)
    {
        var cache = BindingTargetPrograms.GetValue(target, static bindingTarget =>
        {
            if (BindingTargetProgramCompiler.TryCompile(bindingTarget, out var program, out var failureReason))
            {
                return BindingTargetProgramCache.Success(program);
            }

            return BindingTargetProgramCache.Failure(failureReason ?? "unknown failure");
        });

        if (!cache.Succeeded)
        {
            throw new NotSupportedException(
                $"Binding target could not be lowered to bytecode: {cache.FailureReason}");
        }

        ExecutionPlanRunner.ApplyStandaloneBindingTargetProgram(
            cache.Program,
            value,
            environment,
            context,
            mode,
            hasInitializer,
            allowNameInference,
            skipBlockedBindingLookup);
    }

    private sealed class BindingTargetProgramCache
    {
        private BindingTargetProgramCache(
            bool succeeded,
            BindingTargetProgram program,
            string? failureReason)
        {
            Succeeded = succeeded;
            Program = program;
            FailureReason = failureReason;
        }

        public bool Succeeded { get; }
        public BindingTargetProgram Program { get; }
        public string? FailureReason { get; }

        public static BindingTargetProgramCache Success(BindingTargetProgram program) =>
            new(true, program, failureReason: null);

        public static BindingTargetProgramCache Failure(string failureReason) =>
            new(false, default!, failureReason);
    }
}
