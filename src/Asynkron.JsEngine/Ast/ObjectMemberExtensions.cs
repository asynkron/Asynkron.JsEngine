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

extension(ObjectMember member)
    {
        private string ResolveObjectMemberName(JsEnvironment environment,
            EvaluationContext context)
        {
            object? keyValue;

            if (member.IsComputed)
            {
                if (member.Key is not ExpressionNode keyExpression)
                {
                    throw new InvalidOperationException("Computed property name must be an expression.");
                }

                keyValue = EvaluateExpression(keyExpression, environment, context);
            }
            else
            {
                keyValue = member.Key;
            }

            if (context.ShouldStopEvaluation)
            {
                return string.Empty;
            }

            var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
            return context.ShouldStopEvaluation ? string.Empty : propertyName;
        }
    }

}
