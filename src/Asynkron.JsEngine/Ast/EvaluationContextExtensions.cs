using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(EvaluationContext context)
    {
        private string GetSourceInfo(SourceReference? fallback = null)
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

        private void RestoreSignal(ICompletionSignal? signal)
        {
            switch (signal)
            {
                case null:
                    return;
                case ReturnCompletionSignal returnSignal:
                    context.SetReturn(returnSignal.JsValue);
                    break;
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
}
