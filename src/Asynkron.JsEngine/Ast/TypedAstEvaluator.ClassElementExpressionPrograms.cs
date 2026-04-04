#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static readonly ConditionalWeakTable<ExpressionNode, ClassElementExpressionProgramCache>
        ClassElementExpressionPrograms = new();

    private static JsValue EvaluateClassElementExpressionProgram(
        ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var cache = ClassElementExpressionPrograms.GetValue(expression, static node =>
        {
            if (ExpressionProgramCompiler.TryCompile(node, out var program, out var failureReason))
            {
                return ClassElementExpressionProgramCache.Success(program);
            }

            return ClassElementExpressionProgramCache.Failure(failureReason ?? "unknown failure");
        });

        if (!cache.Succeeded)
        {
            throw new NotSupportedException(
                $"Class element expression could not be lowered to expression bytecode: {cache.FailureReason}");
        }

        return ExecutionPlanRunner.EvaluateStandaloneExpressionProgram(cache.Program, environment, context);
    }

    private sealed class ClassElementExpressionProgramCache
    {
        private ClassElementExpressionProgramCache(
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

        public static ClassElementExpressionProgramCache Success(ExpressionProgram program) =>
            new(true, program, failureReason: null);

        public static ClassElementExpressionProgramCache Failure(string failureReason) =>
            new(false, default, failureReason);
    }
}
