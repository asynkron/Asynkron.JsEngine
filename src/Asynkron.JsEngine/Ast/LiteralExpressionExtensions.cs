using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(LiteralExpression literal)
    {
        private JsValue EvaluateLiteral(EvaluationContext context)
        {
            return literal.Value switch
            {
                null => JsValue.Null,
                true => JsValue.True,
                false => JsValue.False,
                double d => new JsValue(d),
                int i => new JsValue(i),
                long l => new JsValue(l),
                string s => new JsValue(s),
                JsBigInt bigInt => new JsValue(bigInt),
                RegexLiteralValue regex => new JsValue(StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags,
                    context.RealmState)),
                _ => JsValue.FromObject(literal.Value)
            };
        }
    }
}
