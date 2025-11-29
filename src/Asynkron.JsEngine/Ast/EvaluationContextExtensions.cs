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
    }

    extension(EvaluationContext context)
    {
        private void RestoreSignal(ISignal? signal)
        {
            switch (signal)
            {
                case null:
                    return;
                case ReturnSignal returnSignal:
                    context.SetReturn(returnSignal.Value);
                    break;
                case BreakSignal breakSignal:
                    context.SetBreak(breakSignal.Label);
                    break;
                case ContinueSignal continueSignal:
                    context.SetContinue(continueSignal.Label);
                    break;
                case ThrowFlowSignal throwSignal:
                    context.SetThrow(throwSignal.Value);
                    break;
            }
        }
    }
}
