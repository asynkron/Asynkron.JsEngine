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

extension(DoWhileStatement statement)
    {
        private object? EvaluateDoWhile(JsEnvironment environment,
            EvaluationContext context,
            Symbol? loopLabel)
        {
            var isStrict = IsStrictBlock(statement.Body);
            if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
            {
                throw new NotSupportedException("Failed to normalize do/while loop.");
            }

            return EvaluateLoopPlan(plan, environment, context, loopLabel);
        }
    }

}
