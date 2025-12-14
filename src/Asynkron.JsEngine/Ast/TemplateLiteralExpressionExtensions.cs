using System.Text;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(TemplateLiteralExpression expression)
    {
        private object? EvaluateTemplateLiteral(JsEnvironment environment,
            EvaluationContext context)
        {
            var builder = new StringBuilder();
            foreach (var part in expression.Parts)
            {
                if (part.Text is not null)
                {
                    builder.Append(part.Text);
                    continue;
                }

                if (part.Expression is null)
                {
                    continue;
                }

                var valueJs = EvaluateExpression(part.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                builder.Append(valueJs.ToObject().ToJsString());
            }

            return builder.ToString();
        }
    }
}
