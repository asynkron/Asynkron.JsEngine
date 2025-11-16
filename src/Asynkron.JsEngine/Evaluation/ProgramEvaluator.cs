using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine.Evaluation;

public static class ProgramEvaluator
{
    internal static object? EvaluateBlock(Cons block, JsEnvironment environment, EvaluationContext context)
    {
        context.SourceReference = block.SourceReference;

        if (block.IsEmpty || block.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Block))
        {
            throw new InvalidOperationException($"Block S-expression must start with the 'block' symbol.{EvaluationGuards.GetSourceInfo(context)}");
        }

        // Check if block has "use strict" directive
        var isStrict = false;
        var statements = block.Rest;
        if (statements is { IsEmpty: false, Head: Cons { Head: Symbol useStrictSymbol } } &&
            ReferenceEquals(useStrictSymbol, JsSymbols.UseStrict))
        {
            isStrict = true;
            statements = statements.Rest; // Skip the use strict directive
        }

        var scope = new JsEnvironment(environment, false, isStrict);
        object? result = null;
        foreach (var statement in statements)
        {
            result = StatementEvaluator.EvaluateStatement(statement, scope, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return result;
    }
}
