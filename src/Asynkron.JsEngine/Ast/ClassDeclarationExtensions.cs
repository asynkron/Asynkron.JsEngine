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

extension(ClassDeclaration declaration)
    {
        private object? EvaluateClass(JsEnvironment environment,
            EvaluationContext context)
        {
            var constructorValue = CreateClassValue(declaration.Definition, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return EmptyCompletion;
            }

            environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
            return EmptyCompletion;
        }
    }

}
