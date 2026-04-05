#region

using System.Text;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTemplateLiteral(this TemplateLiteralExpression expression, JsEnvironment environment,
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

            var valueJs = EvaluateCachedExpressionProgram(
                part.Expression,
                environment,
                context,
                "Dynamic template literal expression");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            builder.Append(valueJs.ToJsString());
        }

        return new JsValue(builder.ToString());
    }
}
