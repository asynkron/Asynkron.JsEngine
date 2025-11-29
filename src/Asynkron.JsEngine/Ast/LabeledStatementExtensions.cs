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

extension(LabeledStatement statement)
    {
        private object? EvaluateLabeled(JsEnvironment environment,
            EvaluationContext context)
        {
            context.PushLabel(statement.Label);
            try
            {
                var result = EvaluateStatement(statement.Statement, environment, context, statement.Label);

                return context.TryClearBreak(statement.Label) ? EmptyCompletion : result;
            }
            finally
            {
                context.PopLabel();
            }
        }
    }

}
