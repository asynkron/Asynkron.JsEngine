#region

using Asynkron.JsEngine.JsTypes;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(RegexLiteralExpression regex)
    {
        /// <summary>
        ///     Evaluates a regex literal by creating a RegExp object using cached pattern and compiled regex.
        ///     The cache avoids repeated pattern normalization and regex compilation.
        /// </summary>
        private JsValue EvaluateRegexLiteral(EvaluationContext context)
        {
            var cache = ((IAstCacheable<RegexCache>)regex).GetOrCreateCache();
            var jsRegExp = new JsRegExp(cache, context.RealmState);
            var target = jsRegExp.JsObject;
            target["__regex__"] = jsRegExp;

            if (context.RealmState?.RegExpPrototype is not null)
            {
                target.SetPrototype(context.RealmState.RegExpPrototype);
            }

            return new JsValue(target);
        }
    }
}
