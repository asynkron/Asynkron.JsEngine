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

extension(DestructuringAssignmentExpression expression)
    {
        private object? EvaluateDestructuringAssignment(JsEnvironment environment, EvaluationContext context)
        {
            var assignedValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return assignedValue;
            }

            // Reuse the same binding machinery as variable declarations so nested
            // destructuring assignments behave consistently.
            AssignBindingTarget(expression.Target, assignedValue, environment, context);
            return assignedValue;
        }
    }

}
