#region

using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static string GetSourceInfo(this EvaluationContext context, SourceReference? fallback = null)
    {
        var source = fallback ?? context.SourceReference;
        if (source is null)
        {
            return " (no source reference)";
        }

        var snippet = source.GetText();
        if (snippet.Length > 50)
        {
            snippet = snippet[..47] + "...";
        }

        return
            $" at {source} (snippet: '{snippet}') Source: '{source.Source}' Start: {source.StartPosition} End: {source.EndPosition}";
    }

    private static void RestoreSignal(this EvaluationContext context, ICompletionSignal? signal)
    {
        switch (signal)
        {
            case null:
                return;
            case BreakCompletionSignal breakSignal:
                context.SetBreak(breakSignal.Label);
                break;
            case ContinueCompletionSignal continueSignal:
                context.SetContinue(continueSignal.Label);
                break;
            case ThrowFlowCompletionSignal throwSignal:
                context.SetThrow(throwSignal.JsValue);
                break;
            case PendingAwaitCompletionSignal:
                context.SetPendingAwait();
                break;
        }
    }
}
