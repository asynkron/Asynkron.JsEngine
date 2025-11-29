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

extension(ConditionalExpression expression)
    {
        private object? EvaluateConditional(JsEnvironment environment,
            EvaluationContext context)
        {
            var test = EvaluateExpression(expression.Test, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return IsTruthy(test)
                ? EvaluateExpression(expression.Consequent, environment, context)
                : EvaluateExpression(expression.Alternate, environment, context);
        }
    }

}
