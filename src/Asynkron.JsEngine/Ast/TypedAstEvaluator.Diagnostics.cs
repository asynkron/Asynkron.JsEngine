using System.Diagnostics;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static readonly ActivitySource EvaluatorActivitySource = new("Asynkron.JsEngine.TypedAstEvaluator");

    private static Activity? StartEvaluatorActivity(this Activity parent,
        string name,
        EvaluationContext context,
        SourceReference? source)
    {
        if (!EvaluatorActivitySource.HasListeners())
        {
            return null;
        }

        var activity = EvaluatorActivitySource.StartActivity(name, ActivityKind.Internal, parent.Context);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("js.execution.kind", context.ExecutionKind.ToString());
        activity.SetTag("js.scope.mode", context.CurrentScope.Mode.ToString());

        if (source is null)
        {
            return activity;
        }

        activity.SetTag("code.lineno", source.StartLine);
        activity.SetTag("code.column", source.StartColumn);
        activity.SetTag("code.span",
            $"{source.StartLine}:{source.StartColumn}-{source.EndLine}:{source.EndColumn}");

        return activity;
    }
}
