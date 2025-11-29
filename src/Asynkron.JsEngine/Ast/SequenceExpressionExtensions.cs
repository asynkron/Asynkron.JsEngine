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

extension(SequenceExpression expression)
    {
        private object? EvaluateSequence(JsEnvironment environment,
            EvaluationContext context)
        {
            _ = EvaluateExpression(expression.Left, environment, context);
            return context.ShouldStopEvaluation
                ? Symbol.Undefined
                : EvaluateExpression(expression.Right, environment, context);
        }
    }

}
