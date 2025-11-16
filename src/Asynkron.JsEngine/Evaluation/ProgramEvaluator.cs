using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine;

public static class ProgramEvaluator
{
    public static object? EvaluateProgram(Cons program, JsEnvironment environment)
    {
        try
        {
            return EvaluateProgram(program, environment, new EvaluationContext());
        }
        catch (StackOverflowException)
        {
            Console.WriteLine("Stack overflow during evaluation. Possible infinite recursion detected.");
            throw;
        }
    }

    internal static object? EvaluateProgram(Cons program, JsEnvironment environment, EvaluationContext context)
    {
        context.SourceReference = program.SourceReference;

        if (program.IsEmpty || program.Head is not Symbol { } tag || !ReferenceEquals(tag, JsSymbols.Program))
        {
            throw new InvalidOperationException($"Program S-expression must start with the 'program' symbol.{EvaluationGuards.GetSourceInfo(context)}");
        }

        // Check if program has "use strict" directive
        var hasUseStrict = false;
        var statements = program.Rest;
        if (statements is { IsEmpty: false, Head: Cons { Head: Symbol useStrictSymbol } } &&
            ReferenceEquals(useStrictSymbol, JsSymbols.UseStrict))
        {
            hasUseStrict = true;
            statements = statements.Rest; // Skip the use strict directive
        }

        // For global programs with strict mode, we need a wrapper environment
        // to enable strict mode checking without modifying the global environment
        var evalEnv = hasUseStrict ? new JsEnvironment(environment, true, true) : environment;

        object? result = null;
        foreach (var statement in statements)
        {
            result = StatementEvaluator.EvaluateStatement(statement, evalEnv, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        // If there's an unhandled throw, convert it to an exception
        if (context.IsThrow)
        {
            throw new ThrowSignal(context.FlowValue);
        }

        return result;
    }

    public static object? EvaluateBlock(Cons block, JsEnvironment environment)
    {
        return EvaluateBlock(block, environment, new EvaluationContext());
    }

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
