using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(LiteralExpression literal)
    {
        private object? EvaluateLiteral(EvaluationContext context)
        {
            return literal.Value switch
            {
                RegexLiteralValue regex => StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags,
                    context.RealmState),
                _ => literal.Value
            };
        }
    }

}
