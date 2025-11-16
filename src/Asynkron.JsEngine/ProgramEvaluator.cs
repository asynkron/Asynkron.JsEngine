using System;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Lisp;

namespace Asynkron.JsEngine;

/// <summary>
/// Legacy cons-based program evaluator shim. The legacy interpreter still
/// consumes S-expressions, so the executor provides a thin adapter that can be
/// swapped out once the original implementation is reintroduced.
/// </summary>
internal static class ProgramEvaluator
{
    private static readonly SExpressionAstBuilder AstBuilder = new();

    /// <summary>
    /// Evaluates the provided transformed S-expression in the supplied environment.
    /// </summary>
    public static object? Evaluate(Cons program, JsEnvironment environment)
    {
        if (program is null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        // TODO: Replace this shim with the real cons-based interpreter once it
        // lands in the repository. For now we rebuild the typed tree to mirror
        // the existing behaviour so callers can exercise the fallback path.
        var typedProgram = AstBuilder.BuildProgram(program);
        return TypedAstEvaluator.EvaluateProgram(typedProgram, environment);
    }
}
