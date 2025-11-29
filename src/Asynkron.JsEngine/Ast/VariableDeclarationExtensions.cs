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

extension(VariableDeclaration declaration)
    {
        private object? EvaluateVariableDeclaration(JsEnvironment environment,
            EvaluationContext context)
        {
            foreach (var declarator in declaration.Declarators)
            {
                EvaluateVariableDeclarator(declaration.Kind, declarator, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }

            return EmptyCompletion;
        }
    }

}
