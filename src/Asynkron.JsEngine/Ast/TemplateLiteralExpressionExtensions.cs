using System.Text;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(TemplateLiteralExpression expression)
    {
        private JsValue EvaluateTemplateLiteral(JsEnvironment environment,
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

                var valueJs = part.Expression.EvaluateExpression(environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                builder.Append(valueJs.ToObject().ToJsString());
            }

            return new JsValue(builder.ToString());
        }
    }
}
